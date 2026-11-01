using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FriendsWine
{
    // Expand around the original world center conservatively. Bottom-anchored
    // stretching can both enlarge the bounds and move their center by an extent.
    internal sealed class RenderVisibility
    {
        private readonly Plane[] planes = new Plane[6];
        private readonly Vector3[] absNormals = new Vector3[6];
        private readonly Vector3[] corners = new Vector3[4];
        private Camera[] cameras = new Camera[8];
        private sealed class CachedFrustum
        {
            internal readonly Plane[] Planes = new Plane[6];
            internal readonly Vector3[] AbsNormals = new Vector3[6];
            internal int Frame = -1;
        }
        private readonly Dictionary<int, CachedFrustum> uiFrusta = new Dictionary<int, CachedFrustum>();
        private int camerasFrame = -1;
        private int cachedCameraCount;

        internal static float Expansion
        {
            get
            {
                Settings s = Main.Settings;
                float maximum = Mathf.Max(1f, Mathf.Max(Mathf.Max(s.VerticalX, s.VerticalY), Mathf.Max(s.HorizontalX, s.HorizontalY)));
                return 2f * maximum - 1f;
            }
        }

        internal static Bounds LocalEnvelope(Bounds original)
        {
            Settings s = Main.Settings;
            float x = Mathf.Max(1, Mathf.Max(s.VerticalX, s.HorizontalX));
            float y = Mathf.Max(1, Mathf.Max(s.VerticalY, s.HorizontalY));
            return new Bounds(new Vector3(original.center.x, original.min.y + original.size.y * y * .5f, original.center.z),
                new Vector3(original.size.x * x, original.size.y * y, original.size.z));
        }

        internal void SetCamera(Camera camera)
        {
            GeometryUtility.CalculateFrustumPlanes(camera, planes);
            for (int i = 0; i < planes.Length; i++)
            {
                Vector3 n = planes[i].normal;
                absNormals[i] = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
            }
        }

        internal bool Intersects(Bounds original, float expansion)
        { return TestBounds(planes, absNormals, original, expansion); }

        private static bool TestBounds(Plane[] planes, Vector3[] absNormals, Bounds original, float expansion)
        {
            Vector3 center = original.center;
            Vector3 extents = original.extents * expansion + Vector3.one * .001f;
            for (int i = 0; i < planes.Length; i++)
                if (Vector3.Dot(planes[i].normal, center) + planes[i].distance + Vector3.Dot(absNormals[i], extents) < 0)
                    return false;
            return true;
        }

        private void RefreshCameras()
        {
            int count = Camera.allCamerasCount;
            if (camerasFrame == Time.frameCount && cachedCameraCount == count) return;
            if (cameras.Length < count) cameras = new Camera[Mathf.NextPowerOfTwo(count)];
            cachedCameraCount = Camera.GetAllCameras(cameras);
            camerasFrame = Time.frameCount;
        }

        private bool VisibleToUiCamera(Camera camera, Bounds bounds)
        {
            CachedFrustum cached;
            if (!uiFrusta.TryGetValue(camera.GetInstanceID(), out cached))
            { cached = new CachedFrustum(); uiFrusta.Add(camera.GetInstanceID(), cached); }
            if (cached.Frame != Time.frameCount)
            {
                GeometryUtility.CalculateFrustumPlanes(camera, cached.Planes);
                for (int i = 0; i < 6; i++)
                {
                    Vector3 n = cached.Planes[i].normal;
                    cached.AbsNormals[i] = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
                }
                cached.Frame = Time.frameCount;
            }
            return TestBounds(cached.Planes, cached.AbsNormals, bounds, Expansion);
        }

        internal bool InAnyCamera(Renderer renderer)
        {
            if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.forceRenderingOff) return false;
            Bounds bounds = renderer.bounds;
            int layer = 1 << renderer.gameObject.layer;
            RefreshCameras();
            for (int i = 0; i < cachedCameraCount; i++)
            {
                Camera camera = cameras[i];
                if (!camera || (camera.cullingMask & layer) == 0) continue;
                if (VisibleToUiCamera(camera, bounds)) return true;
            }
            return false;
        }

        internal bool GraphicVisible(Graphic graphic)
        {
            if (!graphic || !graphic.isActiveAndEnabled) return false;
            Canvas canvas = graphic.canvas;
            if (!canvas || !canvas.isActiveAndEnabled) return false;
            graphic.rectTransform.GetWorldCorners(corners);
            Bounds bounds = new Bounds(corners[0], Vector3.zero);
            for (int i = 1; i < corners.Length; i++) bounds.Encapsulate(corners[i]);
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                bounds.extents *= Expansion;
                return bounds.max.x >= 0 && bounds.min.x <= Screen.width && bounds.max.y >= 0 && bounds.min.y <= Screen.height;
            }
            RefreshCameras();
            for (int i = 0; i < cachedCameraCount; i++)
            {
                Camera camera = cameras[i];
                if (!camera || (camera.cullingMask & (1 << graphic.gameObject.layer)) == 0) continue;
                if (VisibleToUiCamera(camera, bounds)) return true;
            }
            return false;
        }
    }
}
