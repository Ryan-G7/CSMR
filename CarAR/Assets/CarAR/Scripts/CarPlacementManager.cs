using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace CarAR
{
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
        bool waitingForHeadset;
        bool refreshPlacementPreview;
        CarShowroom showroom;
        public string Status => !placed ? waitingForHeadset ? "Waiting for worn, tracked headset" : "Scanning for floor..." : manualFloor ? "Manually aligned floor" : estimated ? "Estimated floor (no plane data)" : stageFloor ? "OpenXR stage floor" : "Tracked horizontal plane";
        public bool IsPlaced => placed;
        public void RefreshPlacementPreviewAfterAlignment() => refreshPlacementPreview = true;
        public float PlacementPreviewHeight => refreshPlacementPreview && Camera.main != null ? Camera.main.transform.position.y - fallbackEyeHeight : GroundHeight;
        void Start() => showroom = FindAnyObjectByType<CarShowroom>();
        void OnEnable()
        {
            startTime = Time.time;
            if (!placed && carRoot != null) carRoot.gameObject.SetActive(false);
        }
        void Update()
        {
            // Lock the first placement. Later plane refinements must not move a parked car.
            if (placed || carRoot == null || showroom != null && showroom.IsCalibrating) return;
            if (showroom != null && showroom.GetComponent<MRTKControllerInput>()?.IsPlacing == true) return;
            // A PC runtime can publish its last cached pose while the headset is unworn.
            // Start the initial floor timeout only after wearing and positional tracking recover.
            if (showroom != null && !showroom.IsPreview && Application.platform != RuntimePlatform.Android)
            {
                var hmd = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head);
                bool ready = hmd.isValid && hmd.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trackingState, out UnityEngine.XR.InputTrackingState state)
                    && (state & (UnityEngine.XR.InputTrackingState.Position | UnityEngine.XR.InputTrackingState.Rotation))
                    == (UnityEngine.XR.InputTrackingState.Position | UnityEngine.XR.InputTrackingState.Rotation);
                if (hmd.TryGetFeatureValue(UnityEngine.XR.CommonUsages.userPresence, out bool present) && !present) ready = false;
                if (!ready)
                {
                    if (!waitingForHeadset) Debug.Log("[CarAR Placement] Waiting for a worn, position-tracked headset before initial floor placement.");
                    waitingForHeadset = true; startTime = Time.time; return;
                }
                if (waitingForHeadset) Debug.Log("[CarAR Placement] Headset tracking ready; initial floor search started.");
                waitingForHeadset = false;
                if (Time.time - startTime < 1f) return;
            }
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
        public void Recenter()
        {
            if (Camera.main == null || carRoot == null || showroom != null && showroom.IsCalibrating) return;
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
                Debug.LogWarning($"[CarAR] Invalid floor reference: eye height={eyeAboveFloor:F3}m. Using estimated {fallbackEyeHeight:F2}m eye height; calibrate the runtime floor or use marker alignment.");
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
            if (carRoot == null || showroom != null && showroom.IsCalibrating) return;
            // Every subsequent placement preserves the established ground pivot,
            // including open panels, exploded parts and manually adjusted floors.
            float offset = carRoot.position.y - GroundHeight;
            if (!placed)
            {
                var renderers = carRoot.GetComponentsInChildren<Renderer>(true);
                float bottom = float.PositiveInfinity;
                foreach (var r in renderers) bottom = Mathf.Min(bottom, r.bounds.min.y);
                offset = float.IsPositiveInfinity(bottom) ? 0f : carRoot.position.y - bottom;
            }
            carRoot.position = groundPos + Vector3.up * offset;
            GroundHeight = groundPos.y;
            estimated = isEstimated;
            stageFloor = manualFloor = false;
            placed = true;
            carRoot.gameObject.SetActive(true);
            Debug.Log($"[CarAR] Placed: {Status}, ground={GroundHeight:F3}");
        }
        public string DisplayStatus => !placed ? waitingForHeadset ? "请佩戴头显，等待空间定位恢复" : "正在寻找地面" : manualFloor ? "地面高度已手动对齐" : estimated ? "地面高度为估计，可用 ±1cm 校准" : "使用跟踪地面高度";
        public void PlaceOnFloorPoint(Vector3 ground)
        {
            if (showroom != null && showroom.IsCalibrating) return;
            refreshPlacementPreview = false;
            PlaceAt(ground, true);
            manualFloor = true;
        }
        public float GroundHeight { get; private set; }
        public void AdjustFloor(float delta)
        {
            if (!placed || carRoot == null) return;
            if (showroom != null && showroom.IsCalibrating) return;
            carRoot.position += Vector3.up * delta; GroundHeight += delta; manualFloor = true;
        }
    }
}
