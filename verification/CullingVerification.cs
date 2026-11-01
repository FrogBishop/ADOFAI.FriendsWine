using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace FriendsWine
{
    public sealed class RenderCallbackProbe : MonoBehaviour
    {
        internal SpriteRenderer Renderer;
        internal int WillRenderCalls;
        internal bool HideOnRender;
        private void OnWillRenderObject()
        {
            WillRenderCalls++;
            if (HideOnRender) Renderer.color = new Color(1, 1, 1, 0);
        }
    }
    // End-to-end culling work counters and pixels, including a 10,000-renderer scene.
    public sealed class CullingVerification : MonoBehaviour
    {
        private readonly List<string> results = new List<string>();
        private string output;
        private int failures;
        private GameObject group, cameraObject;
        private Camera camera;
        private RenderTexture target;
        private Texture2D texture, readback;
        private Sprite sprite;
        private SpriteRenderer edge, mover;
        private RenderCallbackProbe probe;
        private const int Layer = 29;
        private const int Count = 10000;

        private void Check(bool passed, string message)
        {
            results.Add((passed ? "PASS " : "FAIL ") + message);
            if (!passed) failures++;
            Main.Entry.Logger.Log("CULLING TEST " + results[results.Count - 1]);
            File.WriteAllLines(Path.Combine(output, "culling-results.txt"), results.ToArray());
        }

        private IEnumerator Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "--friendswine-test-output");
            output = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Main.Entry.Path, "CullingTest");
            Directory.CreateDirectory(output);
            IEnumerator routine = Run();
            while (true)
            {
                object next;
                try { if (!routine.MoveNext()) break; next = routine.Current; }
                catch (Exception ex) { Check(false, "unexpected exception: " + ex); break; }
                yield return next;
            }
            if (RuntimeController.Instance) RuntimeController.Instance.StopEffect();
            if (cameraObject) Destroy(cameraObject);
            if (group) Destroy(group);
            if (target) { target.Release(); Destroy(target); }
            if (readback) Destroy(readback);
            if (sprite) Destroy(sprite);
            if (texture) Destroy(texture);
            results.Add("TOTAL failures=" + failures);
            File.WriteAllLines(Path.Combine(output, "culling-results.txt"), results.ToArray());
            Main.Entry.Logger.Log("CULLING TEST COMPLETE failures=" + failures);
            Application.Quit(failures == 0 ? 0 : 1);
        }

        private IEnumerator Run()
        {
            Main.Settings = new Settings { VerticalX = .5f, VerticalY = 2f, HorizontalX = 2f, HorizontalY = .5f, Volume = 0 };
            RuntimeController.Instance.ApplySettings();
            yield return new WaitForSecondsRealtime(8);
            group = new GameObject("FriendsWine.CullingFixture");
            DontDestroyOnLoad(group);
            texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var pixels = new Color[64]; for (int p = 0; p < pixels.Length; p++) pixels[p] = Color.white;
            texture.SetPixels(pixels); texture.Apply();
            sprite = Sprite.Create(texture, new Rect(0, 0, 8, 8), new Vector2(.5f, .5f), 20, 0, SpriteMeshType.FullRect);
            Vector2[] originalVertices = sprite.vertices;
            cameraObject = new GameObject("FriendsWine.CullingCamera");
            DontDestroyOnLoad(cameraObject);
            camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 3; camera.aspect = 1;
            camera.transform.position = new Vector3(0, 0, -20);
            camera.cullingMask = 1 << Layer;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            target = new RenderTexture(256, 256, 24); camera.targetTexture = target;
            readback = new Texture2D(256, 256, TextureFormat.RGB24, false);
            for (int n = 0; n < Count; n++)
            {
                SpriteRenderer r = Create("Tile" + n, n < 8 ? new Vector3(-1.5f + (n % 4), -.5f + (n / 4), 0) : new Vector3(10000 + n, 10000, 0));
                if (n == 0) { probe = r.gameObject.AddComponent<RenderCallbackProbe>(); probe.Renderer = r; }
                if (n == Count - 1) mover = r;
            }
            edge = Create("Edge", new Vector3(3.3f, 0, 0));
            var runtime = RuntimeController.Instance;
            runtime.ToggleEffect();
            SetScale(2, .5f);
            camera.Render();
            int coldCreated = RenderCoordinator.LastCreated;
            int applied = RenderCoordinator.LastApplied, culled = RenderCoordinator.LastCulled;
            double coldMs = RenderCoordinator.LastApplyMilliseconds;
            Check(applied <= 10 && culled >= Count - 8, "10,000-renderer scene deforms only visible/edge tiles: applied=" + applied + ", culled=" + culled);
            Check(coldCreated <= 10, "off-screen tiles allocate no deformation resources: created=" + coldCreated);
            int edgePixels = Capture("01-stretched-edge.png", 250, 256);
            Check(edgePixels > 0, "tile outside original bounds enters view through stretching and remains rendered");
            Vector2[] after = sprite.vertices;
            bool unchanged = after.Length == originalVertices.Length;
            for (int n = 0; unchanged && n < after.Length; n++) unchanged &= (after[n] - originalVertices[n]).sqrMagnitude < 1e-12f;
            Check(unchanged, "native Sprite deformation leaves the shared source asset unchanged");
            Check(probe.WillRenderCalls > 0 && probe.Renderer.isVisible, "original renderer receives native rendering callbacks and isVisible");

            var timings = new List<double>();
            int benchCandidates=0, benchApplied=0, benchCulled=0;
            for (int sample = 0; sample < 6; sample++)
            {
                SetScale(2, .5f); camera.Render(); timings.Add(RenderCoordinator.LastApplyMilliseconds);
                benchCandidates=RenderCoordinator.LastCandidates; benchApplied=RenderCoordinator.LastApplied; benchCulled=RenderCoordinator.LastCulled;
                yield return null;
            }
            timings.Sort();
            results.Add("PERF candidates=" + benchCandidates + ", applied=" + benchApplied + ", culled=" + benchCulled +
                ", coldMs=" + coldMs.ToString("F3") + ", warmMedianMs=" + timings[timings.Count / 2].ToString("F3"));
            Main.Entry.Logger.Log(results[results.Count - 1]);

            probe.HideOnRender = true;
            SetScale(2, .5f); camera.Render();
            Check(Capture("03-render-callback-hidden.png", 45, 85, 98, 115) == 0,
                "a visibility change in the original render callback is honored in the same draw");
            probe.HideOnRender = false; probe.Renderer.color = Color.white;

            mover.transform.position = new Vector3(0, 1.5f, 0);
            SetScale(2, .5f); camera.Render();
            Check(RenderCoordinator.LastApplied == applied + 1, "moving a previously culled tile into view updates immediately");
            mover.transform.position = new Vector3(20000, 10000, 0);
            SetScale(2, .5f); camera.Render();
            Check(RenderCoordinator.LastApplied == applied, "moving a tile back outside stops deformation immediately");
            Check(mover.sprite == sprite && !mover.forceRenderingOff, "culled tile retains its original renderer and sprite");
            edge.transform.position = new Vector3(20000, 20000, 0);
            SetScale(2, .5f); camera.Render();
            Check(Capture("02-edge-culled.png", 250, 256) == 0, "no stale geometry remains after edge tile leaves view");

            var secondaryObject=new GameObject("FriendsWine.SecondaryCullingCamera");
            var secondary=secondaryObject.AddComponent<Camera>();
            secondary.enabled=false;secondary.orthographic=true;secondary.orthographicSize=3;
            secondary.transform.position=new Vector3(20000,10000,-20);
            secondary.cullingMask=1<<Layer;secondary.targetTexture=target;
            SetScale(2,.5f);secondary.Render();
            Check(RenderCoordinator.LastApplied>0,"another camera can deform tiles that are outside the first camera");
            SetScale(2,.5f);camera.Render();
            Check(RenderCoordinator.LastApplied==applied-1,"switching cameras restores the first camera's culling set");
            Destroy(secondaryObject);

            // Perspective, rotations, nonuniform scale, and camera movement must use
            // the same conservative envelope as the actual camera pass.
            var check = new RenderVisibility();
            check.SetCamera(camera);
            Check(check.Intersects(new Bounds(new Vector3(3.3f, 0, 0), new Vector3(.4f, .4f, .1f)), 3), "expanded boundary is retained by the conservative frustum");
            Check(!check.Intersects(new Bounds(new Vector3(100, 0, 0), Vector3.one), 3), "fully outside bounds are rejected");
            camera.orthographic = false; camera.fieldOfView = 60;
            check.SetCamera(camera);
            Check(check.Intersects(new Bounds(Vector3.zero, Vector3.one), 3), "perspective camera includes an object in front");
            Check(!check.Intersects(new Bounds(new Vector3(0, 0, -100), Vector3.one), 3), "perspective camera rejects an object behind it");
            runtime.StopEffect();
            Check(edge.sprite == sprite && !edge.forceRenderingOff, "disable restores native sprites after culling transitions");
        }

        private SpriteRenderer Create(string name, Vector3 position)
        {
            var go = new GameObject("FriendsWine.Test." + name); go.layer = Layer;
            go.transform.SetParent(group.transform, false); go.transform.position = position;
            var renderer = go.AddComponent<SpriteRenderer>(); renderer.sprite = sprite;
            return renderer;
        }

        private static void SetScale(float x, float y)
        {
            typeof(RuntimeController).GetProperty("Scale", BindingFlags.Public | BindingFlags.Instance)
                .SetValue(RuntimeController.Instance, new StretchScale(x, y), null);
        }

        private int Capture(string filename, int minX, int maxX, int minY=0, int maxY=256)
        {
            RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); readback.Apply(); RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(output, filename), readback.EncodeToPNG());
            Color32[] pixels = readback.GetPixels32(); int count = 0;
            for (int y = minY; y < maxY; y++) for (int x = minX; x < maxX; x++)
            { Color32 c = pixels[y * 256 + x]; if (c.r + c.g + c.b > 24) count++; }
            return count;
        }
    }
}
