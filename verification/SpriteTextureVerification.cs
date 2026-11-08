// Real-pixel comparison against a transform-scaled reference, not just mesh bounds.
// Compiled only by build.ps1 -IntegrationTest.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FriendsWine
{
    public sealed class SpriteTextureVerification : MonoBehaviour
    {
        private const int Size = 256;
        private const int Layer = 29;
        private readonly List<string> results = new List<string>();
        private string output;
        private int failures;
        private GameObject fixture, cameraObject;
        private Camera camera;
        private SpriteRenderer renderer;
        private RenderTexture target;
        private Texture2D readback;
        private Material material;
        private Texture2D atlasTexture;
        private Sprite atlasSprite;

        private void Check(bool passed, string message)
        {
            results.Add((passed ? "PASS " : "FAIL ") + message);
            if (!passed) failures++;
            Main.Entry.Logger.Log("SPRITE TEXTURE TEST " + results[results.Count - 1]);
            File.WriteAllLines(Path.Combine(output, "sprite-texture-results.txt"), results.ToArray());
        }

        private IEnumerator Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "--friendswine-test-output");
            output = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Main.Entry.Path, "SpriteTextureTest");
            Directory.CreateDirectory(output);
            yield return new WaitForSecondsRealtime(8);
            try { Run(); }
            catch (Exception ex) { Check(false, "unexpected exception: " + ex); }
            if (fixture) Destroy(fixture);
            if (cameraObject) Destroy(cameraObject);
            if (target) { target.Release(); Destroy(target); }
            if (readback) Destroy(readback);
            if (material) Destroy(material);
            if (atlasSprite) Destroy(atlasSprite);
            if (atlasTexture) Destroy(atlasTexture);
            results.Add("TOTAL failures=" + failures);
            File.WriteAllLines(Path.Combine(output, "sprite-texture-results.txt"), results.ToArray());
            Application.Quit(failures == 0 ? 0 : 1);
        }

        private void Run()
        {
            RuntimeController.Instance.StopEffect();
            fixture = new GameObject("FriendsWine.SpriteTextureFixture");
            fixture.layer = Layer;
            renderer = fixture.AddComponent<SpriteRenderer>();
            material = new Material(Shader.Find("Sprites/Default"));
            renderer.sharedMaterial = material;
            cameraObject = new GameObject("FriendsWine.SpriteTextureCamera");
            camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true; camera.aspect = 1;
            camera.cullingMask = 1 << Layer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.035f, .055f, .085f, 1);
            target = new RenderTexture(Size, Size, 24);
            camera.targetTexture = target;
            readback = new Texture2D(Size, Size, TextureFormat.RGB24, false);
            var faces = new List<Sprite>();
            foreach (Sprite sprite in Resources.FindObjectsOfTypeAll<Sprite>())
                if (sprite.name == "emoji_sweatsmile_base" || sprite.name == "emoji_sweatsmile_details" ||
                    sprite.name == "emoji_upsidedown_base" || sprite.name == "emoji_tongue_base")
                    faces.Add(sprite);
            Check(faces.Count >= 2, "real game emoji sprites loaded: " + faces.Count);
            using (var effect = new NativeSpriteEffect(renderer))
            {
                foreach (Sprite sprite in faces)
                {
                    RunCase(effect, sprite, StretchScale.Identity, false, false, 0, "identity");
                    RunCase(effect, sprite, new StretchScale(1.35f, .75f), false, false, 0, "horizontal");
                    RunCase(effect, sprite, new StretchScale(.75f, 1.35f), false, false, 0, "vertical");
                    RunCase(effect, sprite, new StretchScale(1.35f, .75f), true, true, 31, "flipped-rotated");
                    RunCase(effect, sprite, new StretchScale(1.35f, .75f), false, false, 0, "horizontal-repeat");
                }
                // A sub-rectangle with an off-center pivot catches UV remapping to the
                // complete atlas or to a nearby sprite, even when the geometry is right.
                atlasTexture = new Texture2D(128, 64, TextureFormat.RGBA32, false);
                var pixels = new Color[128 * 64];
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 128; x++)
                        pixels[y * 128 + x] = x < 64 ? Color.red :
                            ((x - 96) * (x - 96) + (y - 32) * (y - 32) < 28 * 28 ? Color.cyan : Color.clear);
                atlasTexture.SetPixels(pixels); atlasTexture.Apply();
                atlasSprite = Sprite.Create(atlasTexture, new Rect(64, 0, 64, 64), new Vector2(.13f, .8f), 64, 0, SpriteMeshType.FullRect);
                atlasSprite.name = "offset-atlas";
                RunCase(effect, atlasSprite, new StretchScale(1.35f, .75f), false, false, 0, "horizontal");
                RunCase(effect, atlasSprite, new StretchScale(.75f, 1.35f), true, true, 31, "flipped-vertical");

                bool planetFound = false;
                foreach (SpriteRenderer source in UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
                {
                    if (!source.sprite || !source.sharedMaterial || source.sharedMaterial.shader.name != "ADOFAI/Frames") continue;
                    renderer.sharedMaterial = source.sharedMaterial;
                    var properties = new MaterialPropertyBlock();
                    source.GetPropertyBlock(properties); renderer.SetPropertyBlock(properties);
                    RunCase(effect, source.sprite, new StretchScale(1.35f, .75f), false, false, 0, "planet-horizontal");
                    RunCase(effect, source.sprite, new StretchScale(.75f, 1.35f), false, false, 0, "planet-vertical");
                    planetFound = true;
                    break;
                }
                Check(planetFound, "real animated planet material covered");
            }
        }

        private void RunCase(NativeSpriteEffect effect, Sprite sprite, StretchScale scale, bool flipX, bool flipY, float rotation, string name)
        {
            string label = sprite.name + "-" + name;
            renderer.sprite = sprite; renderer.flipX = flipX; renderer.flipY = flipY;
            renderer.color = sprite.name.EndsWith("_base", StringComparison.Ordinal) ? new Color(.4f, .08f, 1, 1) : Color.white;
            Quaternion turn = Quaternion.Euler(0, 0, rotation);
            fixture.transform.SetPositionAndRotation(Vector3.zero, turn);
            fixture.transform.localScale = Vector3.one;
            // The renderer's native AABB may still cache the previous sprite until
            // its next render. Build the reference from the untouched source asset.
            Bounds bounds = sprite.bounds;
            Vector3 center = bounds.center;
            bounds.center = new Vector3(flipX ? -center.x : center.x, flipY ? -center.y : center.y, center.z);
            Vector3 anchor = Geometry.Bottom(bounds);
            camera.orthographicSize = Mathf.Max(bounds.size.x, bounds.size.y) * 1.1f;
            camera.transform.position = turn * bounds.center + new Vector3(0, 0, -20);
            Vector2[] originalVertices = sprite.vertices, originalUvs = sprite.uv;

            // The oracle scales the complete original sprite with an object transform.
            fixture.transform.localScale = new Vector3(scale.X, scale.Y, 1);
            fixture.transform.position = turn * new Vector3(anchor.x * (1 - scale.X), anchor.y * (1 - scale.Y), 0);
            Color32[] expected = Capture(label + "-expected.png");
            fixture.transform.SetPositionAndRotation(Vector3.zero, turn);
            fixture.transform.localScale = Vector3.one;

            try
            {
                Check(effect.Begin(camera, scale), label + " effect applied");
                Color32[] actual = Capture(label + "-actual.png");
                Compare(expected, actual, label);
                Vector2[] actualUvs = renderer.sprite.uv;
                float uvError = 0;
                for (int v = 0; v < originalUvs.Length; v++) uvError = Mathf.Max(uvError, (originalUvs[v] - actualUvs[v]).magnitude);
                Check(uvError < 1e-6f, label + " original texture mapping retained: uvError=" + uvError);
            }
            finally { effect.End(); }
            Check(renderer.sprite == sprite && fixture.transform.localScale == Vector3.one,
                label + " source sprite and transform restored");
            bool unchanged = true;
            Vector2[] afterVertices = sprite.vertices, afterUvs = sprite.uv;
            for (int v = 0; v < originalVertices.Length; v++)
                unchanged &= originalVertices[v] == afterVertices[v] && originalUvs[v] == afterUvs[v];
            Check(unchanged, label + " shared source geometry and UVs unchanged");
        }

        private Color32[] Capture(string filename)
        {
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); readback.Apply();
                File.WriteAllBytes(Path.Combine(output, filename), readback.EncodeToPNG());
                return readback.GetPixels32();
            }
            finally { RenderTexture.active = previous; }
        }

        private void Compare(Color32[] expected, Color32[] actual, string label)
        {
            int changed = 0;
            int visible = 0;
            long totalError = 0;
            for (int p = 0; p < expected.Length; p++)
            {
                if (Math.Abs(expected[p].r - expected[0].r) + Math.Abs(expected[p].g - expected[0].g) + Math.Abs(expected[p].b - expected[0].b) > 24) visible++;
                int error = Math.Abs(expected[p].r - actual[p].r) + Math.Abs(expected[p].g - actual[p].g) + Math.Abs(expected[p].b - actual[p].b);
                totalError += error;
                if (error > 24) changed++;
            }
            double average = totalError / (double)(expected.Length * 3);
            Check(visible > 100 && changed < expected.Length * .005 && average < .25,
                label + " matches whole-image scaling: visiblePixels=" + visible + ", mismatchedPixels=" + changed + ", meanChannelError=" + average.ToString("F4"));
        }
    }
}
