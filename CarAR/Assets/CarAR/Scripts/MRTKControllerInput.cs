using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
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
        public XRRayInteractor LeftGrabRay => left.grab;
        public XRRayInteractor RightGrabRay => right.grab;
        public string Status { get; private set; } = "等待手柄连接";
        public bool IsPlacing { get; private set; }
        public bool PlacementValid { get; private set; }

        sealed class Hand
        {
            public ActionBasedController controller;
            public XRRayInteractor ray, grab;
            public XRInteractorLineVisual line;
            public InputAction menu, stick;
            public bool tracked;
        }
        Hand left, right;
        LineRenderer footprint;
        Transform placementHint;
        TMPro.TMP_Text placementText;
        float placementHeight;
        float placementStarted;
        bool triggerReleased;
        Vector3 candidate;
        readonly List<InputAction> ownedActions = new();

        void Awake()
        {
            left = CreateHand("LeftHand"); right = CreateHand("RightHand");
            InputSystem.onDeviceChange += OnDeviceChange;
            foreach (var device in InputSystem.devices) LogDevice(device, "present");
        }
        Hand CreateHand(string hand)
        {
            var root = new GameObject("OpenXR " + hand);
            root.transform.SetParent(trackingOrigin, false);
            var h = new Hand { controller = root.AddComponent<ActionBasedController>() };
            string path = "<XRController>{" + hand + "}/";
            h.controller.positionAction = Property("Aim position", path + "pointerPosition", "Vector3");
            h.controller.rotationAction = Property("Aim rotation", path + "pointerRotation", "Quaternion");
            h.controller.isTrackedAction = Property("Tracked", path + "isTracked", "Button");
            h.controller.trackingStateAction = Property("Tracking state", path + "trackingState", "Integer");
            h.controller.selectAction = Property("Trigger", path + "triggerPressed", "Button");
            h.controller.selectAction.action.AddBinding(path + "select");
            h.ray = CreateRay(root, 1);
            h.line = root.AddComponent<XRInteractorLineVisual>();
            root.GetComponent<LineRenderer>().sharedMaterial = rayMaterial;
            h.line.lineWidth = .003f; h.line.lineLength = 8;
            h.line.smoothMovement = false;
            h.line.invalidColorGradient = LineColor(new Color(.35f, .7f, 1f, .5f));
            h.line.validColorGradient = LineColor(new Color(.1f, 1f, .7f));
            h.line.blockedColorGradient = LineColor(Color.white);

            // Grip selects only the whole vehicle, so clicking a door cannot drag the car.
            var grip = new GameObject("Grip vehicle ray"); grip.transform.SetParent(root.transform, false);
            var controller = grip.AddComponent<ActionBasedController>();
            controller.enableInputTracking = false;
            controller.selectAction = Property("Grip", path + "gripPressed", "Button");
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
            var move = left.tracked ? DeadZone(left.stick.ReadValue<Vector2>()) : Vector2.zero;
            var turn = right.tracked ? DeadZone(right.stick.ReadValue<Vector2>()) : Vector2.zero;
            var forward = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
            car.gestures.MoveBy((forward * move.y + Vector3.Cross(Vector3.up, forward) * move.x) * (.6f * Time.deltaTime));
            car.gestures.RotateBy(turn.x * 35f * Time.deltaTime);
            car.gestures.ScaleBy(Mathf.Exp(turn.y * .45f * Time.deltaTime));
        }
        static Vector2 DeadZone(Vector2 value) => value.magnitude > .2f ? value.normalized * ((Mathf.Min(1, value.magnitude) - .2f) / .8f) : Vector2.zero;
        void UpdateHand(Hand h)
        {
            bool tracked = h.controller.isTrackedAction.action.IsPressed();
            var state = (InputTrackingState)h.controller.trackingStateAction.action.ReadValue<int>();
            tracked &= (state & (InputTrackingState.Position | InputTrackingState.Rotation)) == (InputTrackingState.Position | InputTrackingState.Rotation);
            h.tracked = tracked;
            h.ray.enabled = tracked && !IsPlacing;
            h.grab.enabled = tracked && !IsPlacing;
            h.line.enabled = tracked && !IsPlacing;
        }
        public void BeginPlacement()
        {
            if (ui.showroom.explode.IsBusy) { ui.Notice("请先组装还原，再重新放置"); return; }
            ui.showroom.gestures.SetManipulation(false);
            IsPlacing = true; PlacementValid = false; triggerReleased = false; placementStarted = Time.unscaledTime;
            placementHeight = ui.showroom.placement.GroundHeight;
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
                    var rotation = ui.showroom.car.rotation;
                    var corners = new[] { new Vector3(-size.x, 0, -size.z), new Vector3(-size.x, 0, size.z), new Vector3(size.x, 0, size.z), new Vector3(size.x, 0, -size.z), new Vector3(-size.x, 0, -size.z) };
                    for (int i = 0; i < corners.Length; i++) footprint.SetPosition(i, candidate + rotation * corners[i] * .5f + Vector3.up * .012f);
                    placementHint.position = candidate + Vector3.up * .4f;
                    placementHint.rotation = Quaternion.LookRotation(placementHint.position - Camera.main.transform.position);
                    placementText.text = "停放预览：扳机确认 · 菜单 / Y / B 取消\n摇杆上下调整地面高度，边框应贴合真实地面";
                }
                var trigger = h.controller.selectAction.action;
                if (!trigger.IsPressed()) triggerReleased = true;
                if (PlacementValid && triggerReleased && Time.unscaledTime - placementStarted > .3f && trigger.WasPressedThisFrame())
                {
                    ui.showroom.placement.PlaceOnFloorPoint(candidate);
                    CancelPlacement(); ui.Notice("已停放并锁定，尺寸与姿态保持不变");
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
            InputSystem.onDeviceChange -= OnDeviceChange;
            foreach (var action in ownedActions) action.Dispose();
            if (footprint != null) Destroy(footprint.gameObject);
            if (placementHint != null) Destroy(placementHint.gameObject);
        }
    }
}
