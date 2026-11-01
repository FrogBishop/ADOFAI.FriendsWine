using System;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

namespace FriendsWine
{
    public sealed class ProxyMarker : MonoBehaviour
    {
        internal Renderer Original;
#if FRIENDSWINE_SELFTEST
        private void OnWillRenderObject() { FloorVisibility.Rendered(Original); }
#endif
    }

    internal abstract class RendererProxy : RenderEffect
    {
#if FRIENDSWINE_SELFTEST
        internal static string VerificationSkip = "";
#endif
        protected GameObject root;
        protected Renderer proxy;
        protected readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private readonly List<Material> materials = new List<Material>();
        private Material[] cachedMaterials = new Material[0];
        private bool hadPropertyBlocks;
        private bool hidden;
        private bool originalForceOff;

        protected RendererProxy(Renderer renderer) : base(renderer)
        {
            root = new GameObject("FriendsWine.Visual");
            root.hideFlags = HideFlags.HideAndDontSave;
            root.SetActive(false);
            root.AddComponent<ProxyMarker>().Original = renderer;
            root.transform.SetParent(RuntimeController.Instance.transform, false);
            root.layer = renderer.gameObject.layer;
        }

        internal void Protect()
        {
#if FRIENDSWINE_SELFTEST
            if (RenderCoordinator.LegacyVerification) FloorVisibility.Track(Original);
#endif
            // Exclude proxy components from Unity's global FindObjects queries, and
            // keep inactive proxies outside game hierarchies between camera renders.
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
                component.hideFlags = HideFlags.HideAndDontSave;
        }

        protected void CopyRenderer()
        {
            root.layer = Original.gameObject.layer;
            Original.GetSharedMaterials(materials);
            bool changed = materials.Count != cachedMaterials.Length;
            for (int i = 0; !changed && i < materials.Count; i++) changed |= materials[i] != cachedMaterials[i];
            if (changed) { cachedMaterials = materials.ToArray(); proxy.sharedMaterials = cachedMaterials; }
            proxy.sortingLayerID = Original.sortingLayerID;
            proxy.sortingOrder = Original.sortingOrder;
            proxy.rendererPriority = Original.rendererPriority;
            proxy.renderingLayerMask = Original.renderingLayerMask;
            proxy.lightmapIndex = Original.lightmapIndex;
            proxy.lightmapScaleOffset = Original.lightmapScaleOffset;
            proxy.realtimeLightmapIndex = Original.realtimeLightmapIndex;
            proxy.realtimeLightmapScaleOffset = Original.realtimeLightmapScaleOffset;
            proxy.shadowCastingMode = Original.shadowCastingMode;
            proxy.receiveShadows = Original.receiveShadows;
            proxy.lightProbeUsage = Original.lightProbeUsage;
            proxy.reflectionProbeUsage = Original.reflectionProbeUsage;
            proxy.probeAnchor = Original.probeAnchor;
            bool hasBlocks = Original.HasPropertyBlock();
            if (hasBlocks || hadPropertyBlocks)
            {
                Original.GetPropertyBlock(properties);
                proxy.SetPropertyBlock(properties.isEmpty ? null : properties);
                for (int i = 0; i < materials.Count; i++)
                {
                    Original.GetPropertyBlock(properties, i);
                    // An empty, non-null per-material block overrides the renderer block,
                    // including SpriteRenderer's native texture/color. Clear it with null.
                    proxy.SetPropertyBlock(properties.isEmpty ? null : properties, i);
                }
            }
            hadPropertyBlocks = hasBlocks;
        }

        internal override bool Begin(Camera camera, StretchScale scale)
        {
#if FRIENDSWINE_SELFTEST
            if (Original && Original.GetType().Name == VerificationSkip) return false;
#endif
            if (Failed || !Original || !Original.enabled || !Original.gameObject.activeInHierarchy ||
                Original.forceRenderingOff || (camera.cullingMask & (1 << Original.gameObject.layer)) == 0) return false;
            if (!root || !proxy) return false;
            root.transform.SetParent(Original.transform, false);
            root.SetActive(true);
            CopyRenderer();
            if (!Prepare(camera, scale)) { End(); return false; }
            originalForceOff = Original.forceRenderingOff;
            Original.forceRenderingOff = true;
            hidden = true;
            proxy.enabled = true;
            return true;
        }

        protected abstract bool Prepare(Camera camera, StretchScale scale);
        protected void Around(Vector3 anchor, StretchScale scale)
        {
            root.transform.localPosition = new Vector3(anchor.x * (1 - scale.X), anchor.y * (1 - scale.Y), 0);
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = new Vector3(scale.X, scale.Y, 1);
        }

        internal override void End()
        {
            if (proxy) proxy.enabled = false;
            if (hidden && Original) Original.forceRenderingOff = originalForceOff;
            hidden = false;
            if (root)
            {
                root.SetActive(false);
                if (RuntimeController.Instance) root.transform.SetParent(RuntimeController.Instance.transform, false);
            }
        }

        public override void Dispose()
        {
            End();
            if (root)
            {
                // Remove only tweens targeting our own disposable components.
                foreach (Component component in root.GetComponentsInChildren<Component>(true))
                    DOTween.Kill(component, false);
                DOTween.Kill(root, false);
                UnityEngine.Object.Destroy(root);
            }
        }
    }

    internal sealed class SpriteProxy : RendererProxy
    {
        private readonly SpriteRenderer source;
        private readonly SpriteRenderer target;
        internal SpriteProxy(SpriteRenderer r) : base(r)
        {
            source = r; target = root.AddComponent<SpriteRenderer>(); proxy = target; target.enabled = false;
        }
        protected override bool Prepare(Camera camera, StretchScale scale)
        {
            if (!source.sprite) return false;
            target.sprite = source.sprite;
            target.color = source.color;
            target.flipX = source.flipX; target.flipY = source.flipY;
            target.drawMode = source.drawMode; target.size = source.size;
            target.tileMode = source.tileMode; target.adaptiveModeThreshold = source.adaptiveModeThreshold;
            target.maskInteraction = source.maskInteraction; target.spriteSortPoint = source.spriteSortPoint;
            Around(Geometry.Bottom(source.localBounds), scale);
            return true;
        }
    }

    internal sealed class MaskProxy : RendererProxy
    {
        private readonly SpriteMask source;
        private readonly SpriteMask target;
        internal MaskProxy(SpriteMask r) : base(r)
        { source = r; target = root.AddComponent<SpriteMask>(); proxy = target; target.enabled = false; }
        protected override bool Prepare(Camera camera, StretchScale scale)
        {
            if (!source.sprite) return false;
            target.sprite = source.sprite;
            target.alphaCutoff = source.alphaCutoff;
            target.isCustomRangeActive = source.isCustomRangeActive;
            target.frontSortingLayerID = source.frontSortingLayerID;
            target.frontSortingOrder = source.frontSortingOrder;
            target.backSortingLayerID = source.backSortingLayerID;
            target.backSortingOrder = source.backSortingOrder;
            Around(Geometry.Bottom(source.localBounds), scale);
            return true;
        }
    }

    internal sealed class MeshProxy : RendererProxy
    {
        private readonly MeshRenderer sourceRenderer;
        private readonly MeshRenderer targetRenderer;
        private readonly MeshFilter source;
        private readonly MeshFilter target;
        internal MeshProxy(MeshRenderer r, MeshFilter filter) : base(r)
        {
            sourceRenderer = r;
            source = filter; target = root.AddComponent<MeshFilter>();
            targetRenderer = root.AddComponent<MeshRenderer>(); proxy = targetRenderer; proxy.enabled = false;
        }
        protected override bool Prepare(Camera camera, StretchScale scale)
        {
            if (!source || !source.sharedMesh) return false;
            target.sharedMesh = source.sharedMesh;
            // Preserve any additional vertex colors, UVs, and other material inputs.
            targetRenderer.additionalVertexStreams = sourceRenderer.additionalVertexStreams;
            Around(Geometry.Bottom(source.sharedMesh.bounds), scale);
            return true;
        }
    }

    internal sealed class BakedProxy : RendererProxy
    {
        private readonly Mesh mesh;
        private readonly MeshFilter target;
        internal BakedProxy(Renderer r) : base(r)
        {
            mesh = new Mesh { name = "FriendsWine.Baked", hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
            target = root.AddComponent<MeshFilter>(); target.sharedMesh = mesh;
            proxy = root.AddComponent<MeshRenderer>(); proxy.enabled = false;
        }
        protected override bool Prepare(Camera camera, StretchScale scale)
        {
            var skin = Original as SkinnedMeshRenderer;
            var line = Original as LineRenderer;
            var trail = Original as TrailRenderer;
            if (skin) skin.BakeMesh(mesh, false);
            else if (line) line.BakeMesh(mesh, camera, false);
            else if (trail) trail.BakeMesh(mesh, camera, false);
            else return false;
            Around(Geometry.Bottom(mesh.bounds), scale);
            return mesh.vertexCount > 0;
        }
        public override void Dispose() { base.Dispose(); UnityEngine.Object.Destroy(mesh); }
    }

    internal sealed class ParticleProxy : RendererProxy
    {
        private readonly ParticleSystemRenderer source;
        private readonly ParticleSystem system;
        private readonly Mesh mesh, trailMesh;
        private readonly MeshRenderer trailRenderer;
        private readonly GameObject trailRoot;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private MeshParticleGeometry meshGeometry;

        internal ParticleProxy(ParticleSystemRenderer r) : base(r)
        {
            source = r; system = r.GetComponent<ParticleSystem>();
            mesh = new Mesh { name = "FriendsWine.Particles", hideFlags = HideFlags.HideAndDontSave };
            trailMesh = new Mesh { name = "FriendsWine.ParticleTrails", hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic(); trailMesh.MarkDynamic();
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            proxy = root.AddComponent<MeshRenderer>(); proxy.enabled = false;
            trailRoot = new GameObject("FriendsWine.Trails");
            trailRoot.hideFlags = HideFlags.HideAndDontSave;
            trailRoot.AddComponent<ProxyMarker>().Original = r;
            trailRoot.transform.SetParent(root.transform, false);
            trailRoot.AddComponent<MeshFilter>().sharedMesh = trailMesh;
            trailRenderer = trailRoot.AddComponent<MeshRenderer>(); trailRenderer.enabled = false;
        }

        protected override bool Prepare(Camera camera, StretchScale scale)
        {
            if (!system) return false;
            // Bake a complete world-space snapshot, then explicitly map it back into
            // the source local frame. Default baking differs between simulation/scaling
            // modes and can magnify distant emitters when a parent scale is applied twice.
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            source.BakeMesh(mesh, camera, ParticleSystemBakeMeshOptions.BakeRotationAndScale | ParticleSystemBakeMeshOptions.BakePosition);
            WorldToLocal(mesh);
            if (source.renderMode == ParticleSystemRenderMode.Mesh)
            {
                if (source.meshCount == 1 && source.mesh && source.mesh.isReadable)
                {
                    if (meshGeometry == null || meshGeometry.Source != source.mesh) meshGeometry = new MeshParticleGeometry(source.mesh);
                    meshGeometry.Apply(mesh, vertices, scale);
                }
                else
                {
                    // Preserve individual disconnected islands for uncommon multi-mesh emitters.
                    MeshParticleGeometry.ApplyIslands(mesh, vertices, scale);
                }
            }
            else Geometry.ParticleQuads(mesh, vertices, uvs, scale);
            trailRenderer.enabled = false;
            if (system.trails.enabled && source.trailMaterial)
            {
                source.BakeTrailsMesh(trailMesh, camera, ParticleSystemBakeMeshOptions.BakeRotationAndScale | ParticleSystemBakeMeshOptions.BakePosition);
                WorldToLocal(trailMesh);
                if (trailMesh.vertexCount > 0)
                {
                    Geometry.WholeMesh(trailMesh, vertices, scale);
                    trailRoot.layer = root.layer;
                    trailRenderer.sharedMaterial = source.trailMaterial;
                    trailRenderer.sortingLayerID = source.sortingLayerID;
                    trailRenderer.sortingOrder = source.sortingOrder;
                    source.GetPropertyBlock(properties); trailRenderer.SetPropertyBlock(properties);
                    trailRenderer.enabled = true;
                }
            }
            return mesh.vertexCount > 0 || trailRenderer.enabled;
        }
        private void WorldToLocal(Mesh baked)
        {
            baked.GetVertices(vertices);
            Matrix4x4 inverse = source.transform.worldToLocalMatrix;
            for (int i = 0; i < vertices.Count; i++) vertices[i] = inverse.MultiplyPoint3x4(vertices[i]);
            baked.SetVertices(vertices); baked.RecalculateBounds();
        }
        internal override void End() { base.End(); if (trailRenderer) trailRenderer.enabled = false; }
        public override void Dispose()
        { base.Dispose(); UnityEngine.Object.Destroy(mesh); UnityEngine.Object.Destroy(trailMesh); }
    }
}
