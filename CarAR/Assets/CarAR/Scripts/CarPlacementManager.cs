using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace CarAR
{
    [DefaultExecutionOrder(-150)]
    public class CarPlacementManager : MonoBehaviour
    {
        [SerializeField] ARPlaneManager planeManager;
        [SerializeField] Transform carRoot;
        [SerializeField] float minPlaneArea = 4f;
        [SerializeField] float fallbackDelay = 8f;
        [SerializeField] float fallbackDistance = 4.5f;
        [SerializeField] float fallbackEyeHeight = 1.5f;
        bool placed, estimated, stageFloor, manualFloor;
        float startTime;
        bool guardTracking, needsReposition, hasHeadSample, suppressed;
        float stableSince = -1, headSampleTime;
        Vector3 lastHead;
        Renderer[] hiddenRenderers;
        Collider[] hiddenColliders;
        bool[] renderingState, colliderState;
        TrackedPoseDriver poseDriver;
        MRTKControllerInput controllers;
        CarShowroom showroom;
        public string Status => !TrackingReady ? "Waiting for coherent headset/controller tracking" : needsReposition ? "Tracking origin changed; place vehicle again" : !placed ? "Scanning for floor..." : manualFloor ? "Manually aligned floor" : estimated ? "Estimated floor (no plane data)" : stageFloor ? "OpenXR stage floor" : "Tracked horizontal plane";
        public bool TrackingReady { get; private set; } = true;
        public bool IsPlaced => placed && TrackingReady && !needsReposition;
        public float PlacementPreviewHeight => (placed && !needsReposition) || Camera.main == null ? GroundHeight : Camera.main.transform.position.y - fallbackEyeHeight;
        void Start()
        {
            showroom = FindAnyObjectByType<CarShowroom>();
            controllers = showroom != null ? showroom.GetComponent<MRTKControllerInput>() : null;
            guardTracking = showroom != null && !showroom.IsPreview && Application.platform != RuntimePlatform.Android;
            if (guardTracking)
            {
                TrackingReady = false;
                poseDriver = Camera.main.GetComponent<TrackedPoseDriver>();
            }
        }
        void OnEnable()
        {
            startTime = Time.time;
            if (!placed && carRoot != null) carRoot.gameObject.SetActive(false);
        }
        void Update()
        {
            if (guardTracking) ReadTracking();
            // Lock the first placement. Later plane refinements must not move a parked car.
            if (placed || !TrackingReady || carRoot == null) return;
            if (showroom != null && showroom.GetComponent<MRTKControllerInput>()?.IsPlacing == true) return;
            ARPlane best = null;
            float bestArea = minPlaneArea;
            if (planeManager != null)
                foreach (var plane in planeManager.trackables)
                {
                    if (plane.alignment != PlaneAlignment.HorizontalUp || plane.trackingState != TrackingState.Tracking || plane.subsumedBy != null) continue;
                    if (Camera.main != null && plane.center.y > Camera.main.transform.position.y - .65f) continue;
                    float area = plane.size.x * plane.size.y;
                    if (area > bestArea) { bestArea = area; best = plane; }
                }
            if (best != null)
            {
                // ARPlane.center is already expressed in world space.
                PlaceAt(best.center, false);
            }
            else if (!placed && Time.time - startTime > fallbackDelay && Camera.main != null)
            {
                Recenter();
            }
        }
        void ReadTracking()
        {
            // Use the same native HMD as the camera's TrackedPoseDriver. No camera
            // offsets or input transforms can repair an incoherent runtime pose.
            var hmd = (poseDriver?.positionInput.action?.activeControl?.device as XRHMD) ?? InputSystem.GetDevice<XRHMD>();
            var legacy = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(XRNode.Head);
            const InputTrackingState flags = InputTrackingState.Position | InputTrackingState.Rotation;
            bool valid = hmd != null && hmd.isTracked.isPressed && ((InputTrackingState)hmd.trackingState.ReadValue() & flags) == flags;
            if (hmd != null)
            {
                var rotation = hmd.centerEyeRotation.ReadValue();
                float norm = Quaternion.Dot(rotation, rotation);
                valid &= float.IsFinite(norm) && norm > .8f && norm < 1.2f;
            }
            if (legacy.TryGetFeatureValue(UnityEngine.XR.CommonUsages.userPresence, out bool present) && !present) valid = false;
            var local = hmd != null ? hmd.centerEyePosition.ReadValue() : Vector3.zero;
            var head = Camera.main.transform.parent.TransformPoint(local);
            valid &= float.IsFinite(head.x) && float.IsFinite(head.y) && float.IsFinite(head.z);
            bool any = false;
            bool coherent = controllers != null && controllers.HasCoherentController(head, out any);
            ObserveTracking(valid, head, coherent, any, Time.unscaledTime);
        }
        // Also used to replay recorded runtime coordinates in the smoke suite.
        internal void ObserveTracking(bool headValid, Vector3 head, bool coherentHand, bool anyHand, float now)
        {
            bool valid = headValid && (coherentHand || (placed && !anyHand));
            if (!valid) stableSince = -1;
            else
            {
                float limit = .5f + 8f * Mathf.Min(Mathf.Max(0, now - headSampleTime), .25f);
                if (hasHeadSample && Vector3.Distance(lastHead, head) > limit)
                {
                    stableSince = -1;
                    if (placed && !needsReposition)
                    {
                        needsReposition = true;
                        Debug.LogWarning($"[CarAR Placement] Tracking origin discontinuity ({Vector3.Distance(lastHead, head):F3}m); vehicle hidden until explicit placement. Scale is unchanged.");
                    }
                }
                lastHead = head; headSampleTime = now; hasHeadSample = true;
                if (stableSince < 0) stableSince = now;
            }
            bool ready = valid && now - stableSince >= 1f;
            if (!ready) startTime = Time.time;
            if (TrackingReady != ready)
                Debug.Log("[CarAR Placement] " + (ready ? "Coherent tracking stable; placement available." : "Waiting for worn headset and coherent tracking."));
            TrackingReady = ready;
            if (!IsPlaced)
            {
                showroom?.gestures.SetManipulation(false);
                if (!ready) controllers?.CancelPlacement();
            }
            SetSuppressed(placed && !IsPlaced);
        }
        void SetSuppressed(bool value)
        {
            if (suppressed == value || carRoot == null) return;
            suppressed = value;
            // Keep the root active: deactivation would stop explode coroutines.
            if (value)
            {
                hiddenRenderers = carRoot.GetComponentsInChildren<Renderer>(true);
                hiddenColliders = carRoot.GetComponentsInChildren<Collider>(true);
                renderingState = new bool[hiddenRenderers.Length]; colliderState = new bool[hiddenColliders.Length];
                for (int i = 0; i < hiddenRenderers.Length; i++)
                { renderingState[i] = hiddenRenderers[i].forceRenderingOff; hiddenRenderers[i].forceRenderingOff = true; }
                for (int i = 0; i < hiddenColliders.Length; i++)
                { colliderState[i] = hiddenColliders[i].enabled; hiddenColliders[i].enabled = false; }
            }
            else
            {
                for (int i = 0; i < hiddenRenderers.Length; i++) if (hiddenRenderers[i] != null) hiddenRenderers[i].forceRenderingOff = renderingState[i];
                for (int i = 0; i < hiddenColliders.Length; i++) if (hiddenColliders[i] != null) hiddenColliders[i].enabled = colliderState[i];
            }
        }
        void OnDisable() => SetSuppressed(false);
        public void Recenter()
        {
            if (!TrackingReady || Camera.main == null || carRoot == null) return;
            if (showroom != null)
            {
                showroom.GetComponent<MRTKControllerInput>()?.CancelPlacement();
                showroom.gestures.SetManipulation(false);
            }
            var cam = Camera.main.transform;
            var fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < .001f) fwd = Vector3.forward;
            var origin = GetComponent<Unity.XR.CoreUtils.XROrigin>();
            bool floor = origin != null && (origin.CurrentTrackingOriginMode & UnityEngine.XR.TrackingOriginModeFlags.Floor) != 0;
            // Some PC runtimes advertise Floor while the uncalibrated stage origin is above the HMD.
            // Such a reference cannot represent the floor beneath a standing or seated viewer.
            float eyeAboveFloor = origin != null ? cam.position.y - origin.transform.position.y : 0;
            if (floor && (eyeAboveFloor < .35f || eyeAboveFloor > 2.5f))
            {
                Debug.LogWarning($"[CarAR] Invalid floor reference: eye height={eyeAboveFloor:F3}m. Using estimated {fallbackEyeHeight:F2}m eye height; calibrate the runtime floor or adjust the placement height.");
                floor = false;
            }
            var ground = cam.position + fwd * fallbackDistance;
            ground.y = floor ? origin.transform.position.y : cam.position.y - fallbackEyeHeight;
            PlaceAt(ground, !floor);
            stageFloor = floor;
            LogVisibility();
        }
        public void LogVisibility()
        {
            var cam = Camera.main;
            if (cam == null || carRoot == null) return;
            var parts = carRoot.GetComponentsInChildren<Renderer>(true);
            var bounds = new Bounds(carRoot.position, Vector3.zero);
            int enabled = 0;
            foreach (var r in parts) { bounds.Encapsulate(r.bounds); if (r.enabled && r.gameObject.activeInHierarchy) enabled++; }
            Debug.Log($"[CarAR Visibility] camera={cam.transform.position:F3} forward={cam.transform.forward:F3} car={carRoot.position:F3} bounds={bounds} viewport={cam.WorldToViewportPoint(bounds.center):F3} activeRenderers={enabled}/{parts.Length} frustum={GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(cam), bounds)} stereo={cam.stereoEnabled}");
        }
        public void PlaceAt(Vector3 groundPos, bool isEstimated = false)
        {
            if (!TrackingReady || carRoot == null) return;
            // Floor placement always parks upright, retaining the horizontal heading.
            // RotationAxisConstraint only limits changes relative to the grab's
            // starting pose; it cannot repair a previously tilted pose.
            showroom?.gestures?.SetManipulation(false);
            float tilt = Vector3.Angle(carRoot.up, Vector3.up);
            carRoot.rotation = FloorRotation(carRoot.rotation);
            // Every subsequent placement preserves the established ground pivot,
            // including open panels, exploded parts and manually adjusted floors.
            float offset = carRoot.position.y - GroundHeight;
            if (!placed)
            {
                var renderers = carRoot.GetComponentsInChildren<Renderer>(true);
                float bottom = float.PositiveInfinity;
                foreach (var r in renderers)
                {
                    // World renderer bounds can be stale while the vehicle is
                    // inactive or just rotated. Transform local bounds explicitly.
                    var bounds = r.localBounds;
                    var matrix = r.transform.localToWorldMatrix;
                    var extent = bounds.extents;
                    float radius = Mathf.Abs(matrix.m10) * extent.x + Mathf.Abs(matrix.m11) * extent.y + Mathf.Abs(matrix.m12) * extent.z;
                    bottom = Mathf.Min(bottom, matrix.MultiplyPoint3x4(bounds.center).y - radius);
                }
                offset = float.IsPositiveInfinity(bottom) ? 0f : carRoot.position.y - bottom;
            }
            carRoot.position = groundPos + Vector3.up * offset;
            GroundHeight = groundPos.y;
            estimated = isEstimated;
            stageFloor = manualFloor = false;
            placed = true;
            needsReposition = false;
            carRoot.gameObject.SetActive(true);
            SetSuppressed(false);
            Debug.Log($"[CarAR] Placed: {Status}, ground={GroundHeight:F3}, scale={carRoot.localScale:F4}, position={carRoot.position:F3}, tiltBefore={tilt:F3}deg tiltAfter={Vector3.Angle(carRoot.up, Vector3.up):F3}deg heading={carRoot.eulerAngles.y:F2}deg");
        }
        internal static Quaternion FloorRotation(Quaternion rotation)
        {
            var forward = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up);
            // A vertical forward axis has no heading; recover it from the right axis.
            if (forward.sqrMagnitude < .0001f)
                forward = Vector3.Cross(Vector3.ProjectOnPlane(rotation * Vector3.right, Vector3.up), Vector3.up);
            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }
        public string DisplayStatus => !TrackingReady ? "请佩戴头显并唤醒手柄，等待空间定位稳定" : needsReposition ? "空间定位已重置，请重新选择停放位置" : !placed ? "正在寻找地面" : manualFloor ? "地面高度已手动对齐" : estimated ? "地面高度为估计，可用 ±1cm 校准" : "使用跟踪地面高度";
        public void PlaceOnFloorPoint(Vector3 ground)
        {
            if (!TrackingReady || carRoot == null) return;
            PlaceAt(ground, true);
            manualFloor = true;
        }
        public float GroundHeight { get; private set; }
        public void AdjustFloor(float delta)
        {
            if (!IsPlaced || carRoot == null) return;
            carRoot.position += Vector3.up * delta; GroundHeight += delta; manualFloor = true;
        }
    }
}
