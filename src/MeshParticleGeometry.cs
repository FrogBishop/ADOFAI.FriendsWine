using System;
using System.Collections.Generic;
using UnityEngine;

namespace FriendsWine
{
    // Recover each baked particle's affine pose from its source vertices. This keeps
    // the source mesh's own bottom-center pivot even after arbitrary particle rotation.
    internal sealed class MeshParticleGeometry
    {
        internal readonly Mesh Source;
        private readonly Vector3[] original;
        private readonly Vector3 anchor;
        private readonly int a, b, c;
        private readonly Matrix4x4 inverseBasis;
        private readonly bool planar;
        private readonly bool valid;

        internal MeshParticleGeometry(Mesh source)
        {
            Source = source; original = source.vertices; anchor = Geometry.Bottom(source.bounds);
            if (original.Length < 3) return;
            Vector3 origin = original[0];
            for (int i = 1; i < original.Length; i++)
                if ((original[i] - origin).sqrMagnitude > 1e-10f) { a = i; break; }
            if (a == 0) return;
            Vector3 x = original[a] - origin;
            for (int i = 1; i < original.Length; i++)
                if (Vector3.Cross(x, original[i] - origin).sqrMagnitude > 1e-10f) { b = i; break; }
            if (b == 0) return;
            Vector3 y = original[b] - origin, normal = Vector3.Cross(x, y);
            for (int i = 1; i < original.Length; i++)
                if (Mathf.Abs(Vector3.Dot(normal, original[i] - origin)) > 1e-7f) { c = i; break; }
            planar = c == 0;
            inverseBasis = Basis(x, y, planar ? normal : original[c] - origin).inverse;
            valid = true;
        }

        private static Matrix4x4 Basis(Vector3 x, Vector3 y, Vector3 z)
        {
            Matrix4x4 m = Matrix4x4.identity;
            m.SetColumn(0, new Vector4(x.x, x.y, x.z, 0));
            m.SetColumn(1, new Vector4(y.x, y.y, y.z, 0));
            m.SetColumn(2, new Vector4(z.x, z.y, z.z, 0));
            return m;
        }

        internal void Apply(Mesh baked, List<Vector3> vertices, StretchScale scale)
        {
            baked.GetVertices(vertices);
            if (!valid || vertices.Count % original.Length != 0) { ApplyIslands(baked, vertices, scale); return; }
            for (int first = 0; first < vertices.Count; first += original.Length)
            {
                Vector3 p = vertices[first], x = vertices[first + a] - p, y = vertices[first + b] - p;
                Matrix4x4 pose = Basis(x, y, planar ? Vector3.Cross(x, y) : vertices[first + c] - p) * inverseBasis;
                for (int i = 0; i < original.Length; i++)
                {
                    Vector3 delta = Geometry.Stretch(original[i], anchor, scale) - original[i];
                    vertices[first + i] += pose.MultiplyVector(delta);
                }
            }
            baked.SetVertices(vertices); baked.RecalculateBounds();
        }

        internal static void ApplyIslands(Mesh mesh, List<Vector3> vertices, StretchScale scale)
        {
            mesh.GetVertices(vertices);
            if (vertices.Count == 0) return;
            int[] parent = new int[vertices.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int[] triangles = mesh.triangles;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Union(parent, triangles[i], triangles[i + 1]); Union(parent, triangles[i], triangles[i + 2]);
            }
            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < vertices.Count; i++)
            {
                int id = Find(parent, i); List<int> group;
                if (!groups.TryGetValue(id, out group)) { group = new List<int>(); groups[id] = group; }
                group.Add(i);
            }
            var uv = new List<Vector2>(); mesh.GetUVs(0, uv);
            var frames = new Dictionary<int, Matrix4x4>();
            if (uv.Count == vertices.Count)
            {
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    int i0 = triangles[i], i1 = triangles[i + 1], i2 = triangles[i + 2], id = Find(parent, i0);
                    if (frames.ContainsKey(id)) continue;
                    Vector2 d1 = uv[i1] - uv[i0], d2 = uv[i2] - uv[i0];
                    float determinant = d1.x * d2.y - d1.y * d2.x;
                    if (Mathf.Abs(determinant) < 1e-8f) continue;
                    Vector3 p1 = vertices[i1] - vertices[i0], p2 = vertices[i2] - vertices[i0];
                    Vector3 u = (p1 * d2.y - p2 * d1.y) / determinant;
                    Vector3 v = (p2 * d1.x - p1 * d2.x) / determinant;
                    if (Vector3.Cross(u, v).sqrMagnitude < 1e-12f) continue;
                    Matrix4x4 frame = Basis(u.normalized, v.normalized, Vector3.Cross(u, v).normalized);
                    frame.SetColumn(3, new Vector4(vertices[i0].x, vertices[i0].y, vertices[i0].z, 1));
                    frames[id] = frame;
                }
            }
            foreach (var pair in groups)
            {
                Matrix4x4 frame;
                if (!frames.TryGetValue(pair.Key, out frame)) frame = Matrix4x4.identity;
                Matrix4x4 inverse = frame.inverse;
                Bounds bounds = new Bounds(inverse.MultiplyPoint3x4(vertices[pair.Value[0]]), Vector3.zero);
                foreach (int index in pair.Value) bounds.Encapsulate(inverse.MultiplyPoint3x4(vertices[index]));
                Vector3 anchor = Geometry.Bottom(bounds);
                foreach (int index in pair.Value)
                    vertices[index] = frame.MultiplyPoint3x4(Geometry.Stretch(inverse.MultiplyPoint3x4(vertices[index]), anchor, scale));
            }
            mesh.SetVertices(vertices); mesh.RecalculateBounds();
        }
        private static int Find(int[] parent, int i)
        { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
        private static void Union(int[] parent, int a, int b) { parent[Find(parent, a)] = Find(parent, b); }
    }
}
