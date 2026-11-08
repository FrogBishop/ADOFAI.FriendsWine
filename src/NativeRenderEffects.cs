using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;

namespace FriendsWine
{
    internal abstract class RenderEffect : IDisposable
    {
        internal readonly Renderer Original;
        internal bool Failed;
        protected RenderEffect(Renderer original) { Original = original; }
        internal abstract bool Begin(Camera camera, StretchScale scale);
        internal abstract void End();
        public abstract void Dispose();
    }

    // Preserve the actual SpriteRenderer, its materials, and every native visibility
    // callback. Substitute geometry for a render while preserving its texture mapping.
    internal sealed class NativeSpriteEffect : RenderEffect
    {
        private static readonly Action<SpriteRenderer, Bounds> setBounds =
            (Action<SpriteRenderer, Bounds>)Delegate.CreateDelegate(typeof(Action<SpriteRenderer, Bounds>),
                typeof(SpriteRendererDataAccessExtensions).GetMethod("SetLocalAABB", BindingFlags.Static | BindingFlags.NonPublic,
                    null, new[] { typeof(SpriteRenderer), typeof(Bounds) }, null));

        private readonly SpriteRenderer source;
        private Sprite sprite;
        private Sprite deformed;
        private Vector2[] originalVertices;
        private NativeArray<Vector3> positions;
        private NativeArray<Vector2> uvs;
        private Bounds previousBounds;
        private bool applied;
        private SpriteProxy fallback;

        internal NativeSpriteEffect(SpriteRenderer renderer) : base(renderer) { source = renderer; }

        internal override bool Begin(Camera camera, StretchScale scale)
        {
            if (!source.sprite) return false;
            if (source.drawMode != SpriteDrawMode.Simple)
            {
                if (fallback == null) { fallback = new SpriteProxy(source); fallback.Protect(); }
                return fallback.Begin(camera, scale);
            }
            if (sprite != source.sprite)
            {
                sprite = source.sprite;
                if (deformed) UnityEngine.Object.Destroy(deformed);
                deformed = UnityEngine.Object.Instantiate(sprite);
                deformed.name = sprite.name;
                deformed.hideFlags = HideFlags.HideAndDontSave;
                originalVertices = sprite.vertices;
                if (positions.IsCreated) positions.Dispose();
                if (uvs.IsCreated) uvs.Dispose();
                positions = new NativeArray<Vector3>(originalVertices.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                uvs = new NativeArray<Vector2>(sprite.uv, Allocator.Persistent);
            }
            previousBounds = source.localBounds;
            Bounds geometryBounds = sprite.bounds;
            Vector3 anchor = new Vector3(geometryBounds.center.x, source.flipY ? geometryBounds.max.y : geometryBounds.min.y, 0);
            for (int i = 0; i < originalVertices.Length; i++)
            {
                Vector2 p = originalVertices[i];
                positions[i] = new Vector3(scale.X == 1 ? p.x : anchor.x + (p.x - anchor.x) * scale.X,
                    scale.Y == 1 ? p.y : anchor.y + (p.y - anchor.y) * scale.Y, 0);
            }
            // Unity regenerates UVs when the position channel changes. Restore the
            // source UVs AFTER every position update, or stretching moves the sampled
            // texture window instead of stretching the complete image (including atlases).
            deformed.SetVertexAttribute(VertexAttribute.Position, positions);
            deformed.SetVertexAttribute(VertexAttribute.TexCoord0, uvs);
            source.sprite = deformed;
            applied = true;
            // Keep native visibility stable over the full beat at screen edges. A
            // temporary compression must not suspend the game's animation updater.
            setBounds(source, RenderVisibility.LocalEnvelope(previousBounds));
            return true;
        }

        internal override void End()
        {
            if (fallback != null) fallback.End();
            if (applied && source)
            {
                if (source.sprite == deformed)
                {
                    source.sprite = sprite;
                    setBounds(source, previousBounds);
                }
            }
            applied = false;
        }

        public override void Dispose()
        {
            End();
            if (positions.IsCreated) positions.Dispose();
            if (uvs.IsCreated) uvs.Dispose();
            if (deformed) UnityEngine.Object.Destroy(deformed);
            if (fallback != null) fallback.Dispose();
        }
    }

    // The original MeshRenderer stays enabled and visible to the engine. FloorMesh's
    // cached source meshes, colliders, transforms and animation materials are untouched.
    internal sealed class NativeMeshEffect : RenderEffect
    {
        private readonly MeshRenderer source;
        private readonly MeshFilter filter;
        private readonly bool immutable;
        private Mesh original;
        private Mesh deformed;
        private bool applied;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector4> tangents = new List<Vector4>();
        private readonly List<Vector4> uvs = new List<Vector4>();
        private readonly List<Color32> colors = new List<Color32>();
        private MeshProxy fallback;

        internal NativeMeshEffect(MeshRenderer renderer, MeshFilter meshFilter) : base(renderer)
        { source = renderer; filter = meshFilter; immutable = renderer.GetComponent("FloorMesh") != null; }

        internal override bool Begin(Camera camera, StretchScale scale)
        {
            Mesh current = filter.sharedMesh;
            if (!current) return false;
            if (!current.isReadable || source.isPartOfStaticBatch || source.additionalVertexStreams)
            {
                if (fallback == null) { fallback = new MeshProxy(source, filter); fallback.Protect(); }
                return fallback.Begin(camera, scale);
            }
            bool changed = current != original || !deformed || current.vertexCount != deformed.vertexCount || current.subMeshCount != deformed.subMeshCount;
            if (changed)
            {
                if (deformed) UnityEngine.Object.Destroy(deformed);
                original = current;
                deformed = UnityEngine.Object.Instantiate(current);
                deformed.name = current.name;
                deformed.hideFlags = HideFlags.HideAndDontSave;
                deformed.MarkDynamic();
            }
            original.GetVertices(vertices);
            Vector3 anchor = Geometry.Bottom(original.bounds);
            for (int i = 0; i < vertices.Count; i++) vertices[i] = Geometry.Stretch(vertices[i], anchor, scale);
            deformed.SetVertices(vertices);
            original.GetNormals(normals);
            for (int i = 0; i < normals.Count; i++)
            { Vector3 n = normals[i]; normals[i] = new Vector3(n.x / scale.X, n.y / scale.Y, n.z).normalized; }
            if (normals.Count != 0) deformed.SetNormals(normals);
            original.GetTangents(tangents);
            for (int i = 0; i < tangents.Count; i++)
            {
                Vector4 t = tangents[i]; Vector3 v = new Vector3(t.x * scale.X, t.y * scale.Y, t.z).normalized;
                tangents[i] = new Vector4(v.x, v.y, v.z, t.w);
            }
            if (tangents.Count != 0) deformed.SetTangents(tangents);
            if (!immutable && !changed)
            {
                original.GetColors(colors); deformed.SetColors(colors);
                for (int channel = 0; channel < 8; channel++) { original.GetUVs(channel, uvs); deformed.SetUVs(channel, uvs); }
                for (int submesh = 0; submesh < original.subMeshCount; submesh++)
                    deformed.SetIndices(original.GetIndices(submesh), original.GetTopology(submesh), submesh, false);
            }
            deformed.bounds = RenderVisibility.LocalEnvelope(original.bounds);
            filter.sharedMesh = deformed;
            applied = true;
            return true;
        }

        internal override void End()
        {
            if (fallback != null) fallback.End();
            if (applied && filter && filter.sharedMesh == deformed) filter.sharedMesh = original;
            applied = false;
        }

        public override void Dispose()
        {
            End();
            if (deformed) UnityEngine.Object.Destroy(deformed);
            if (fallback != null) fallback.Dispose();
        }
    }
}
