// Compiled only by build.ps1 -IntegrationTest; absent from the release DLL.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace FriendsWine
{
    public sealed class IntegrationVerification : MonoBehaviour
    {
        private string output;
        private readonly List<string> results = new List<string>();
        private readonly List<GameObject> fixtures = new List<GameObject>();
        private readonly List<SpriteRenderer> sprites = new List<SpriteRenderer>();
        private readonly Dictionary<SpriteRenderer, Sprite> originalSprites = new Dictionary<SpriteRenderer, Sprite>();
        private Camera testCamera;
        private TMP_Text text;
        private Vector3[] textBaseline;
        private Image panel;
        private Sprite sprite;
        private Sprite offsetSprite;
        private Texture2D texture;
        private int renderedChecks;
        private int failures;
        private bool checking;
        private bool finished;
        private float oldScale;
        private bool oldPause;
        private AudioSource audioSource;

        private void Check(bool condition, string message)
        {
            string line = (condition ? "PASS " : "FAIL ") + message;
            results.Add(line); Main.Entry.Logger.Log("SELFTEST " + line);
            if (!condition) failures++;
            File.WriteAllLines(Path.Combine(output, "integration-results.txt"), results.ToArray());
        }

        private IEnumerator Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "--friendswine-test-output");
            output = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Main.Entry.Path, "SelfTest");
            Directory.CreateDirectory(output);
            oldScale = Time.timeScale; oldPause = AudioListener.pause;
            var runtime = RuntimeController.Instance;
            audioSource = runtime.GetComponent<AudioSource>();
            double timeout = Time.realtimeSinceStartupAsDouble + 45;
            while (!audioSource.clip && Time.realtimeSinceStartupAsDouble < timeout) yield return null;
            Check(audioSource.clip != null, "MP3 decoded into an AudioClip");
            Check(!runtime.Active && !audioSource.isPlaying, "effect and music initially OFF");
            if (!audioSource.clip) { Finish(); yield break; }
            yield return new WaitForSecondsRealtime(8);
            HideManagerWindow();
            yield return new WaitForSecondsRealtime(.2f);
            Capture("01-game-before.png");
            DumpRenderers("renderers-before.txt");
            yield return new WaitForSecondsRealtime(.2f);
            if(Array.IndexOf(args,"--friendswine-isolate")>=0)
            {
                runtime.ToggleEffect();
                foreach(string kind in new[]{"", "ParticleSystemRenderer", "MeshRenderer", "SpriteRenderer", "TrailRenderer"})
                {
                    RendererProxy.VerificationSkip=kind;
                    yield return new WaitForSecondsRealtime(.25f);
                    Capture("isolate-"+(kind==""?"all":kind)+".png");
                    yield return new WaitForSecondsRealtime(.15f);
                }
                RendererProxy.VerificationSkip="";
                DumpRenderers("renderers-active.txt");
                Finish();yield break;
            }
            runtime.ToggleEffect();
            yield return new WaitForSecondsRealtime(.35f);
            DumpRenderers("renderers-active.txt");
            Capture("01b-game-active.png");
            yield return new WaitForSecondsRealtime(.2f);
            runtime.StopEffect();
            BuildFixtures();
            yield return new WaitForSecondsRealtime(.5f);
            if (text) { text.ForceMeshUpdate(); textBaseline = (Vector3[])text.textInfo.meshInfo[0].vertices.Clone(); }
            Capture("02-fixtures-before.png");
            runtime.ToggleEffect();
            yield return new WaitForSecondsRealtime(.3f);
            results.Add("AUDIO DIAGNOSTIC active="+runtime.Active+", enabled="+audioSource.enabled+", objectActive="+audioSource.gameObject.activeInHierarchy+
                ", playing="+audioSource.isPlaying+", samples="+audioSource.timeSamples+", clipState="+audioSource.clip.loadState+
                ", focus="+Application.isFocused+", runInBackground="+Application.runInBackground+", listenerPause="+AudioListener.pause+
                ", audioSupported="+SystemInfo.supportsAudio+", sampleRate="+AudioSettings.outputSampleRate+", cameras="+Camera.allCamerasCount);
            Check(runtime.Active && audioSource.isPlaying, "effect and BGM start together");
            Check(audioSource.ignoreListenerPause && audioSource.ignoreListenerVolume, "independent audio listener settings");
            Camera.onPreRender += InspectRender;
            checking = true;
            yield return new WaitForSecondsRealtime(.6f);
            Capture("03-fixtures-stretch.png");
            int sampleBefore = audioSource.timeSamples;
            var scaleBefore = runtime.Scale;
            Time.timeScale = 0;
            AudioListener.pause = true;
            float movement = 0;
            for (int pausedFrame=0;pausedFrame<12;pausedFrame++)
            {
                yield return new WaitForSecondsRealtime(.025f);
                movement=Mathf.Max(movement,Mathf.Abs(runtime.Scale.X-scaleBefore.X)+Mathf.Abs(runtime.Scale.Y-scaleBefore.Y));
            }
            Check(audioSource.isPlaying && audioSource.timeSamples != sampleBefore, "BGM advances while game time and listener are paused");
            Check(movement > .005f, "stretch continues with timeScale=0; movement="+movement);
            Time.timeScale = oldScale; AudioListener.pause = oldPause;
            runtime.StopEffect();
            checking = false;
            yield return new WaitForSecondsRealtime(.25f);
            Check(!runtime.Active && !audioSource.isPlaying, "effect and BGM stop together");
            bool restored = true;
            foreach (var sr in sprites) restored &= sr && !sr.forceRenderingOff && sr.transform.localScale == Vector3.one;
            Check(restored, "all fixture SpriteRenderers restored without changing game transforms");
            if (text && textBaseline != null)
            {
                text.ForceMeshUpdate();
                Vector3[] now = text.textInfo.meshInfo[0].vertices;
                float error = 0;
                for (int v = 0; v < Math.Min(textBaseline.Length, now.Length); v++) error = Mathf.Max(error, (textBaseline[v] - now[v]).magnitude);
                Check(error < .001f, "TMP vertices restored; max error=" + error);
            }
            Capture("04-fixtures-restored.png");
            for (int repeat = 0; repeat < 3; repeat++)
            {
                runtime.ToggleEffect();
                float immediateTime=audioSource.time;
                double toggleAt=Time.realtimeSinceStartupAsDouble;
                yield return new WaitForSecondsRealtime(.12f);
                double elapsed=Time.realtimeSinceStartupAsDouble-toggleAt;
                Check(audioSource.isPlaying && immediateTime < .1f && audioSource.time <= elapsed+.3,
                    "toggle " + repeat + " restarts music from beginning (elapsed="+elapsed.ToString("F2")+", audio="+audioSource.time.ToString("F2")+")");
                runtime.StopEffect();
                yield return new WaitForSecondsRealtime(.1f);
            }
            Check(renderedChecks > 0, "camera callbacks checked actual rendering");
            runtime.ToggleEffect();
            yield return new WaitForSecondsRealtime(.25f);
            audioSource.time = audioSource.clip.length - .15f;
            yield return new WaitForSecondsRealtime(.45f);
            Check(audioSource.isPlaying && audioSource.time < audioSource.clip.length*.5f,
                "BGM loops automatically across the end of the clip (audio="+audioSource.time.ToString("F2")+")");
            runtime.StopEffect();
            runtime.ToggleEffect();
            yield return new WaitForSecondsRealtime(.25f);
            var epochField=typeof(RuntimeController).GetField("startedAt",BindingFlags.Instance|BindingFlags.NonPublic);
            double epoch=(double)epochField.GetValue(runtime);
            Scene activeScene=SceneManager.GetActiveScene();
            if(activeScene.buildIndex>=0)
            {
                // Use the real game's restart entry point: it kills the outgoing scene's
                // DOTween animations before loading, unlike a raw SceneManager reload.
                bool sceneLoaded=false;
                UnityEngine.Events.UnityAction<Scene,LoadSceneMode> loaded=(scene,mode)=>{sceneLoaded=true;};
                SceneManager.sceneLoaded+=loaded;
                Type gameBase=Type.GetType("ADOBase, Assembly-CSharp");
                MethodInfo restart=gameBase==null?null:gameBase.GetMethod("RestartScene",BindingFlags.Public|BindingFlags.Static);
                Check(restart!=null,"game restart entry point located");
                if(restart!=null) restart.Invoke(null,null);
                double sceneDeadline=Time.realtimeSinceStartupAsDouble+20;
                while(!sceneLoaded&&Time.realtimeSinceStartupAsDouble<sceneDeadline)yield return null;
                SceneManager.sceneLoaded-=loaded;
                Check(sceneLoaded,"game restart completed through ADOBase.RestartScene");
                yield return new WaitForSecondsRealtime(.6f);
                Check(runtime.Active && RuntimeController.Instance==runtime && (double)epochField.GetValue(runtime)==epoch,
                    "reloading the actual game scene preserves the effect clock and runtime");
                Check(audioSource.isPlaying,"BGM continues after the game scene reload");
            }
            runtime.StopEffect();
            // Exercise settings persistence, invalid values, and a new hotkey without retaining test values.
            Settings original = Main.Settings;
            string path = Path.Combine(output, "temporary-settings.json");
            var custom = new Settings { VerticalX=.51f, VerticalY=1.81f, HorizontalX=1.63f, HorizontalY=.72f, Volume=.23f, ToggleKey="Ctrl+F9" };
            custom.Write(path); Settings roundTrip = Settings.Read(path);
            Check(roundTrip.VerticalX == .51f && roundTrip.VerticalY == 1.81f && roundTrip.HorizontalX == 1.63f && roundTrip.HorizontalY == .72f && roundTrip.Volume == .23f && roundTrip.ToggleKey == "Ctrl+F9", "six configuration values round-trip independently");
            custom.Volume = 2;
            bool rejected = false; try { custom.Validate(); } catch (ArgumentException) { rejected=true; }
            Check(rejected, "invalid volume rejected");
            Main.Settings = original;
            Finish();
        }

        private void Capture(string name) { ScreenCapture.CaptureScreenshot(Path.Combine(output, name)); }

        private void DumpRenderers(string filename)
        {
            var lines=new List<string>();
            foreach(Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if(renderer.GetComponent<ProxyMarker>())continue;
                string hierarchy=renderer.name;
                Transform parent=renderer.transform.parent;
                while(parent){hierarchy=parent.name+"/"+hierarchy;parent=parent.parent;}
                var sr=renderer as SpriteRenderer;var mf=renderer.GetComponent<MeshFilter>();
                var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
                string detail="";
                foreach(var material in renderer.sharedMaterials) if(material)
                    detail+=" mat="+material.name+" shader="+material.shader.name+" matTex="+(material.mainTexture?material.mainTexture.name:"null");
                lines.Add(renderer.GetType().Name+" | "+hierarchy+" | enabled="+renderer.enabled+" static="+renderer.isPartOfStaticBatch+
                    " | sprite="+(sr&&sr.sprite?sr.sprite.name+" texture="+sr.sprite.texture.name+" color="+sr.color:"-")+
                    " | mesh="+(mf&&mf.sharedMesh?mf.sharedMesh.name+" vertices="+mf.sharedMesh.vertexCount:"-")+
                    " | extra="+(renderer is MeshRenderer&&((MeshRenderer)renderer).additionalVertexStreams?((MeshRenderer)renderer).additionalVertexStreams.name:"null")+
                    " | MPB="+renderer.HasPropertyBlock()+" tex="+(block.GetTexture("_MainTex")?block.GetTexture("_MainTex").name:"null")+detail);
            }
            File.WriteAllLines(Path.Combine(output,filename),lines.ToArray());
        }

        private void HideManagerWindow()
        {
            Type ui = typeof(UnityModManagerNet.UnityModManager).GetNestedType("UI", BindingFlags.Public|BindingFlags.NonPublic);
            if (ui == null) return;
            object instance = null;
            var property=ui.GetProperty("Instance",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
            if(property!=null) instance=property.GetValue(null,null);
            var field=ui.GetField("Instance",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
            if(instance==null&&field!=null) instance=field.GetValue(null);
            var toggle=ui.GetMethod("ToggleWindow",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,new[]{typeof(bool)},null);
            if(instance!=null&&toggle!=null) toggle.Invoke(instance,new object[]{false});
        }

        private GameObject Fixture(string name)
        { var go = new GameObject("FriendsWine.Test." + name); go.layer = 30; DontDestroyOnLoad(go); fixtures.Add(go); return go; }

        private void BuildFixtures()
        {
            var go = Fixture("Camera"); testCamera = go.AddComponent<Camera>();
            testCamera.orthographic = true; testCamera.orthographicSize = 5;
            testCamera.transform.position = new Vector3(0,0,-20);
            testCamera.cullingMask = 1 << 30; testCamera.depth = 1000;
            testCamera.clearFlags = CameraClearFlags.SolidColor; testCamera.backgroundColor = new Color(.08f,.1f,.14f);
            texture = new Texture2D(64,64,TextureFormat.RGBA32,false);
            var pixels = new Color[64*64];
            for (int y=0;y<64;y++) for(int x=0;x<64;x++) pixels[y*64+x] = (x/8+y/8)%2 == 0 ? Color.white : new Color(.25f,.55f,.9f);
            texture.SetPixels(pixels); texture.Apply();
            sprite = Sprite.Create(texture,new Rect(0,0,64,64),new Vector2(.5f,.5f),32,0,SpriteMeshType.FullRect);
            for(int i=0;i<4;i++)
            {
                var item=Fixture("Sprite"+i); item.transform.localPosition=new Vector3(-4.5f+3*i,1.8f,0);
                item.transform.localRotation=Quaternion.Euler(0,0, i==1?35:i==2?-25:0);
                var sr=item.AddComponent<SpriteRenderer>(); sr.sprite=sprite; sprites.Add(sr);
                if(i==2) sr.flipY=true;
            }
            // Nested sprite confirms that rendering the parent does not stretch its child twice.
            var nested=Fixture("NestedSprite"); nested.transform.SetParent(sprites[3].transform,false);
            nested.transform.localPosition=new Vector3(0,-2,0); nested.transform.localRotation=Quaternion.Euler(0,0,15);
            var ns=nested.AddComponent<SpriteRenderer>(); ns.sprite=sprite; ns.color=Color.yellow; sprites.Add(ns);
            var offsetObject=Fixture("OffsetPivot");
            offsetObject.transform.position=new Vector3(2,-1.8f,0);
            offsetObject.transform.rotation=Quaternion.Euler(0,0,35);
            offsetSprite=Sprite.Create(texture,new Rect(0,0,64,64),new Vector2(.13f,.8f),32,0,SpriteMeshType.FullRect);
            var os=offsetObject.AddComponent<SpriteRenderer>();os.sprite=offsetSprite;os.flipX=true;os.flipY=true;sprites.Add(os);
            foreach(var sr in sprites)originalSprites[sr]=sr.sprite;
            var canvasObject=Fixture("Canvas"); var canvas=canvasObject.AddComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.sortingOrder=32000;
            var textObject=Fixture("TMP"); textObject.transform.SetParent(canvasObject.transform,false);
            var tmp=textObject.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset font=TMP_Settings.defaultFontAsset;
            if(!font)
            {
                var existing=UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None);
                foreach(var candidate in existing) if(candidate!=tmp&&candidate.font) {font=candidate.font;break;}
            }
            if(font)
            {
                tmp.font=font;tmp.text="Friends Wine 130 BPM";tmp.fontSize=38;tmp.color=Color.white;
                tmp.alignment=TextAlignmentOptions.Center;
                tmp.rectTransform.anchorMin=tmp.rectTransform.anchorMax=new Vector2(.5f,.15f);
                tmp.rectTransform.sizeDelta=new Vector2(800,90);text=tmp;
            }
            var panelObject=Fixture("Image"); panelObject.transform.SetParent(canvasObject.transform,false);
            panel=panelObject.AddComponent<Image>();panel.sprite=sprite;
            panel.rectTransform.anchorMin=panel.rectTransform.anchorMax=new Vector2(.5f,.4f);
            panel.rectTransform.sizeDelta=new Vector2(90,90);panel.rectTransform.localRotation=Quaternion.Euler(0,0,20);
            var particlesObject=Fixture("Particles");particlesObject.transform.position=new Vector3(-4,-1.8f,0);
            var ps=particlesObject.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.startLifetime=100;main.startSpeed=0;main.startSize=.8f;main.maxParticles=8;
            var emission=ps.emission;emission.enabled=false;
            var psRenderer=particlesObject.GetComponent<ParticleSystemRenderer>();
            psRenderer.sharedMaterial=new Material(Shader.Find("Sprites/Default"));psRenderer.sharedMaterial.mainTexture=texture;
            for(int i=0;i<3;i++) ps.Emit(new ParticleSystem.EmitParams {position=new Vector3(i*1.2f,0,0),rotation=i*25},1);
            ps.Pause();
        }

        private void InspectRender(Camera camera)
        {
            if(!checking||camera!=testCamera||renderedChecks>=12) return;
            StretchScale scale=RuntimeController.Instance.Scale;
            if(Mathf.Abs(scale.Y-1)<.03f) return;
            foreach(var sr in sprites)
            {
                if(!sr) continue;
                Bounds baseline=originalSprites[sr].bounds;
                Vector3 center=baseline.center;
                if(sr.flipX)center.x=-center.x;
                if(sr.flipY)center.y=-center.y;
                baseline.center=center;
                Vector2[] drawn=sr.sprite.vertices;
                Bounds actual=new Bounds(new Vector3(drawn[0].x*(sr.flipX?-1:1),drawn[0].y*(sr.flipY?-1:1),0),Vector3.zero);
                foreach(Vector2 p in drawn)actual.Encapsulate(new Vector3(p.x*(sr.flipX?-1:1),p.y*(sr.flipY?-1:1),0));
                Vector3 anchor=Geometry.Bottom(baseline);
                float error=(sr.transform.TransformPoint(Geometry.Bottom(actual))-sr.transform.TransformPoint(anchor)).magnitude;
                bool sizes=Mathf.Abs(actual.size.x-baseline.size.x*scale.X)<.001f && Mathf.Abs(actual.size.y-baseline.size.y*scale.Y)<.001f;
                if(error>.0001f||sr.forceRenderingOff||!sr.enabled||!sizes)
                    Check(false,"native render-time bottom anchor/visibility "+sr.name+" error="+error+", sizes="+sizes);
                Bounds world=sr.bounds;world.Expand(.002f);
                bool encloses=true;
                foreach(Vector2 p in drawn)encloses &= world.Contains(sr.transform.TransformPoint(new Vector3(p.x*(sr.flipX?-1:1),p.y*(sr.flipY?-1:1),0)));
                if(!encloses)Check(false,"native culling bounds contain rotated/flipped geometry: "+sr.name);
                if(renderedChecks==0) Check(!sr.forceRenderingOff&&sr.enabled,"original SpriteRenderer keeps its native lifecycle");
            }
            renderedChecks++;
            if(renderedChecks==12) Check(true,"12 rendered frames preserve rotated, flipped, and nested sprite anchors");
            if(renderedChecks==12&&text&&textBaseline!=null)
            {
                bool anchored=true,stretched=true;
                var info=text.textInfo;
                for(int character=0;character<info.characterCount;character++)
                {
                    var ch=info.characterInfo[character];if(!ch.isVisible||ch.materialReferenceIndex!=0)continue;
                    int v=ch.vertexIndex;Vector3[] now=info.meshInfo[0].vertices;
                    Vector3 originalBottom=(textBaseline[v]+textBaseline[v+3])*.5f;
                    Vector3 newBottom=(now[v]+now[v+3])*.5f;
                    anchored&=(originalBottom-newBottom).magnitude<.001f;
                    float originalHeight=(textBaseline[v+1]-textBaseline[v]).magnitude;
                    float actualHeight=(now[v+1]-now[v]).magnitude;
                    if(originalHeight>.001f)stretched&=Mathf.Abs(actualHeight/originalHeight-scale.Y)<.002f;
                }
                Check(anchored&&stretched,"each TMP character preserves its own bottom and follows the current scale");
            }
        }

        private void Finish()
        {
            finished=true; checking=false; Camera.onPreRender-=InspectRender;
            Time.timeScale=oldScale; AudioListener.pause=oldPause;
            if(RuntimeController.Instance) RuntimeController.Instance.StopEffect();
            results.Add("TOTAL failures="+failures+", renderFrames="+renderedChecks);
            File.WriteAllLines(Path.Combine(output,"integration-results.txt"),results.ToArray());
            Main.Entry.Logger.Log("SELFTEST COMPLETE failures="+failures);
            Application.Quit(failures==0?0:1);
        }

        private void OnDestroy()
        {
            Camera.onPreRender-=InspectRender;
            if(!finished) {Time.timeScale=oldScale;AudioListener.pause=oldPause;}
            foreach(var go in fixtures) if(go) Destroy(go);
            if(sprite) Destroy(sprite);if(offsetSprite)Destroy(offsetSprite);if(texture)Destroy(texture);
        }
    }
}
