using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CarAR
{
    public class ExplodeController : MonoBehaviour
    {
        [SerializeField] float duration = 1.6f;
        [SerializeField] float baseDistance = 1.4f;
        struct Part { public Transform transform; public Vector3 home, offset; }
        readonly List<Part> parts = new();
        float progress;
        bool targetExploded;
        Coroutine animation;
        public bool IsExploded => targetExploded;
        public bool IsBusy => targetExploded || animation != null || progress > .001f;
        public float Progress => progress;
        public int AssemblyCount => parts.Count;

        void EnsureInit()
        {
            if (parts.Count > 0) return;
            var candidates = new List<Transform>();
            foreach (Transform content in transform)
                if (content.GetComponentsInChildren<MeshRenderer>(true).Length > 0) candidates.Add(content);
            if (candidates.Count == 1 && candidates[0].childCount > 1)
            {
                var content = candidates[0];
                candidates.Clear();
                foreach (Transform child in content)
                    if (child.GetComponentsInChildren<MeshRenderer>(true).Length > 0) candidates.Add(child);
            }
            if (candidates.Count <= 1)
            {
                candidates.Clear();
                foreach (var renderer in GetComponentsInChildren<MeshRenderer>(true)) candidates.Add(renderer.transform);
            }
            var whole = ModelBounds.Local(transform);
            int index = 0;
            foreach (var candidate in candidates)
            {
                var local = ModelBounds.Local(candidate);
                var centre = transform.InverseTransformPoint(candidate.TransformPoint(local.center));
                var direction = centre - whole.center;
                if (direction.sqrMagnitude < .02f)
                    direction = new Vector3(Mathf.Sin(index * 2.4f), 1, Mathf.Cos(index * 2.4f));
                direction.y = Mathf.Max(.15f, direction.y);
                direction.Normalize();
                var offset = candidate.parent.InverseTransformVector(transform.TransformVector(direction * baseDistance));
                parts.Add(new Part { transform = candidate, home = candidate.localPosition, offset = offset });
                index++;
            }
        }

        public void ResetModel()
        {
            if (animation != null) StopCoroutine(animation);
            animation = null;
            Apply(0);
            parts.Clear();
            progress = 0;
            targetExploded = false;
        }

        public void Toggle() => SetTarget(targetExploded ? 0 : 1);
        public void SetTarget(float target)
        {
            EnsureInit();
            targetExploded = target > .5f;
            if (animation != null) StopCoroutine(animation);
            animation = StartCoroutine(Animate(targetExploded ? 1 : 0));
        }

        IEnumerator Animate(float target)
        {
            float from = progress, elapsed = 0;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                Apply(Mathf.Lerp(from, target, Mathf.SmoothStep(0, 1, elapsed / Mathf.Max(.01f, duration))));
                yield return null;
            }
            Apply(target);
            animation = null;
        }

        void Apply(float value)
        {
            progress = value;
            foreach (var part in parts)
                if (part.transform != null) part.transform.localPosition = part.home + part.offset * value;
        }
    }
}
