using System;
using System.Collections.Generic;
using UnityEngine;

namespace FriendsWine
{
    internal static class Geometry
    {
        internal static Vector3 Bottom(Bounds b) { return new Vector3(b.center.x, b.min.y, b.center.z); }
        internal static Vector3 Stretch(Vector3 p, Vector3 anchor, StretchScale s)
        {
            return new Vector3(s.X == 1 ? p.x : anchor.x + (p.x - anchor.x) * s.X,
                               s.Y == 1 ? p.y : anchor.y + (p.y - anchor.y) * s.Y, p.z);
        }

        // Keep the rotated bottom edge fixed; preserve each quad's own axes.
        internal static void Quad(Vector3[] v, int bl, int tl, int tr, int br, StretchScale s)
        {
            Vector3 a = (v[bl] + v[br]) * .5f;
            Vector3 leftUp = v[tl] - v[bl], rightUp = v[tr] - v[br];
            v[bl] = a + (v[bl] - a) * s.X;
            v[br] = a + (v[br] - a) * s.X;
            v[tl] = v[bl] + leftUp * s.Y;
            v[tr] = v[br] + rightUp * s.Y;
        }

        internal static void ParticleQuads(Mesh mesh, List<Vector3> vertices, List<Vector2> uvs, StretchScale s)
        {
            mesh.GetVertices(vertices); mesh.GetUVs(0, uvs);
            for (int start = 0; start + 3 < vertices.Count; start += 4)
            {
                // BakeMesh emits a separate four-vertex quad for every billboard.
                int bl = start, tl = start + 1, tr = start + 2, br = start + 3;
                if (uvs.Count == vertices.Count)
                {
                    float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
                    for (int j = start; j < start + 4; j++)
                    {
                        minU = Mathf.Min(minU, uvs[j].x); maxU = Mathf.Max(maxU, uvs[j].x);
                        minV = Mathf.Min(minV, uvs[j].y); maxV = Mathf.Max(maxV, uvs[j].y);
                    }
                    if (maxU - minU > 1e-7f && maxV - minV > 1e-7f)
                    {
                        float midU = (minU + maxU) * .5f, midV = (minV + maxV) * .5f;
                        for (int j = start; j < start + 4; j++)
                        {
                            if (uvs[j].y < midV) { if (uvs[j].x < midU) bl = j; else br = j; }
                            else { if (uvs[j].x < midU) tl = j; else tr = j; }
                        }
                    }
                }
                Vector3 a = (vertices[bl] + vertices[br]) * .5f;
                Vector3 upLeft = vertices[tl] - vertices[bl], upRight = vertices[tr] - vertices[br];
                vertices[bl] = a + (vertices[bl] - a) * s.X;
                vertices[br] = a + (vertices[br] - a) * s.X;
                vertices[tl] = vertices[bl] + upLeft * s.Y;
                vertices[tr] = vertices[br] + upRight * s.Y;
            }
            mesh.SetVertices(vertices); mesh.RecalculateBounds();
        }

        internal static void WholeMesh(Mesh mesh, List<Vector3> vertices, StretchScale s)
        {
            mesh.GetVertices(vertices);
            Vector3 anchor = Bottom(mesh.bounds);
            for (int i = 0; i < vertices.Count; i++) vertices[i] = Stretch(vertices[i], anchor, s);
            mesh.SetVertices(vertices); mesh.RecalculateBounds();
        }
    }
}
