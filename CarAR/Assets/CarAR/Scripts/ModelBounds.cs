using UnityEngine;

namespace CarAR
{
    internal static class ModelBounds
    {
        public static Bounds Local(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            bool initialized = false;
            var result = new Bounds();
            foreach (var renderer in renderers)
            {
                if (!renderer.enabled || !renderer.gameObject.activeSelf) continue;
                var bounds = renderer.localBounds;
                var matrix = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                var center = bounds.center;
                var extent = bounds.extents;
                for (int i = 0; i < 8; i++)
                {
                    var corner = center + Vector3.Scale(extent, new Vector3(
                        (i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var point = matrix.MultiplyPoint3x4(corner);
                    if (!initialized) { result = new Bounds(point, Vector3.zero); initialized = true; }
                    else result.Encapsulate(point);
                }
            }
            return initialized ? result : new Bounds(Vector3.zero, Vector3.one);
        }

        public static float BottomY(Transform root)
        {
            var bounds = Local(root);
            return root.TransformPoint(bounds.min).y;
        }
    }
}
