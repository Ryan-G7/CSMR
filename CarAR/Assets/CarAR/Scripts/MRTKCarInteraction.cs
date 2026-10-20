using System.Collections.Generic;
using MixedReality.Toolkit.SpatialManipulation;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace CarAR
{
    // Application policy only. MRTK owns selection, pinch, dragging and two-hand transforms.
    public class MRTKCarInteraction : MonoBehaviour
    {
        public const float MinimumScale = .1f;
        public const float MaximumScale = 10f;
        public ObjectManipulator manipulator;
        readonly List<XRHandSubsystem> hands = new();
        CarShowroom showroom;
        public Vector3 ModelSize { get; private set; } = new Vector3(2, 1.3f, 4.4f);
        void Awake()
        {
            showroom = FindAnyObjectByType<CarShowroom>();
            var renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            ModelSize = bounds.size / transform.localScale.x;
            Debug.Log($"[CarAR Dimensions] 1:1 model bounds: {ModelSize:F3}m; model units, not certified OEM dimensions.");
        }
        public bool ManipulationEnabled => manipulator != null && manipulator.enabled;
        public bool HandsTracked { get; private set; }
        public string Status { get; private set; } = "MRTK input initializing";
        public void SetManipulation(bool value)
        {
            if (manipulator == null) return;
            if (value && showroom != null && (!showroom.placement.IsPlaced || showroom.explode.IsBusy ||
                showroom.GetComponent<MRTKControllerInput>()?.IsPlacing == true)) return;
            if (manipulator.enabled == value) return;
            manipulator.enabled = value;
            Debug.Log("[CarAR MRTK] Vehicle manipulation " + (value ? "unlocked" : "locked"));
        }
        public void ScaleBy(float factor)
        {
            if (!ManipulationEnabled || !float.IsFinite(factor) || factor <= 0 || Mathf.Approximately(factor, 1)) return;
            transform.localScale = Vector3.one * Mathf.Clamp(transform.localScale.x * factor, MinimumScale, MaximumScale);
        }
        public void RotateBy(float angle) { if (ManipulationEnabled) transform.Rotate(Vector3.up, angle, Space.World); }
        public void MoveBy(Vector3 displacement) { if (ManipulationEnabled) transform.position += Vector3.ProjectOnPlane(displacement, Vector3.up); }
        void Update()
        {
            hands.Clear(); SubsystemManager.GetSubsystems(hands);
            bool running = false; HandsTracked = false;
            foreach (var h in hands)
                if (h.running) { running = true; HandsTracked |= h.leftHand.isTracked || h.rightHand.isTracked; }
            Status = HandsTracked ? "Hands tracked / MRTK ready" : running ? "No hands visible" : "Runtime has no active XR Hands provider";
        }
    }
}
