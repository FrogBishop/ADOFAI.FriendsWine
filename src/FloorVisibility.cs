#if FRIENDSWINE_SELFTEST
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FriendsWine
{
    // scrFloor uses renderer visibility to enable/disable its opacity updates. Our
    // proxy draws instead of that renderer, so relay its real camera visibility.
    internal static class FloorVisibility
    {
        private sealed class State
        {
            internal MonoBehaviour Floor;
            internal int MainId;
            internal int GlowId;
            internal int LastRenderedFrame = -1;
            internal bool Visible;
        }

        private static readonly Dictionary<int, State> floors = new Dictionary<int, State>();
        private static readonly Dictionary<int, List<State>> renderers = new Dictionary<int, List<State>>();
        private static readonly List<int> removed = new List<int>();
        private static Type floorType;
        private static MethodInfo becameVisible;
        private static MethodInfo becameInvisible;
        private static FieldInfo topGlow;
        private static bool forwarding;
#if FRIENDSWINE_SELFTEST
        private static bool baseline;
#endif

        internal static void Install(Harmony harmony)
        {
#if FRIENDSWINE_SELFTEST
            baseline = Array.IndexOf(Environment.GetCommandLineArgs(), "--friendswine-visibility-baseline") >= 0;
            if (baseline) return;
#endif
            floorType = AccessTools.TypeByName("scrFloor");
            if (floorType == null) throw new InvalidOperationException("Cannot locate ADOFAI scrFloor.");
            becameVisible = AccessTools.Method(floorType, "OnBecameVisible");
            becameInvisible = AccessTools.Method(floorType, "OnBecameInvisible");
            topGlow = AccessTools.Field(floorType, "topGlow");
            if (floorType == null || becameVisible == null || becameInvisible == null)
                throw new InvalidOperationException("Cannot locate ADOFAI floor visibility callbacks.");
            harmony.Patch(becameInvisible, prefix: new HarmonyMethod(typeof(FloorVisibility), "BeforeInvisible"));
        }

        internal static void Track(Renderer renderer)
        {
#if FRIENDSWINE_SELFTEST
            if (baseline) return;
#endif
            if (floorType == null || !renderer) return;
            var floor = renderer.GetComponent(floorType) as MonoBehaviour;
            if (!floor || floors.ContainsKey(floor.GetInstanceID())) return;
            var glow = topGlow == null ? null : topGlow.GetValue(floor) as Renderer;
            var state = new State
            {
                Floor = floor,
                MainId = renderer.GetInstanceID(),
                GlowId = glow ? glow.GetInstanceID() : 0
            };
            floors.Add(floor.GetInstanceID(), state);
            Watch(state.MainId, state);
            if (state.GlowId != 0 && state.GlowId != state.MainId) Watch(state.GlowId, state);
        }

        private static void Watch(int id, State state)
        {
            List<State> watchers;
            if (!renderers.TryGetValue(id, out watchers))
            { watchers = new List<State>(); renderers.Add(id, watchers); }
            watchers.Add(state);
        }

        private static void Unwatch(int id, State state)
        {
            List<State> watchers;
            if (id == 0 || !renderers.TryGetValue(id, out watchers)) return;
            watchers.Remove(state);
            if (watchers.Count == 0) renderers.Remove(id);
        }

        internal static void Rendered(Renderer original)
        {
            if (!original || !RuntimeController.Instance || !RuntimeController.Instance.Active) return;
            List<State> watchers;
            if (!renderers.TryGetValue(original.GetInstanceID(), out watchers)) return;
            foreach (State state in watchers)
            {
                if (!state.Floor) continue;
                state.LastRenderedFrame = Time.frameCount;
                if (!state.Visible)
                {
                    state.Visible = true;
                    Notify(state, true);
                }
            }
        }

        // Ignore only an invisible callback caused by suppressing an original whose
        // proxy is still in view. Off-screen floors continue to receive the callback.
        private static bool BeforeInvisible(MonoBehaviour __instance)
        {
            if (forwarding || !RuntimeController.Instance || !RuntimeController.Instance.Active) return true;
            State state;
            return !floors.TryGetValue(__instance.GetInstanceID(), out state) || !state.Visible;
        }

        internal static void Tick()
        {
            removed.Clear();
            foreach (var pair in floors)
            {
                State state = pair.Value;
                if (!state.Floor)
                { Unwatch(state.MainId, state); Unwatch(state.GlowId, state); removed.Add(pair.Key); continue; }
                // A camera has not rendered the floor or its glow during the last frame.
                // Root activation/deactivation per camera must not count as leaving view.
                if (state.Visible && state.LastRenderedFrame < Time.frameCount - 1)
                { state.Visible = false; Notify(state, false); }
            }
            foreach (int id in removed) floors.Remove(id);
        }

        private static void Notify(State state, bool visible)
        {
            bool previous = forwarding;
            forwarding = true;
            try { (visible ? becameVisible : becameInvisible).Invoke(state.Floor, null); }
            catch (Exception ex) { Main.LogError("Floor visibility", ex); }
            finally { forwarding = previous; }
        }

        internal static void Clear()
        {
            // Leave the real component consistent with the last actual camera visibility.
            // Unity resumes delivering its own events after the original renderer returns.
            foreach (State state in floors.Values) if (state.Floor) Notify(state, state.Visible);
            floors.Clear(); renderers.Clear(); removed.Clear();
        }
    }
}
#endif
