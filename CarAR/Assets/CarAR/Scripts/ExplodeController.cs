using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace CarAR
{
    public class ExplodeController : MonoBehaviour
    {
        [SerializeField] float duration = 1.6f;
        [SerializeField] float baseDistance = 1.4f;
        struct Part { public Transform t; public Vector3 home, offset; }
        readonly List<Part> parts = new();
        CarPanel[] panels;
        float progress;
        bool targetExploded;
        Coroutine animation;
        public bool IsExploded => targetExploded;
        public bool IsBusy => targetExploded || animation != null || progress > 0.001f;
        public float Progress => progress;
        void EnsureInit()
        {
            if (parts.Count > 0) return;
            panels = GetComponentsInChildren<CarPanel>(true);
            var roots = new HashSet<Transform>();
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
            {
                var panel = r.GetComponentInParent<CarPanel>();
                // Treat each hinged panel as one assembly; never animate both it and its children.
                roots.Add(panel != null ? panel.transform : r.transform);
            }
            var bounds = new Bounds(transform.position, Vector3.zero);
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true)) bounds.Encapsulate(r.bounds);
            int index = 0;
            foreach (var part in roots)
            {
                var rs = part.GetComponentsInChildren<Renderer>(true);
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                var dir = transform.InverseTransformPoint(b.center) - transform.InverseTransformPoint(bounds.center);
                if (dir.sqrMagnitude < .02f) dir = new Vector3(Mathf.Sin(index * 2.4f), 1, Mathf.Cos(index * 2.4f));
                dir.Normalize();
                // Convert the car-space displacement to the actual parent's space.
                var offset = part.parent.InverseTransformVector(transform.TransformVector(dir * baseDistance));
                parts.Add(new Part { t = part, home = part.localPosition, offset = offset }); index++;
            }
        }
        public void Toggle() => SetTarget(targetExploded ? 0 : 1);
        public void SetTarget(float target)
        {
            EnsureInit(); targetExploded = target > .5f;
            if (animation != null) StopCoroutine(animation);
            animation = StartCoroutine(Animate(targetExploded ? 1 : 0));
        }
        IEnumerator Animate(float target)
        {
            foreach (var panel in panels) panel.Close();
            bool closed;
            do { closed = true; foreach (var panel in panels) closed &= panel.IsClosed; if (!closed) yield return null; } while (!closed);
            float from = progress, elapsed = 0;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                Apply(Mathf.Lerp(from, target, Mathf.SmoothStep(0, 1, elapsed / Mathf.Max(.01f, duration))));
                yield return null;
            }
            Apply(target); animation = null;
        }
        void Apply(float t) { progress = t; foreach (var p in parts) p.t.localPosition = p.home + p.offset * t; }
    }
}
