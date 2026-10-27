using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;

namespace CarAR
{
    // Only application commands live here; XRI owns rays, selection and MRTK manipulation.
    [DefaultExecutionOrder(-100)]
    public sealed class MRTKControllerInput : MonoBehaviour
    {
        public MRTKShowroomUI ui;
        public Transform trackingOrigin;
        public Material rayMaterial;
        public const int VehicleLayer = 2;
        public XRRayInteractor LeftRay => left.ray;
        public XRRayInteractor RightRay => right.ray;
        public bool LeftRayVisible => RayVisible(left);
        public bool RightRayVisible => RayVisible(right);
        public string Status { get; private set; } = "等待手柄连接";
        public bool IsPlacing { get; private set; }
        public bool PlacementValid { get; private set; }

        sealed class Hand
        {
            public ActionBasedController controller;
            public XRRayInteractor ray, grab;
            public XRInteractorLineVisual line;
            public InputAction menu, stick, grip;
            public InputActionProperty aimPosition, aimRotation, gripPosition, gripRotation, aimTracked, aimState, gripTracked, gripState;
            public LineRenderer renderer;
            public string poseSource;
            public bool tracked, gripReleased, axesReady;
        }
        Hand left, right;
        LineRenderer footprint;
        Transform placementHint;
        TMPro.TMP_Text placementText;
        float placementHeight;
        float placementStarted;
        bool triggerReleased;
        Hand placementHand;
        float nextDiagnostics;
        Vector3 candidate;
        readonly List<InputAction> ownedActions = new();

        void Awake()
        {
            left = CreateHand("LeftHand"); right = CreateHand("RightHand");
            foreach (var device in InputSystem.devices) LogDevice(device, "present");
        }
        void OnEnable()
        {
            // Re-activating a parent immediately registers its enabled XRI rays.
            // Keep grip selection disarmed before the first manager update.
            left.grab.enabled = right.grab.enabled = false;
            left.controller.gameObject.SetActive(true); right.controller.gameObject.SetActive(true);
            foreach (var action in ownedActions) action.Enable();
            InputSystem.onDeviceChange += OnDeviceChange;
            Application.onBeforeRender += LogRayDiagnostics;
        }
        void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
            Application.onBeforeRender -= LogRayDiagnostics;
            CancelPlacement();
            // XR origin children can be destroyed before this component at scene teardown.
            if (left?.controller != null) left.controller.gameObject.SetActive(false);
            if (right?.controller != null) right.controller.gameObject.SetActive(false);
            foreach (var action in ownedActions) action.Disable();
            left.gripReleased = right.gripReleased = false;
            left.axesReady = right.axesReady = false;
        }
        Hand CreateHand(string hand)
        {
            var root = new GameObject("OpenXR " + hand);
            root.transform.SetParent(trackingOrigin, false);
            var h = new Hand { controller = root.AddComponent<ActionBasedController>() };
            string path = "<XRController>{" + hand + "}/";
            h.aimPosition = Property("Aim position", path + "pointerPosition", "Vector3");
            h.aimRotation = Property("Aim rotation", path + "pointerRotation", "Quaternion");
            h.gripPosition = Property("Grip position", path + "devicePosition", "Vector3");
            h.gripRotation = Property("Grip rotation", path + "deviceRotation", "Quaternion");
            h.aimTracked = Property("Aim tracked", path + "pointer/isTracked", "Button");
            h.aimState = Property("Aim tracking state", path + "pointer/trackingState", "Integer");
            h.gripTracked = Property("Tracked", path + "isTracked", "Button");
            h.gripState = Property("Tracking state", path + "trackingState", "Integer");
            SetPoseSource(h, true);
            h.controller.selectAction = Property("Trigger", path + "triggerPressed", "Button");
            h.controller.selectAction.action.AddBinding(path + "select");
            h.ray = CreateRay(root, 1);
            h.line = root.AddComponent<XRInteractorLineVisual>();
            h.renderer = root.GetComponent<LineRenderer>();
            h.renderer.sharedMaterial = rayMaterial;
            h.renderer.numCapVertices = 4;
            h.renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            h.line.lineWidth = .005f; h.line.lineLength = 8;
            h.line.overrideInteractorLineLength = true;
            h.line.autoAdjustLineLength = false;
            h.line.smoothMovement = false;
            h.line.invalidColorGradient = LineColor(new Color(.35f, .7f, 1f, 1));
            h.line.validColorGradient = LineColor(new Color(.1f, 1f, .7f));
            h.line.blockedColorGradient = LineColor(Color.white);

            // Grip selects only the whole vehicle, so clicking a door cannot drag the car.
            var grip = new GameObject("Grip vehicle ray"); grip.transform.SetParent(root.transform, false);
            var controller = grip.AddComponent<ActionBasedController>();
            controller.enableInputTracking = false;
            controller.selectAction = Property("Grip", path + "gripPressed", "Button");
            h.grip = controller.selectAction.action;
            h.grab = CreateRay(grip, VehicleLayer);
            h.menu = Own(new InputAction(hand + " Menu", InputActionType.Button));
            h.menu.AddBinding(path + "menuButton"); h.menu.AddBinding(path + "secondaryButton");
            h.menu.performed += _ => { if (h.tracked) ui.TogglePanel(); };
            h.stick = Own(new InputAction(hand + " Review axes", InputActionType.Value, path + "primary2DAxis", expectedControlType: "Vector2"));
            h.menu.Enable(); h.stick.Enable();
            return h;
        }
        static XRRayInteractor CreateRay(GameObject go, int mask)
        {
            var ray = go.AddComponent<XRRayInteractor>();
            ray.interactionLayers = mask; ray.maxRaycastDistance = 12;
            ray.enableUIInteraction = false; // MRTK buttons have native 3D colliders.
            ray.allowAnchorControl = false; ray.useForceGrab = false;
            ray.selectActionTrigger = XRBaseControllerInteractor.InputTriggerType.State;
            return ray;
        }
        InputAction Own(InputAction action) { ownedActions.Add(action); return action; }
        InputActionProperty Property(string name, string binding, string type)
        {
            var action = Own(new InputAction(name, type == "Button" ? InputActionType.Button : InputActionType.Value,
                binding, expectedControlType: type));
            action.wantsInitialStateCheck = true;
            return new InputActionProperty(action);
        }
        static Gradient LineColor(Color color)
        {
            var result = new Gradient();
            result.SetKeys(new[] { new GradientColorKey(color, 0), new GradientColorKey(color, 1) },
                new[] { new GradientAlphaKey(color.a, 0), new GradientAlphaKey(color.a, 1) });
            return result;
        }
        void Update()
        {
            UpdateHand(left); UpdateHand(right);
            Status = left.tracked && right.tracked ? "双手柄已跟踪" : left.tracked ? "左手柄已跟踪" : right.tracked ? "右手柄已跟踪" : "未检测到已跟踪手柄";
            if (IsPlacing) { UpdatePlacement(); return; }
            var car = ui.showroom;
            if (!car.placement.IsPlaced || !car.gestures.ManipulationEnabled || car.explode.IsBusy || car.gestures.manipulator.isSelected) return;
            var move = left.tracked && left.axesReady ? DeadZone(left.stick.ReadValue<Vector2>()) : Vector2.zero;
            var turn = right.tracked && right.axesReady ? DeadZone(right.stick.ReadValue<Vector2>()) : Vector2.zero;
            var forward = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
            car.gestures.MoveBy((forward * move.y + Vector3.Cross(Vector3.up, forward) * move.x) * (.6f * Time.deltaTime));
            car.gestures.RotateBy(turn.x * 35f * Time.deltaTime);
            car.gestures.ScaleBy(Mathf.Exp(turn.y * .45f * Time.deltaTime));
        }
        static Vector2 DeadZone(Vector2 value) => value.magnitude > .2f ? value.normalized * ((Mathf.Min(1, value.magnitude) - .2f) / .8f) : Vector2.zero;
        void UpdateHand(Hand h)
        {
            bool aim = PoseValid(h.aimPosition, h.aimRotation,
                h.aimTracked.action.controls.Count > 0 ? h.aimTracked : h.gripTracked,
                h.aimState.action.controls.Count > 0 ? h.aimState : h.gripState);
            bool grip = PoseValid(h.gripPosition, h.gripRotation, h.gripTracked, h.gripState);
            bool tracked = aim || grip;
            if (tracked && h.poseSource != (aim ? "aim" : "grip")) SetPoseSource(h, aim);
            h.tracked = tracked;
            // Keep the native pointing line visible during floor placement too.
            h.ray.interactionLayers = IsPlacing ? 0 : 1;
            h.ray.enabled = tracked;
            // State-triggered XRI rays otherwise select immediately when tracking
            // resumes or the vehicle unlocks with a grip already held down.
            bool canGrab = tracked && !IsPlacing && ui.showroom.placement.IsPlaced && ui.showroom.gestures.ManipulationEnabled;
            if (!canGrab) h.gripReleased = h.axesReady = false;
            else
            {
                if (!h.grip.IsPressed()) h.gripReleased = true;
                if (DeadZone(h.stick.ReadValue<Vector2>()) == Vector2.zero) h.axesReady = true;
            }
            h.grab.enabled = canGrab && h.gripReleased;
            h.line.enabled = tracked;
        }
        static bool PoseValid(InputActionProperty position, InputActionProperty rotation, InputActionProperty tracked, InputActionProperty state)
        {
            const InputTrackingState poseFlags = InputTrackingState.Position | InputTrackingState.Rotation;
            if (position.action.controls.Count == 0 || rotation.action.controls.Count == 0 || !tracked.action.IsPressed() ||
                ((InputTrackingState)state.action.ReadValue<int>() & poseFlags) != poseFlags) return false;
            var p = position.action.ReadValue<Vector3>(); var q = rotation.action.ReadValue<Quaternion>();
            float norm = Quaternion.Dot(q, q);
            return float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z) && float.IsFinite(norm) && norm > .8f && norm < 1.2f;
        }
        // Read native grip actions, not controller transforms updated later by XRI.
        // One coherent hand is sufficient; inactive controllers do not block a parked review.
        public bool HasCoherentController(Vector3 head, out bool anyTracked)
        {
            bool l = TryGripPoint(left, out var lp), r = TryGripPoint(right, out var rp);
            anyTracked = l || r;
            return l && Vector3.Distance(head, lp) <= 3f || r && Vector3.Distance(head, rp) <= 3f;
        }
        bool TryGripPoint(Hand hand, out Vector3 point)
        {
            point = default;
            if (hand == null || !PoseValid(hand.gripPosition, hand.gripRotation, hand.gripTracked, hand.gripState)) return false;
            point = trackingOrigin.TransformPoint(hand.gripPosition.action.ReadValue<Vector3>());
            return true;
        }
        static void SetPoseSource(Hand h, bool aim)
        {
            var position = aim ? h.aimPosition : h.gripPosition;
            var rotation = aim ? h.aimRotation : h.gripRotation;
            var tracked = aim && h.aimTracked.action.controls.Count > 0 ? h.aimTracked : h.gripTracked;
            var state = aim && h.aimState.action.controls.Count > 0 ? h.aimState : h.gripState;
            if (h.controller.positionAction.action != position.action) h.controller.positionAction = position;
            if (h.controller.rotationAction.action != rotation.action) h.controller.rotationAction = rotation;
            if (h.controller.isTrackedAction.action != tracked.action) h.controller.isTrackedAction = tracked;
            if (h.controller.trackingStateAction.action != state.action) h.controller.trackingStateAction = state;
            // Controller setters disable their old direct actions. Keep both sources
            // available to detect recovery of the OpenXR aim pose after tracking loss.
            foreach (var action in new[] { h.aimPosition, h.aimRotation, h.gripPosition, h.gripRotation, h.aimTracked, h.aimState, h.gripTracked, h.gripState }) action.action.Enable();
            h.poseSource = aim ? "aim" : "grip";
        }
        static bool RayVisible(Hand h) => h != null && h.line.enabled && h.renderer.enabled && h.renderer.positionCount >= 2;
        [BeforeRenderOrder(20000)]
        void LogRayDiagnostics()
        {
            if (Time.unscaledTime < nextDiagnostics) return;
            nextDiagnostics = Time.unscaledTime + 5;
            foreach (var h in new[] { left, right })
            {
                if (h == null) continue;
                var p = h.controller.transform.position;
                string endpoints = h.renderer.positionCount >= 2 ? $"{h.renderer.GetPosition(0):F3}->{h.renderer.GetPosition(h.renderer.positionCount - 1):F3}" : "none";
                Debug.Log($"[CarAR Ray] {h.controller.name} tracked={h.tracked} source={h.poseSource} position={p:F3} forward={h.controller.transform.forward:F3} headDistance={(Camera.main != null ? Vector3.Distance(p, Camera.main.transform.position) : -1):F3} ray={h.ray.enabled} visual={h.line.enabled} rendered={RayVisible(h)} points={h.renderer.positionCount} endpoints={endpoints} posControl={h.controller.positionAction.action.activeControl?.path} aimState={h.aimState.action.ReadValue<int>()} gripState={h.gripState.action.ReadValue<int>()} placing={IsPlacing}");
            }
        }
        public void BeginPlacement()
        {
            if (!ui.showroom.placement.TrackingReady) { ui.Notice(ui.showroom.placement.DisplayStatus); return; }
            if (ui.showroom.explode.IsBusy) { ui.Notice("请先组装还原，再重新放置"); return; }
            ui.showroom.gestures.SetManipulation(false);
            IsPlacing = true; PlacementValid = false; triggerReleased = false; placementHand = null; placementStarted = Time.unscaledTime;
            placementHeight = ui.showroom.placement.PlacementPreviewHeight;
            ui.controlPanel.gameObject.SetActive(false);
            if (footprint == null)
            {
                var go = new GameObject("Vehicle floor footprint (controller placement)");
                footprint = go.AddComponent<LineRenderer>(); footprint.sharedMaterial = rayMaterial;
                footprint.positionCount = 5; footprint.widthMultiplier = .012f; footprint.loop = false;
                footprint.startColor = footprint.endColor = new Color(.1f, 1f, .7f);
                var hint = new GameObject("Placement instructions", typeof(RectTransform), typeof(Canvas));
                placementHint = hint.transform;
                hint.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                hint.GetComponent<Canvas>().worldCamera = Camera.main;
                hint.GetComponent<RectTransform>().sizeDelta = new Vector2(900, 100);
                hint.transform.localScale = Vector3.one * .001f;
                var text = new GameObject("Instructions", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
                text.transform.SetParent(placementHint, false);
                text.GetComponent<RectTransform>().sizeDelta = new Vector2(900, 100);
                placementText = text.GetComponent<TMPro.TMP_Text>(); placementText.font = ui.status.font;
                placementText.fontSize = 28; placementText.alignment = TMPro.TextAlignmentOptions.Center;
                placementText.raycastTarget = false;
                footprint.gameObject.SetActive(false); placementHint.gameObject.SetActive(false);
            }
        }
        void UpdatePlacement()
        {
            var h = right.tracked ? right : left.tracked ? left : null;
            // A newly tracked or different hand must release its own trigger first.
            // The previous hand's released state cannot authorize a held trigger.
            if (h != placementHand) { placementHand = h; triggerReleased = false; placementStarted = Time.unscaledTime; }
            PlacementValid = false;
            if (h != null)
            {
                var aim = h.controller.transform;
                placementHeight += DeadZone(h.stick.ReadValue<Vector2>()).y * .25f * Time.deltaTime;
                var plane = new Plane(Vector3.up, Vector3.up * placementHeight);
                var ray = new Ray(aim.position, aim.forward);
                if (plane.Raycast(ray, out float distance) && distance > .3f && distance < 15)
                {
                    candidate = ray.GetPoint(distance); PlacementValid = true;
                    var size = ui.showroom.gestures.ModelSize * ui.showroom.car.localScale.x;
                    var rotation = CarPlacementManager.FloorRotation(ui.showroom.car.rotation);
                    var corners = new[] { new Vector3(-size.x, 0, -size.z), new Vector3(-size.x, 0, size.z), new Vector3(size.x, 0, size.z), new Vector3(size.x, 0, -size.z), new Vector3(-size.x, 0, -size.z) };
                    for (int i = 0; i < corners.Length; i++) footprint.SetPosition(i, candidate + rotation * corners[i] * .5f + Vector3.up * .012f);
                    placementHint.position = candidate + Vector3.up * .4f;
                    placementHint.rotation = Quaternion.LookRotation(placementHint.position - Camera.main.transform.position);
                    placementText.text = "停放预览：扳机确认 · 菜单 / Y / B 取消\n摇杆上下调高度，让四角贴合真实地面；确认后车身自动放平";
                }
                var trigger = h.controller.selectAction.action;
                if (!trigger.IsPressed()) triggerReleased = true;
                if (PlacementValid && triggerReleased && Time.unscaledTime - placementStarted > .3f && trigger.WasPressedThisFrame())
                {
                    ui.showroom.placement.PlaceOnFloorPoint(candidate);
                    CancelPlacement(); ui.Notice("已放平并停车锁定，保留车头朝向和尺寸");
                }
            }
            footprint.gameObject.SetActive(IsPlacing && PlacementValid);
            placementHint.gameObject.SetActive(IsPlacing && PlacementValid);
        }
        public void CancelPlacement()
        {
            IsPlacing = false; PlacementValid = false;
            if (footprint != null) footprint.gameObject.SetActive(false);
            if (placementHint != null) placementHint.gameObject.SetActive(false);
        }
        static void LogDevice(UnityEngine.InputSystem.InputDevice device, string change)
        {
            if (device is TrackedDevice)
                Debug.Log($"[CarAR Input] {change}: {device.displayName}, layout={device.layout}, usages={string.Join(", ", device.usages)}, interface={device.description.interfaceName}");
        }
        void OnDeviceChange(UnityEngine.InputSystem.InputDevice device, InputDeviceChange change) => LogDevice(device, change.ToString());
        void OnDestroy()
        {
            // These objects belong to the input component but live under the XR origin.
            if (left?.controller != null) Destroy(left.controller.gameObject);
            if (right?.controller != null) Destroy(right.controller.gameObject);
            foreach (var action in ownedActions) action.Dispose();
            if (footprint != null) Destroy(footprint.gameObject);
            if (placementHint != null) Destroy(placementHint.gameObject);
        }
    }
}
