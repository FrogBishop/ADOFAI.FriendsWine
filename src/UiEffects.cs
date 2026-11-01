using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FriendsWine
{
    public sealed class GraphicStretchEffect : BaseMeshEffect
    {
        private readonly List<UIVertex> vertices = new List<UIVertex>();
        public override void ModifyMesh(VertexHelper vh)
        {
            var runtime = RuntimeController.Instance;
            if (!IsActive() || runtime == null || !runtime.Active || vh.currentVertCount == 0) return;
            vertices.Clear(); vh.GetUIVertexStream(vertices);
            StretchScale s = runtime.Scale;
            if (graphic is Text)
            {
                // Legacy uGUI Text: one six-vertex triangle stream per glyph.
                for (int first = 0; first + 5 < vertices.Count; first += 6)
                {
                    Vector3 min = vertices[first].position, max = min;
                    for (int j = first + 1; j < first + 6; j++)
                    { min = Vector3.Min(min, vertices[j].position); max = Vector3.Max(max, vertices[j].position); }
                    Vector3 anchor = new Vector3((min.x + max.x) * .5f, min.y, min.z);
                    for (int j = first; j < first + 6; j++)
                    { var v = vertices[j]; v.position = Geometry.Stretch(v.position, anchor, s); vertices[j] = v; }
                }
            }
            else
            {
                Rect r = graphic.rectTransform.rect;
                Vector3 anchor = new Vector3(r.center.x, r.yMin, 0);
                for (int i = 0; i < vertices.Count; i++)
                { var v = vertices[i]; v.position = Geometry.Stretch(v.position, anchor, s); vertices[i] = v; }
            }
            vh.Clear(); vh.AddUIVertexTriangleStream(vertices);
        }
    }

    internal sealed class TmpEffect : IDisposable
    {
        internal readonly TMP_Text Text;
        private bool subscribed;
        private bool modifying;
        private bool[][] visited = new bool[0][];

        internal TmpEffect(TMP_Text text) { Text = text; Enable(); }
        internal void Enable()
        {
            if (subscribed || !Text) return;
            Text.OnPreRenderText += Modify;
            subscribed = true;
        }

        internal void Refresh()
        {
            if (!Text || !Text.isActiveAndEnabled) return;
            // Regenerate from the layout every time, never from our previous deformation.
            Text.havePropertiesChanged = true;
            Text.ForceMeshUpdate();
        }

        private void Modify(TMP_TextInfo info)
        {
            var runtime = RuntimeController.Instance;
            if (modifying || runtime == null || !runtime.Active) return;
            modifying = true;
            try
            {
                StretchScale scale = runtime.Scale;
                if (visited.Length < info.meshInfo.Length) visited = new bool[info.meshInfo.Length][];
                for (int m = 0; m < info.meshInfo.Length; m++)
                {
                    int size = info.meshInfo[m].vertices.Length;
                    if (visited[m] == null || visited[m].Length < size) visited[m] = new bool[size];
                    else Array.Clear(visited[m], 0, visited[m].Length);
                }
                for (int c = 0; c < info.characterCount; c++)
                {
                    TMP_CharacterInfo character = info.characterInfo[c];
                    if (!character.isVisible) continue;
                    int m = character.materialReferenceIndex, i = character.vertexIndex;
                    Vector3[] v = info.meshInfo[m].vertices;
                    if (i + 3 >= v.Length) continue;
                    Geometry.Quad(v, i, i + 1, i + 2, i + 3, scale);
                    for (int j = i; j < i + 4; j++) visited[m][j] = true;
                }
                // Underlines, highlights, and other extra geometry still participate.
                for (int m = 0; m < info.meshInfo.Length; m++)
                {
                    Vector3[] v = info.meshInfo[m].vertices;
                    for (int i = 0; i + 3 < info.meshInfo[m].vertexCount; i += 4)
                        if (!visited[m][i]) Geometry.Quad(v, i, i + 1, i + 2, i + 3, scale);
                }
            }
            finally { modifying = false; }
        }

        public void Dispose()
        {
            if (Text && subscribed)
            {
                Text.OnPreRenderText -= Modify;
                subscribed = false;
                Text.havePropertiesChanged = true;
                Text.ForceMeshUpdate();
            }
        }
    }
}
