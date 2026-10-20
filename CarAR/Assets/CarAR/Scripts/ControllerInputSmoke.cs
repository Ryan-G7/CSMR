using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace CarAR
{
    // Exercises actual Input System actions and native XRI rays, without a headset or button-event shortcuts.
    internal static class ControllerInputSmoke
    {
        static void Check(bool result, string message) { if (!result) throw new InvalidOperationException("Controller smoke: " + message); }
        public static IEnumerator Run(CarShowroom showroom)
        {
            InputSystem.RegisterLayout(@"{
                ""name"":""CarARSmokeController"", ""extend"":""XRController"", ""controls"":[
                    {""name"":""pointerPosition"",""layout"":""Vector3""},
                    {""name"":""pointerRotation"",""layout"":""Quaternion""},
                    {""name"":""triggerPressed"",""layout"":""Button""},
                    {""name"":""gripPressed"",""layout"":""Button""},
                    {""name"":""secondaryButton"",""layout"":""Button""},
                    {""name"":""menuButton"",""layout"":""Button""},
                    {""name"":""primary2DAxis"",""layout"":""Vector2""}
                ]}");
            var left = InputSystem.AddDevice("CarARSmokeController");
            var right = InputSystem.AddDevice("CarARSmokeController");
            InputSystem.SetDeviceUsage(left, UnityEngine.InputSystem.CommonUsages.LeftHand);
            InputSystem.SetDeviceUsage(right, UnityEngine.InputSystem.CommonUsages.RightHand);
            var ui = showroom.GetComponent<MRTKShowroomUI>();
            var input = showroom.GetComponent<MRTKControllerInput>();
            var car = showroom.car;
            var initialPosition = car.position; var initialScale = car.localScale; var initialRotation = car.rotation;
            float initialGround = showroom.placement.GroundHeight;
            var camera = Camera.main.transform;
            var cameraPosition = camera.position; var cameraRotation = camera.rotation;
            void Pose(UnityEngine.InputSystem.InputDevice device, Vector3 position, Vector3 target, float? trigger = null)
            {
                var rotation = Quaternion.LookRotation(target - position);
                Send(device, input.trackingOrigin.InverseTransformPoint(position), Quaternion.Inverse(input.trackingOrigin.rotation) * rotation, trigger: trigger);
            }
            IEnumerator Click(int index)
            {
                var position = camera.position + Vector3.right * .15f;
                Pose(right, position, ui.buttons[index].transform.position);
                yield return new WaitForSeconds(.2f);
                int count = ui.ConfirmedCount;
                Send(right, trigger: 1); yield return new WaitForSeconds(.12f);
                Send(right, trigger: 0); yield return new WaitForSeconds(.2f);
                Check(ui.ConfirmedCount == count + 1, $"native trigger ray did not click button {index}; hover={ui.buttons[index].isHovered}");
            }
            Check(!ui.ControlPanel.gameObject.activeSelf, "panel must start hidden");
            Pose(left, camera.position + Vector3.left * .2f, car.position);
            Pose(right, camera.position + Vector3.right * .2f, car.position);
            yield return new WaitForSeconds(.2f);
            Check(input.LeftRayVisible && input.RightRayVisible, "native line visuals are not rendering for tracked controllers");
            using (StateEvent.From(right, out var fallbackEvent))
            {
                // Reproduce a runtime that tracks grip while its aim pose is invalid.
                ((QuaternionControl)right["pointerRotation"]).WriteValueIntoEvent(default, fallbackEvent);
                InputSystem.QueueEvent(fallbackEvent);
            }
            yield return new WaitForSeconds(.2f);
            Check(input.RightRayVisible, "grip fallback lost the visible ray when aim became invalid");
            Pose(right, camera.position + Vector3.right * .2f, car.position);
            yield return new WaitForSeconds(.2f);
            Check(input.RightRayVisible, "native aim ray did not recover");
            Send(left, menu: 1); yield return new WaitForSeconds(.2f);
            Send(left, menu: 0); yield return new WaitForSeconds(.2f);
            Check(ui.ControlPanel.gameObject.activeSelf, $"controller menu did not open panel; enabled={left.enabled}, tracked={input.Status}, focus={Application.isFocused}");
            CapturePanel(ui, "controller-panel.png");
            var panelPosition = ui.ControlPanel.position;
            int initialClicks = ui.ConfirmedCount;
            camera.LookAt(ui.buttons[4].transform.position);
            yield return new WaitForSeconds(1.5f);
            camera.position += Vector3.right * .3f; yield return null;
            Check(ui.ConfirmedCount == initialClicks, "head gaze activated a button");
            Check(ui.ControlPanel.position == panelPosition, "panel followed head motion");
            Check(car.position == initialPosition, "parked car followed head motion");
            camera.SetPositionAndRotation(cameraPosition, cameraRotation);
            yield return Click(27); yield return new WaitForSeconds(.7f);
            Check(showroom.hood.IsOpen, "native trigger ray did not open AMG hood");
            yield return Click(27); yield return new WaitForSeconds(.7f);
            // Picking up a controller or unlocking with held grips must not grab
            // the vehicle until each grip has been released and pressed again.
            Send(left, grip: 1); Send(right, grip: 1, stick: Vector2.down); yield return new WaitForSeconds(.1f);
            yield return Click(4); Check(showroom.gestures.ManipulationEnabled, "unlock failed");
            Check(!showroom.gestures.manipulator.isSelected && car.localScale == initialScale,
                "unlocking with held grips/stick selected or resized the vehicle");
            input.enabled = false; input.enabled = true;
            yield return new WaitForSeconds(.2f);
            Check(!showroom.gestures.manipulator.isSelected && car.localScale == initialScale,
                "re-enabling input with held grips/stick selected or resized the vehicle");
            Send(left, grip: 0); Send(right, grip: 0, stick: Vector2.zero); yield return new WaitForSeconds(.1f);
            yield return Click(25);
            CapturePanel(ui, "controller-adjustments.png");
            yield return Click(10); yield return Click(13);
            Check(car.localScale.x < .95f && Quaternion.Angle(car.rotation, initialRotation) > 4, "precision scale / rotate failed");
            yield return Click(26); Check(!ui.ControlPanel.gameObject.activeSelf, "panel close failed");

            car.SetPositionAndRotation(initialPosition, initialRotation); car.localScale = Vector3.one;
            Pose(left, car.position + new Vector3(-.4f, .65f, -5), car.position + new Vector3(-.4f, .65f, 0));
            Pose(right, car.position + new Vector3(.4f, .65f, -5), car.position + new Vector3(.4f, .65f, 0));
            yield return new WaitForSeconds(.25f);
            Send(left, grip: 1); yield return new WaitForSeconds(.25f);
            Check(showroom.gestures.manipulator.isSelected, "grip ray failed to select vehicle");
            var beforeMove = car.position;
            Pose(left, initialPosition + new Vector3(-.1f, .65f, -5), initialPosition + new Vector3(-.1f, .65f, 0));
            yield return new WaitForSeconds(.5f);
            Check(Vector3.Distance(car.position, beforeMove) > .1f, "grip drag failed");
            Send(right, grip: 1); yield return new WaitForSeconds(.25f);
            Check(showroom.gestures.manipulator.interactorsSelecting.Count == 2, "second controller grip did not select vehicle");
            float scale = car.localScale.x;
            Pose(left, initialPosition + new Vector3(-.6f, .65f, -5), initialPosition + new Vector3(-.6f, .65f, 0));
            Pose(right, initialPosition + new Vector3(.9f, .65f, -5), initialPosition + new Vector3(.9f, .65f, 0));
            yield return new WaitForSeconds(.6f);
            Check(car.localScale.x > scale * 1.1f, "two-controller MRTK scaling failed");
            Send(left, tracked: false); Send(right, tracked: false, stick: Vector2.down);
            yield return new WaitForSeconds(.2f);
            var scaleBeforeRecovery = car.localScale;
            Send(left); Send(right);
            yield return new WaitForSeconds(.2f);
            Check(!showroom.gestures.manipulator.isSelected && car.localScale == scaleBeforeRecovery,
                "tracking recovery with held grips/stick silently selected or resized the vehicle");
            Send(left, grip: 0); Send(right, grip: 0, stick: Vector2.zero); yield return new WaitForSeconds(.25f);
            Check(!showroom.gestures.manipulator.isSelected, "grip release retained selection");
            Send(right, stick: new Vector2(0, -1)); yield return new WaitForSeconds(.4f);
            Send(right, stick: Vector2.zero); yield return new WaitForSeconds(.1f);
            Check(car.localScale.x < 1.95f, "right stick scaling failed");
            var beforeStick = car.position;
            Send(left, stick: Vector2.right); yield return new WaitForSeconds(.4f);
            Send(left, stick: Vector2.zero); yield return new WaitForSeconds(.1f);
            Check(Vector3.Distance(beforeStick, car.position) > .15f, "left stick translation failed");

            input.BeginPlacement();
            var ground = new Vector3(2, showroom.placement.GroundHeight, 2);
            Pose(right, ground + new Vector3(0, 1.5f, -3), ground);
            yield return new WaitForSeconds(.4f);
            Check(input.IsPlacing && input.PlacementValid, "floor placement preview unavailable");
            Send(right, tracked: false);
            Pose(left, ground + new Vector3(0, 1.5f, -3), ground, trigger: 1);
            yield return new WaitForSeconds(.2f);
            Check(input.IsPlacing, "switching to a hand with a held trigger confirmed placement without a release");
            Send(left, trigger: 0);
            Pose(right, ground + new Vector3(0, 1.5f, -3), ground);
            yield return new WaitForSeconds(.4f);
            Send(right, stick: Vector2.up); yield return new WaitForSeconds(.3f);
            Send(right, stick: Vector2.zero); yield return new WaitForSeconds(.1f);
            Send(right, trigger: 1); yield return new WaitForSeconds(.2f);
            Send(right, trigger: 0); yield return new WaitForSeconds(.2f);
            Check(!input.IsPlacing && !showroom.gestures.ManipulationEnabled, "placement failed to confirm and park");
            Check(input.RightRayVisible, "ray did not remain visible after floor placement");
            Check(showroom.placement.GroundHeight > initialGround + .04f, "placement stick height calibration failed");
            // The oblique aim ray moves its intersection horizontally when the floor height changes.
            var expectedPoint = ground + new Vector3(0, 1, -2) * (showroom.placement.GroundHeight - initialGround);
            Check(Vector3.ProjectOnPlane(car.position - expectedPoint, Vector3.up).magnitude < .01f, "floor placement confirmed at wrong point");
            var parked = car.position;
            var parkedScale = car.localScale;
            camera.position += Vector3.right;
            camera.rotation = Quaternion.Euler(15, 70, 0);
            yield return new WaitForSeconds(.5f);
            Check(Vector3.Distance(parked, car.position) < .0001f, "parked root moved when head pose changed");
            Check(car.localScale == parkedScale && car.gameObject.activeInHierarchy,
                "turning the head changed vehicle scale or active state");
            InputSystem.RemoveDevice(left); InputSystem.RemoveDevice(right);
            camera.SetPositionAndRotation(cameraPosition, cameraRotation);
            showroom.placement.AdjustFloor(initialGround - showroom.placement.GroundHeight);
            car.SetPositionAndRotation(initialPosition, initialRotation); car.localScale = initialScale;
            Debug.Log("[CarAR Controller Smoke] PASS: native trigger/button rays, held-grip unlock and tracking-recovery protection, deliberate grip drag/two-controller scaling, joystick transforms, floor footprint and head-independent parked position/scale/visibility.");
        }
        static void CapturePanel(MRTKShowroomUI ui, string file)
        {
            var go = new GameObject("Controller UI diagnostic camera");
            var camera = go.AddComponent<Camera>(); camera.enabled = false;
            camera.transform.SetPositionAndRotation(ui.ControlPanel.position - ui.ControlPanel.forward * 1.1f, ui.ControlPanel.rotation);
            camera.fieldOfView = 45; camera.cullingMask = 1 << 5;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.02f, .025f, .03f);
            go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().allowXRRendering = false;
            var target = RenderTexture.GetTemporary(1024, 1024, 24);
            var previous = RenderTexture.active;
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            var image = new Texture2D(1024, 1024, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0); image.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(Application.dataPath, "../" + file), image.EncodeToPNG());
            camera.targetTexture = null; RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target); UnityEngine.Object.Destroy(image); UnityEngine.Object.Destroy(go);
        }
        static void Send(UnityEngine.InputSystem.InputDevice device, Vector3? position = null, Quaternion? rotation = null,
            float? trigger = null, float? grip = null, float? menu = null, Vector2? stick = null, bool tracked = true)
        {
            using (StateEvent.From(device, out var pointer))
            {
                ((ButtonControl)device["isTracked"]).WriteValueIntoEvent(tracked ? 1f : 0f, pointer);
                ((IntegerControl)device["trackingState"]).WriteValueIntoEvent(tracked ? 3 : 0, pointer);
                if (position.HasValue)
                {
                    ((Vector3Control)device["pointerPosition"]).WriteValueIntoEvent(position.Value, pointer);
                    ((Vector3Control)device["devicePosition"]).WriteValueIntoEvent(position.Value, pointer);
                }
                if (rotation.HasValue)
                {
                    ((QuaternionControl)device["pointerRotation"]).WriteValueIntoEvent(rotation.Value, pointer);
                    ((QuaternionControl)device["deviceRotation"]).WriteValueIntoEvent(rotation.Value, pointer);
                }
                if (trigger.HasValue) ((ButtonControl)device["triggerPressed"]).WriteValueIntoEvent(trigger.Value, pointer);
                if (grip.HasValue) ((ButtonControl)device["gripPressed"]).WriteValueIntoEvent(grip.Value, pointer);
                if (menu.HasValue) ((ButtonControl)device["menuButton"]).WriteValueIntoEvent(menu.Value, pointer);
                if (stick.HasValue) ((Vector2Control)device["primary2DAxis"]).WriteValueIntoEvent(stick.Value, pointer);
                InputSystem.QueueEvent(pointer);
            }
        }
    }
}
