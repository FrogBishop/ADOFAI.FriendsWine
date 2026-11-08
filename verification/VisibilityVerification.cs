// Compiled only for the opt-in game integration test, never in the release DLL.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendsWine
{
    public sealed class VisibilityVerification : MonoBehaviour
    {
        private string output;
        private readonly List<string> results = new List<string>();
        private readonly Dictionary<Camera, int> cameraMasks = new Dictionary<Camera, int>();
        private int failures;
        private MonoBehaviour floor;
        private Renderer renderer;
        private Material material;
        private FieldInfo opacity;
        private Camera camera;
        private GameObject cameraObject;
        private RenderTexture target;
        private Texture2D readback;
        private int alphaId;
        private int originalLayer;
        private float originalOpacity;
        private bool originalEnabled;
        private bool originalForceOff;
        private Vector3 cameraPosition;
        private const int TestLayer = 30;

        private void Check(bool condition, string message)
        {
            string line = (condition ? "PASS " : "FAIL ") + message;
            results.Add(line); if (!condition) failures++;
            Main.Entry.Logger.Log("VISIBILITY TEST " + line);
            File.WriteAllLines(Path.Combine(output, "visibility-results.txt"), results.ToArray());
        }

        private IEnumerator Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--friendswine-test-output");
            output = index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(Main.Entry.Path, "VisibilityTest");
            Directory.CreateDirectory(output);
            IEnumerator run = Run();
            while (true)
            {
                object next;
                try { if (!run.MoveNext()) break; next = run.Current; }
                catch (Exception ex) { Check(false, "unexpected exception: " + ex); break; }
                yield return next;
            }
            Cleanup();
            results.Add("TOTAL failures=" + failures);
            File.WriteAllLines(Path.Combine(output, "visibility-results.txt"), results.ToArray());
            Main.Entry.Logger.Log("VISIBILITY TEST COMPLETE failures=" + failures);
            Application.Quit(failures == 0 ? 0 : 1);
        }

        private void LateUpdate()
        {
            // Hidden/batch players may skip automatic camera rendering. Advance the
            // actual renderer visibility callbacks every frame before inspecting the
            // game's updater; rendering only in Pixels() observes stale material state.
            if (camera) camera.Render();
        }

        private IEnumerator Run()
        {
            var runtime = RuntimeController.Instance;
            Main.Settings = new Settings { Volume = 0 };
            runtime.ApplySettings();
            yield return new WaitForSecondsRealtime(8);
            Type baseType = Type.GetType("ADOBase, Assembly-CSharp");
            PropertyInfo controllerProperty = baseType.GetProperty("controller", BindingFlags.Static | BindingFlags.Public);
            object controller = controllerProperty.GetValue(null, null);
            controller.GetType().GetMethod("EnterLevel", new[] { typeof(string), typeof(bool) }).Invoke(controller, new object[] { "1-1", false });
            double deadline = Time.realtimeSinceStartupAsDouble + 30;
            bool loaded = false;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
                controller = controllerProperty.GetValue(null, null);
                if (!(controller is UnityEngine.Object) || !(UnityEngine.Object)controller) continue;
                object customLevel = baseType.GetProperty("customLevel", BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
                if (customLevel == null || (bool)customLevel.GetType().GetField("isLoading").GetValue(customLevel)) continue;
                if ((string)controller.GetType().GetField("levelName").GetValue(controller) == "1-1") { loaded = true; break; }
            }
            Check(loaded, "actual built-in gameplay level 1-1 loaded through EnterLevel");
            if (!loaded) yield break;
            yield return new WaitForSecondsRealtime(.4f);

            Type floorType = Type.GetType("scrFloor, Assembly-CSharp");
            FieldInfo seqId = floorType.GetField("seqID");
            foreach (UnityEngine.Object candidate in UnityEngine.Object.FindObjectsByType(floorType, FindObjectsSortMode.None))
            {
                var component = candidate as MonoBehaviour;
                if (component && (int)seqId.GetValue(component) >= 3 && (int)seqId.GetValue(component) <= 6)
                { floor = component; break; }
            }
            if (!floor) throw new InvalidOperationException("No gameplay scrFloor fixture found.");
            object floorRenderer = floorType.GetField("floorRenderer").GetValue(floor);
            renderer = (Renderer)floorRenderer.GetType().GetField("renderer").GetValue(floorRenderer);
            material = (Material)floorRenderer.GetType().GetField("material").GetValue(floorRenderer);
            opacity = floorType.GetField("opacity");
            alphaId = (int)floorType.GetField("ShaderProperty_Alpha").GetValue(null);
            originalOpacity = (float)opacity.GetValue(floor);
            originalLayer = renderer.gameObject.layer;
            originalEnabled = renderer.enabled;
            originalForceOff = renderer.forceRenderingOff;
            Check(material.HasProperty(alphaId), "real floor shader exposes its game-managed alpha");
            if (!material.HasProperty(alphaId)) yield break;
            foreach (Camera c in Camera.allCameras)
            { cameraMasks.Add(c, c.cullingMask); c.cullingMask &= ~(1 << TestLayer); }
            renderer.gameObject.layer = TestLayer;
            renderer.enabled = true;
            renderer.forceRenderingOff = false;
            cameraObject = new GameObject("FriendsWine.VisibilityTestCamera");
            DontDestroyOnLoad(cameraObject);
            camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 1.2f;
            cameraPosition = renderer.bounds.center + new Vector3(0, 0, -20);
            camera.transform.position = cameraPosition;
            camera.cullingMask = 1 << TestLayer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.03f, .05f, .08f, 1);
            target = new RenderTexture(256, 256, 24);
            camera.targetTexture = target;
            readback = new Texture2D(256, 256, TextureFormat.RGB24, false);

            opacity.SetValue(floor, 1f);
            yield return new WaitForSecondsRealtime(.3f);
            Check(Pixels("01-original-visible.png") > 100, "original floor actually appears in camera pixels");
            runtime.ToggleEffect();
            yield return new WaitForSecondsRealtime(.4f);
            Check(floor.enabled, "proxy visibility keeps the real scrFloor updater enabled");
            Check(Pixels("02-stretched-visible.png") > 100, "visible floor still renders while stretched");

            opacity.SetValue(floor, 0f);
            yield return new WaitForSecondsRealtime(.15f);
            Check(Mathf.Abs(material.GetFloat(alphaId)) < .001f, "real LateUpdate applies opacity zero while effect is ON");
            Check(Pixels("03-stretched-hidden.png") < 8, "opacity-zero floor remains absent from actual rendered pixels");
            opacity.SetValue(floor, .25f);
            yield return new WaitForSecondsRealtime(.15f);
            Check(Mathf.Abs(material.GetFloat(alphaId) - .25f) < .001f, "partial opacity follows the game's own update");
            Check(Pixels("04-stretched-quarter.png") > 30, "partly transparent floor can render again");
            opacity.SetValue(floor, 1f);
            yield return new WaitForSecondsRealtime(.15f);
            Check(Mathf.Abs(material.GetFloat(alphaId) - 1f) < .001f && Pixels("05-stretched-restored.png") > 100,
                "floor can fade back to fully visible without toggling the mod");

            camera.transform.position = cameraPosition + Vector3.right * 1000;
            yield return new WaitForSecondsRealtime(.2f);
            Check(!floor.enabled, "off-screen floor returns to the game's disabled update state");
            opacity.SetValue(floor, 0f);
            camera.transform.position = cameraPosition;
            yield return new WaitForSecondsRealtime(.25f);
            Check(floor.enabled && Mathf.Abs(material.GetFloat(alphaId)) < .001f && Pixels("06-reentered-hidden.png") < 8,
                "off-screen opacity change is applied when floor re-enters view");

            runtime.StopEffect();
            yield return new WaitForSecondsRealtime(.15f);
            Check(Pixels("07-disabled-hidden.png") < 8, "stopping the effect preserves the floor's hidden state");
            runtime.ToggleEffect();
            yield return new WaitForSecondsRealtime(.2f);
            Check(Pixels("08-reenabled-hidden.png") < 8, "enabling the effect on an already hidden floor keeps it hidden");
            opacity.SetValue(floor, 1f);
            yield return new WaitForSecondsRealtime(.15f);
            renderer.enabled = false;
            yield return new WaitForSecondsRealtime(.15f);
            Check(Pixels("09-renderer-disabled.png") < 8 && !renderer.enabled, "disabled Renderer is not made visible");
            renderer.enabled = true;
            renderer.forceRenderingOff = true;
            yield return new WaitForSecondsRealtime(.15f);
            Check(Pixels("10-force-hidden.png") < 8 && renderer.forceRenderingOff, "game forceRenderingOff is respected");
            renderer.forceRenderingOff = false;
            yield return new WaitForSecondsRealtime(.15f);
            runtime.StopEffect();
            opacity.SetValue(floor, 1f);
            yield return new WaitForSecondsRealtime(.15f);
            Check(Pixels("11-final-restored.png") > 100, "normal rendering resumes after disable");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--friendswine-track-tests") >= 0)
            {
                IEnumerator animations = TrackAnimations();
                while (animations.MoveNext()) yield return animations.Current;
            }
        }

        private struct AnimationFrame
        {
            internal Vector3 Position, Scale;
            internal Quaternion Rotation;
            internal float Opacity, Alpha;
            internal int Pixels;
        }

        private IEnumerator TrackAnimations()
        {
            RuntimeController runtime = RuntimeController.Instance;
            runtime.StopEffect();
            Settings previousSettings = Main.Settings;
            Main.Settings = new Settings { VerticalX=1, VerticalY=1, HorizontalX=1, HorizontalY=1, Volume=0 };
            runtime.ApplySettings();
            float previousTimeScale = Time.timeScale;
            Time.timeScale = 0;
            Vector3 startPosition = floor.transform.position;
            Vector3 startScale = floor.transform.localScale;
            Quaternion startRotation = floor.transform.rotation;
            var moves = (IDictionary)floor.GetType().GetField("moveTweens").GetValue(floor);
            string[][] modes = {
                new[] { "Extend", "Grow", "Grow_Spin", "Fade", "Drop", "Rise", "Assemble", "Assemble_Far" },
                new[] { "Retract", "Scatter", "Scatter_Far", "Shrink", "Shrink_Spin", "Fade" }
            };
            for (int direction=0; direction<2; direction++)
            {
                Type effectType = Type.GetType((direction==0 ? "ffxFloorAppearPlus" : "ffxFloorDisappearPlus") + ", Assembly-CSharp");
                foreach (string mode in modes[direction])
                {
                    var states = new AnimationFrame[2,3];
                    for (int enabled=0; enabled<2; enabled++)
                    {
                        if (runtime.Active) runtime.StopEffect();
                        foreach (object value in moves.Values) { var old=value as Tween; if(old!=null) old.Kill(false); }
                        moves.Clear();
                        DOTween.Kill(floor.transform, false);
                        floor.transform.position=startPosition;
                        floor.transform.localScale=startScale;
                        floor.transform.rotation=startRotation;
                        floor.GetType().GetField("tweenRot").SetValue(floor,floor.GetType().GetField("startRot").GetValue(floor));
                        opacity.SetValue(floor,1f);
                        renderer.enabled=true; renderer.forceRenderingOff=false;
                        yield return null; yield return null;
                        if(enabled==1) runtime.ToggleEffect();
                        yield return null; yield return null;

                        var effect = floor.gameObject.AddComponent(effectType);
                        effectType.GetField("runManually").SetValue(effect,true);
                        effectType.GetField("triggered").SetValue(effect,true);
                        effectType.GetField("duration").SetValue(effect,.6f);
                        FieldInfo modeField=effectType.GetField("animType");
                        modeField.SetValue(effect,Enum.Parse(modeField.FieldType,mode));
                        var existing=DOTween.PlayingTweens();
                        var before=existing==null?new HashSet<Tween>():new HashSet<Tween>(existing);
                        UnityEngine.Random.State randomState=UnityEngine.Random.state;
                        UnityEngine.Random.InitState(123456);
                        if(direction==0) effectType.GetMethod("FloorSetup").Invoke(effect,null);
                        effectType.GetMethod("StartEffect",new[]{Type.GetType("scrPlanet, Assembly-CSharp")}).Invoke(effect,new object[]{null});
                        UnityEngine.Random.state=randomState;
                        var created=new List<Tween>();
                        var playing=DOTween.PlayingTweens();
                        if(playing!=null) foreach(Tween tween in playing) if(!before.Contains(tween)) {tween.Pause(); created.Add(tween);}

                        for(int stage=0;stage<3;stage++)
                        {
                            foreach(Tween tween in created) if(tween.IsActive()) tween.Goto(.3f*stage,false);
                            yield return null; yield return null;
                            int count=Pixels("animation-"+(direction==0?"appear-":"disappear-")+mode+"-"+(enabled==0?"off-":"on-")+stage+".png");
                            states[enabled,stage]=new AnimationFrame {
                                Position=floor.transform.position, Scale=floor.transform.localScale, Rotation=floor.transform.rotation,
                                Opacity=(float)opacity.GetValue(floor), Alpha=material.GetFloat(alphaId), Pixels=count
                            };
                        }
                        foreach(Tween tween in created) if(tween.IsActive()) tween.Kill(false);
                        moves.Clear();
                        Destroy(effect);
                        if(runtime.Active) runtime.StopEffect();
                    }
                    bool same=true;
                    for(int stage=0;stage<3;stage++)
                    {
                        AnimationFrame off=states[0,stage], on=states[1,stage];
                        same &= (off.Position-on.Position).sqrMagnitude<1e-7f && (off.Scale-on.Scale).sqrMagnitude<1e-7f &&
                            Quaternion.Angle(off.Rotation,on.Rotation)<.01f && Mathf.Abs(off.Opacity-on.Opacity)<.001f &&
                            Mathf.Abs(off.Alpha-on.Alpha)<.001f && Math.Abs(off.Pixels-on.Pixels)<=Math.Max(8,off.Pixels/200);
                    }
                    string label=(direction==0?"appear ":"disappear ")+mode;
                    Check(same,"native "+label+" matches unmodified gameplay at start/middle/end; pixels off="+
                        states[0,0].Pixels+","+states[0,1].Pixels+","+states[0,2].Pixels+" on="+
                        states[1,0].Pixels+","+states[1,1].Pixels+","+states[1,2].Pixels);
                    if(direction==0 && (mode=="Fade"||mode=="Grow"||mode=="Grow_Spin"||mode=="Extend"||mode=="Drop"||mode=="Rise"))
                        Check(states[1,0].Pixels<8 && states[1,2].Pixels>100,label+" transitions from absent to visible");
                    if(direction==1 && (mode=="Fade"||mode=="Shrink"||mode=="Shrink_Spin"||mode=="Retract"))
                        Check(states[1,0].Pixels>100 && states[1,2].Pixels<8,label+" transitions from visible to absent");
                }
            }
            floor.transform.position=startPosition; floor.transform.localScale=startScale; floor.transform.rotation=startRotation;
            Time.timeScale=previousTimeScale;
            Main.Settings=previousSettings; runtime.ApplySettings();
        }

        private int Pixels(string filename)
        {
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); readback.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(output, filename), readback.EncodeToPNG());
            Color32[] pixels = readback.GetPixels32();
            Color32 background = pixels[0];
            int count = 0;
            foreach (Color32 color in pixels)
                if (Math.Abs(color.r - background.r) + Math.Abs(color.g - background.g) + Math.Abs(color.b - background.b) > 8) count++;
            Main.Entry.Logger.Log("VISIBILITY PIXELS " + filename + "=" + count + ", floor.enabled=" + floor.enabled + ", alpha=" + material.GetFloat(alphaId));
            return count;
        }

        private void Cleanup()
        {
            if (RuntimeController.Instance) RuntimeController.Instance.StopEffect();
            if (floor) opacity.SetValue(floor, originalOpacity);
            if (renderer)
            {
                renderer.gameObject.layer = originalLayer;
                renderer.enabled = originalEnabled;
                renderer.forceRenderingOff = originalForceOff;
            }
            foreach (var pair in cameraMasks) if (pair.Key) pair.Key.cullingMask = pair.Value;
            if (cameraObject) Destroy(cameraObject);
            if (target) { target.Release(); Destroy(target); }
            if (readback) Destroy(readback);
        }
    }
}
