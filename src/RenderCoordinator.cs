using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FriendsWine
{
    internal sealed class RenderCoordinator : IDisposable
    {
        private sealed class Entry
        {
            internal Renderer Original;
            internal GameObject Object;
            internal RenderEffect Effect;
            internal bool Ignored;
        }
        private readonly Dictionary<int, Entry> renderers = new Dictionary<int, Entry>();
        private readonly List<RenderEffect> applied = new List<RenderEffect>();
        private readonly RenderVisibility visibility = new RenderVisibility();
        private readonly RenderVisibility uiVisibility = new RenderVisibility();
        private readonly Dictionary<int, TmpEffect> texts = new Dictionary<int, TmpEffect>();
        private readonly Dictionary<int, GraphicStretchEffect> graphics = new Dictionary<int, GraphicStretchEffect>();
        private readonly HashSet<string> unsupported = new HashSet<string>();
        private readonly Stack<Camera> cameras = new Stack<Camera>();
        private readonly Harmony harmony = new Harmony("FriendsWine.Rendering");
        private bool enabled;
        private double nextScan;
        private int scanCount;
#if FRIENDSWINE_SELFTEST
        internal static bool LegacyVerification;
        internal static int LastCandidates, LastCulled, LastApplied, LastCreated, TotalCreated;
        internal static double LastApplyMilliseconds;
#endif

        internal string Coverage
        {
            get { return string.Format("渲染对象 {0} · 文本 {1} · UI 图形 {2}", renderers.Count, texts.Count, graphics.Count); }
        }

        internal void Enable()
        {
            if (enabled) return;
            enabled = true;
            Camera.onPreCull += BeforeCamera;
            Camera.onPostRender += AfterCamera;
            RenderPipelineManager.beginCameraRendering += BeforeSrpCamera;
            RenderPipelineManager.endCameraRendering += AfterSrpCamera;
            SceneManager.sceneLoaded += SceneLoaded;
#if FRIENDSWINE_SELFTEST
            LegacyVerification = Array.IndexOf(Environment.GetCommandLineArgs(), "--friendswine-legacy-rendering") >= 0;
            if (LegacyVerification) FloorVisibility.Install(harmony);
#endif
            ImmediateDrawing.Install(harmony);
            Scan();
        }

        private void SceneLoaded(Scene scene, LoadSceneMode mode) { nextScan = 0; }
        internal void Tick()
        {
            // An interrupted/nested camera must not leave originals hidden into gameplay.
            if (cameras.Count != 0) { RestoreRenderers(); cameras.Clear(); }
#if FRIENDSWINE_SELFTEST
            if (LegacyVerification) FloorVisibility.Tick();
#endif
            if (Time.realtimeSinceStartupAsDouble >= nextScan) Scan();
        }

        private void Scan()
        {
            nextScan = Time.realtimeSinceStartupAsDouble + .2;
            var dead = new List<int>();
            foreach (var pair in renderers) if (!pair.Value.Original) { if (pair.Value.Effect != null) pair.Value.Effect.Dispose(); dead.Add(pair.Key); }
            foreach (int id in dead) renderers.Remove(id);
            dead.Clear();
            foreach (var pair in texts) if (!pair.Value.Text) { pair.Value.Dispose(); dead.Add(pair.Key); }
            foreach (int id in dead) texts.Remove(id);
            dead.Clear();
            foreach (var pair in graphics) if (!pair.Value) dead.Add(pair.Key);
            foreach (int id in dead) graphics.Remove(id);

            foreach (TMP_Text text in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
            {
                int id = text.GetInstanceID();
                if (!texts.ContainsKey(id)) texts.Add(id, new TmpEffect(text));
            }
            foreach (Graphic graphic in UnityEngine.Object.FindObjectsByType<Graphic>(FindObjectsSortMode.None))
            {
                if (graphic is TMP_Text || graphic is TMP_SubMeshUI) continue;
                int id = graphic.GetInstanceID();
                if (!graphics.ContainsKey(id))
                {
                    var effect = graphic.GetComponent<GraphicStretchEffect>();
                    if (!effect) effect = graphic.gameObject.AddComponent<GraphicStretchEffect>();
                    effect.enabled = true;
                    graphics.Add(id, effect);
                }
            }
            foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                int id = renderer.GetInstanceID();
                if (renderers.ContainsKey(id)) continue;
                renderers.Add(id, new Entry { Original = renderer, Object = renderer.gameObject });
            }
            if (scanCount++ == 0) Main.Entry.Logger.Log("Initial coverage: " + Coverage);
        }

        private RenderEffect CreateEffect(Renderer renderer)
        {
            if (renderer.GetComponent<ProxyMarker>() || renderer.GetComponent<TMP_Text>() || renderer.GetComponent<TMP_SubMesh>()) return null;
            RenderEffect effect = null;
            bool legacy = false;
#if FRIENDSWINE_SELFTEST
            legacy = LegacyVerification;
#endif
            if (renderer is SpriteRenderer) effect = legacy ? (RenderEffect)new SpriteProxy((SpriteRenderer)renderer) : new NativeSpriteEffect((SpriteRenderer)renderer);
            else if (renderer is SpriteMask) effect = new MaskProxy((SpriteMask)renderer);
            else if (renderer is ParticleSystemRenderer) effect = new ParticleProxy((ParticleSystemRenderer)renderer);
            else if (renderer is SkinnedMeshRenderer || renderer is LineRenderer || renderer is TrailRenderer) effect = new BakedProxy(renderer);
            else if (renderer is MeshRenderer)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter) effect = legacy ? (RenderEffect)new MeshProxy((MeshRenderer)renderer, filter) : new NativeMeshEffect((MeshRenderer)renderer, filter);
                else if (renderer.GetComponent<TextMesh>()) effect = new LegacyTextProxy((MeshRenderer)renderer);
            }
            var proxy = effect as RendererProxy;
            if (proxy != null) proxy.Protect();
            if (effect == null && unsupported.Add(renderer.GetType().FullName))
                Main.Entry.Logger.Log("Renderer has no adapter: " + renderer.GetType().FullName + " / " + renderer.name);
            return effect;
        }

        internal void PrepareUi()
        {
            foreach (var effect in texts.Values)
            {
                if (!effect.Text || !effect.Text.isActiveAndEnabled) continue;
                var ui = effect.Text as TextMeshProUGUI;
                if (ui ? !uiVisibility.GraphicVisible(ui) : !uiVisibility.InAnyCamera(effect.Text.GetComponent<Renderer>())) continue;
                try { effect.Refresh(); }
                catch (Exception ex) { Main.LogError("TMP refresh", ex); }
            }
            foreach (var effect in graphics.Values)
            {
                if (!effect || !effect.isActiveAndEnabled) continue;
                var graphic = effect.GetComponent<Graphic>();
                if (uiVisibility.GraphicVisible(graphic)) graphic.SetVerticesDirty();
            }
        }

        private void BeforeSrpCamera(ScriptableRenderContext context, Camera camera) { BeforeCamera(camera); }
        private void AfterSrpCamera(ScriptableRenderContext context, Camera camera) { AfterCamera(camera); }
        private void BeforeCamera(Camera camera)
        {
            if (!enabled || !RuntimeController.Instance || !RuntimeController.Instance.Active) return;
            if (cameras.Count != 0 && cameras.Peek() == camera) return;
            if (cameras.Count != 0) RestoreRenderers();
            cameras.Push(camera);
            ApplyRenderers(camera);
        }
        private void ApplyRenderers(Camera camera)
        {
            visibility.SetCamera(camera);
            float expansion = RenderVisibility.Expansion;
            StretchScale scale = RuntimeController.Instance.Scale;
            int mask = camera.cullingMask;
#if FRIENDSWINE_SELFTEST
            var clock = System.Diagnostics.Stopwatch.StartNew();
            LastCandidates = LastCulled = LastApplied = LastCreated = 0;
#endif
            foreach (Entry entry in renderers.Values)
            {
                Renderer renderer = entry.Original;
                if (entry.Ignored || !renderer || (entry.Effect != null && entry.Effect.Failed)) continue;
                // Reject layers/disabled components before querying bounds, since a
                // particle/skinned bounds query can synchronize engine jobs.
                if ((mask & (1 << entry.Object.layer)) == 0 || !renderer.enabled) continue;
#if FRIENDSWINE_SELFTEST
                LastCandidates++;
                if (!LegacyVerification && !visibility.Intersects(renderer.bounds, expansion)) { LastCulled++; continue; }
#else
                if (!visibility.Intersects(renderer.bounds, expansion)) continue;
#endif
                // Far-away entries need only a bounds query: avoid several engine
                // property calls, material reads, allocation, and geometry work for them.
                if (!entry.Object.activeInHierarchy || renderer.forceRenderingOff) continue;
                try
                {
                    if (entry.Effect == null)
                    {
                        entry.Effect = CreateEffect(renderer);
                        if (entry.Effect == null) { entry.Ignored = true; continue; }
#if FRIENDSWINE_SELFTEST
                        LastCreated++; TotalCreated++;
#endif
                    }
                    if (entry.Effect.Begin(camera, scale))
                    {
                        applied.Add(entry.Effect);
#if FRIENDSWINE_SELFTEST
                        LastApplied++;
#endif
                    }
                }
                catch (Exception ex)
                {
                    if (entry.Effect != null) { entry.Effect.End(); entry.Effect.Failed = true; }
                    else entry.Ignored = true;
                    Main.LogError("Rendering " + renderer.name, ex);
                }
            }
#if FRIENDSWINE_SELFTEST
            LastApplyMilliseconds = clock.Elapsed.TotalMilliseconds;
#endif
        }
        private void AfterCamera(Camera camera)
        {
            if (cameras.Count == 0 || cameras.Peek() != camera) return;
            RestoreRenderers(); cameras.Pop();
            if (cameras.Count != 0) ApplyRenderers(cameras.Peek());
        }
        private void RestoreRenderers()
        {
            foreach (RenderEffect effect in applied)
            {
                try { effect.End(); }
                catch (Exception ex) { effect.Failed = true; Main.LogError("Restore renderer", ex); }
            }
            applied.Clear();
        }

        internal void Disable()
        {
            if (!enabled) return;
            enabled = false;
            Camera.onPreCull -= BeforeCamera;
            Camera.onPostRender -= AfterCamera;
            RenderPipelineManager.beginCameraRendering -= BeforeSrpCamera;
            RenderPipelineManager.endCameraRendering -= AfterSrpCamera;
            SceneManager.sceneLoaded -= SceneLoaded;
            cameras.Clear(); RestoreRenderers();
#if FRIENDSWINE_SELFTEST
            if (LegacyVerification) FloorVisibility.Clear();
#endif
            harmony.UnpatchAll("FriendsWine.Rendering");
            foreach (Entry entry in renderers.Values) if (entry.Effect != null) entry.Effect.Dispose();
            renderers.Clear();
            foreach (var effect in texts.Values) effect.Dispose();
            texts.Clear();
            foreach (var effect in graphics.Values)
            {
                if (!effect) continue;
                effect.enabled = false;
                var graphic = effect.GetComponent<Graphic>();
                if (graphic) graphic.SetVerticesDirty();
            }
            graphics.Clear();
            Canvas.ForceUpdateCanvases();
            nextScan = 0;
        }
        public void Dispose()
        {
            Disable();
            foreach (var effect in UnityEngine.Object.FindObjectsByType<GraphicStretchEffect>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                UnityEngine.Object.Destroy(effect);
        }
    }
}
