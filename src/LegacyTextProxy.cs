using System.Collections.Generic;
using UnityEngine;

namespace FriendsWine
{
    internal sealed class LegacyTextProxy : RendererProxy
    {
        private readonly TextMesh text;
        private readonly TextGenerator generator = new TextGenerator();
        private readonly Mesh mesh;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector2> uv = new List<Vector2>();
        private readonly List<Color32> colors = new List<Color32>();
        private readonly List<int> triangles = new List<int>();
        internal LegacyTextProxy(MeshRenderer r) : base(r)
        {
            text = r.GetComponent<TextMesh>();
            mesh = new Mesh { name = "FriendsWine.LegacyText", hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            proxy = root.AddComponent<MeshRenderer>(); proxy.enabled = false;
        }
        protected override bool Prepare(Camera camera, StretchScale scale)
        {
            if (!text || !text.font || string.IsNullOrEmpty(text.text)) return false;
            int size = text.fontSize > 0 ? text.fontSize : text.font.fontSize;
            if (size <= 0) size = 16;
            var settings = new TextGenerationSettings
            {
                font = text.font, fontSize = size, fontStyle = text.fontStyle, color = text.color,
                textAnchor = text.anchor, lineSpacing = text.lineSpacing, richText = text.richText,
                scaleFactor = 1, horizontalOverflow = HorizontalWrapMode.Overflow,
                verticalOverflow = VerticalWrapMode.Overflow, updateBounds = true,
                generationExtents = Vector2.zero, pivot = Vector2.zero, generateOutOfBounds = true
            };
            if (!generator.Populate(text.text, settings)) return false;
            IList<UIVertex> generated = generator.verts;
            vertices.Clear(); uv.Clear(); colors.Clear(); triangles.Clear();
            int count = generated.Count - 4; // TextGenerator appends an empty sentinel quad.
            float unit = text.characterSize * .1f;
            for (int first = 0; first + 3 < count; first += 4)
            {
                Vector3 min = generated[first].position, max = min;
                for (int j = first + 1; j < first + 4; j++)
                { min = Vector3.Min(min, generated[j].position); max = Vector3.Max(max, generated[j].position); }
                Vector3 anchor = new Vector3((min.x + max.x) * .5f, min.y, min.z);
                for (int j = first; j < first + 4; j++)
                {
                    Vector3 p = Geometry.Stretch(generated[j].position, anchor, scale) * unit;
                    p.z += text.offsetZ;
                    vertices.Add(p); uv.Add(generated[j].uv0); colors.Add(generated[j].color);
                }
                triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
                triangles.Add(first + 2); triangles.Add(first + 3); triangles.Add(first);
            }
            mesh.Clear(); mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            return count > 0;
        }
        public override void Dispose() { base.Dispose(); ((System.IDisposable)generator).Dispose(); UnityEngine.Object.Destroy(mesh); }
    }
}
