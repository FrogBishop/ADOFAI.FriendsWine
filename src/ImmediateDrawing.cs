using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FriendsWine
{
    internal static class ImmediateDrawing
    {
        [ThreadStatic] private static int suppression;
        [ThreadStatic] private static int depth;
        private static bool Active
        { get { return suppression == 0 && RuntimeController.Instance && RuntimeController.Instance.Active; } }

        internal static void Install(Harmony harmony)
        {
            var styleDraw = AccessTools.Method(typeof(GUIStyle), "Draw", new[]
            { typeof(Rect), typeof(GUIContent), typeof(int), typeof(bool), typeof(bool), typeof(bool), typeof(bool) });
            if (styleDraw != null) harmony.Patch(styleDraw, prefix: new HarmonyMethod(typeof(ImmediateDrawing), "StylePrefix"));
            int count = 0;
            foreach (Type type in new[] { typeof(GUI), typeof(Graphics) })
            {
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (method.GetMethodBody() == null || method.ContainsGenericParameters) continue;
                    ParameterInfo[] p = method.GetParameters();
                    bool texture = (method.Name == "DrawTexture" || method.Name == "DrawTextureWithTexCoords") && p.Length > 0 && p[0].ParameterType == typeof(Rect);
                    bool mesh = method.Name == "DrawMesh" || method.Name == "DrawMeshNow" || method.Name == "RenderMesh" || method.Name == "DrawMeshInstanced";
                    bool hasMesh = false, hasMatrix = false;
                    foreach (var parameter in p)
                    {
                        hasMesh |= parameter.ParameterType == typeof(Mesh);
                        hasMatrix |= parameter.ParameterType == typeof(Matrix4x4) || parameter.ParameterType == typeof(Matrix4x4).MakeByRefType() || parameter.ParameterType == typeof(Matrix4x4[]);
                    }
                    if (!texture && !(mesh && hasMesh && hasMatrix)) continue;
                    try
                    {
                        harmony.Patch(method,
                            prefix: new HarmonyMethod(typeof(ImmediateDrawing), texture ? "TexturePrefix" : "MeshPrefix"),
                            finalizer: new HarmonyMethod(typeof(ImmediateDrawing), "DrawFinalizer"));
                        count++;
                    }
                    catch (Exception ex) { Main.Entry.Logger.Log("Optional draw hook unavailable: " + method.Name + " / " + ex.Message); }
                }
            }
            Main.Entry.Logger.Log("Immediate draw hooks: " + count + ".");
        }

        private static Matrix4x4 Around(Vector3 anchor, StretchScale scale)
        { return Matrix4x4.Translate(anchor) * Matrix4x4.Scale(new Vector3(scale.X, scale.Y, 1)) * Matrix4x4.Translate(-anchor); }

        private static void TexturePrefix(object[] __args, out bool __state)
        {
            __state = Active;
            if (!__state) return;
            if (depth++ != 0) return;
            Rect r = (Rect)__args[0]; StretchScale s = RuntimeController.Instance.Scale;
            Vector2 bottom = new Vector2(r.center.x, r.yMax);
            __args[0] = new Rect(bottom.x + (r.x - bottom.x) * s.X, bottom.y + (r.y - bottom.y) * s.Y, r.width * s.X, r.height * s.Y);
        }

        private static void MeshPrefix(object[] __args, out bool __state)
        {
            __state = Active;
            if (!__state) return;
            if (depth++ != 0) return;
            Mesh mesh = null;
            foreach (object arg in __args) if (arg is Mesh) { mesh = (Mesh)arg; break; }
            if (!mesh) return;
            Matrix4x4 local = Around(Geometry.Bottom(mesh.bounds), RuntimeController.Instance.Scale);
            for (int i = 0; i < __args.Length; i++)
            {
                if (__args[i] is Matrix4x4) __args[i] = (Matrix4x4)__args[i] * local;
                else if (__args[i] is Matrix4x4[])
                {
                    var copy = (Matrix4x4[])((Matrix4x4[])__args[i]).Clone();
                    for (int j = 0; j < copy.Length; j++) copy[j] = copy[j] * local;
                    __args[i] = copy;
                }
            }
        }

        private static void DrawFinalizer(bool __state) { if (__state) depth--; }

        private static bool StylePrefix(GUIStyle __instance, Rect position, GUIContent content,
            int controlId, bool isHover, bool isActive, bool on, bool hasKeyboardFocus)
        {
            if (!Active || Event.current == null || Event.current.type != EventType.Repaint) return true;
            Matrix4x4 previous = GUI.matrix;
            suppression++;
            try
            {
                StretchScale scale = RuntimeController.Instance.Scale;
                GUI.matrix = previous * Around(new Vector3(position.center.x, position.yMax, 0), scale);
                var background = new GUIContent("", content.image, content.tooltip);
                __instance.Draw(position, background, isHover, isActive, on, hasKeyboardFocus);
                GUI.matrix = previous;
                if (string.IsNullOrEmpty(content.text)) return false;
                var glyphStyle = new GUIStyle(__instance)
                {
                    alignment = TextAnchor.UpperLeft, padding = new RectOffset(), margin = new RectOffset(),
                    border = new RectOffset(), contentOffset = Vector2.zero, wordWrap = false,
                    clipping = TextClipping.Overflow
                };
                ClearBackgrounds(glyphStyle);
                string text = content.text;
                var tags = new List<KeyValuePair<string, string>>();
                for (int i = 0; i < text.Length;)
                {
                    if (__instance.richText && text[i] == '<')
                    {
                        int end = text.IndexOf('>', i + 1);
                        if (end >= 0)
                        {
                            string tag = text.Substring(i + 1, end - i - 1);
                            bool closing = tag.StartsWith("/", StringComparison.Ordinal);
                            string name = tag.TrimStart('/').Split('=')[0].Trim().ToLowerInvariant();
                            if (name == "b" || name == "i" || name == "color" || name == "size")
                            {
                                if (closing)
                                {
                                    for (int t = tags.Count - 1; t >= 0; t--) if (tags[t].Key == name) { tags.RemoveAt(t); break; }
                                }
                                else tags.Add(new KeyValuePair<string, string>(name, text.Substring(i, end - i + 1)));
                                i = end + 1; continue;
                            }
                        }
                    }
                    string element = StringInfo.GetNextTextElement(text, i);
                    Vector2 cursor = __instance.GetCursorPixelPosition(position, content, i);
                    i += element.Length;
                    if (element == "\r" || element == "\n") continue;
                    string decorated = element;
                    for (int t = tags.Count - 1; t >= 0; t--) decorated = tags[t].Value + decorated + "</" + tags[t].Key + ">";
                    var glyph = new GUIContent(decorated);
                    Vector2 size = glyphStyle.CalcSize(glyph);
                    Rect rect = new Rect(cursor.x, cursor.y, Mathf.Max(1, size.x), Mathf.Max(1, size.y));
                    GUI.matrix = previous * Around(new Vector3(rect.center.x, rect.yMax, 0), scale);
                    glyphStyle.Draw(rect, glyph, isHover, isActive, on, hasKeyboardFocus);
                }
                return false;
            }
            finally { GUI.matrix = previous; suppression--; }
        }

        private static void ClearBackgrounds(GUIStyle style)
        {
            foreach (GUIStyleState state in new[] { style.normal, style.hover, style.active, style.focused, style.onNormal, style.onHover, style.onActive, style.onFocused })
            { state.background = null; }
        }
    }
}
