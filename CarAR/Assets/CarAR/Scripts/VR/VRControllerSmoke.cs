using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace CarAR
{
    // Replay native XR input events through the actual locomotion action bindings.
    internal static class VRControllerSmoke
    {
        static void Check(bool result,string detail){if(!result)throw new InvalidOperationException("VR controller smoke: "+detail);}
        public static IEnumerator Run(VRShowroom app)
        {
            VerifyTrackingHeight(app);
            InputSystem.RegisterLayout(@"{""name"":""VRSmokeController"",""extend"":""XRController"",""controls"":[
                {""name"":""pointerPosition"",""layout"":""Vector3""},
                {""name"":""pointerRotation"",""layout"":""Quaternion""},
                {""name"":""triggerPressed"",""layout"":""Button""},
                {""name"":""menuButton"",""layout"":""Button""},
                {""name"":""secondaryButton"",""layout"":""Button""},
                {""name"":""primary2DAxis"",""layout"":""Vector2""}]}");
            var left=InputSystem.AddDevice("VRSmokeController");var right=InputSystem.AddDevice("VRSmokeController");
            InputSystem.SetDeviceUsage(left,UnityEngine.InputSystem.CommonUsages.LeftHand);
            InputSystem.SetDeviceUsage(right,UnityEngine.InputSystem.CommonUsages.RightHand);
            app.BeginControllerReplay();
            var initial=app.Feet;var heading=app.origin.Camera.transform.eulerAngles.y;
            Check(app.Continuous,"left-stick movement was not enabled by default");
            Send(left,Vector2.up);Send(right,Vector2.right);
            yield return new WaitForSeconds(.3f);
            Check(Mathf.Abs(Mathf.DeltaAngle(heading,app.origin.Camera.transform.eulerAngles.y))<.01f,"held stick turned on controller acquisition");
            Check(Vector3.Distance(initial,app.Feet)<.06f,"held left stick moved on controller acquisition");
            Send(left,Vector2.zero);
            Send(right,Vector2.zero);yield return new WaitForSeconds(.1f);
            Send(right,Vector2.right);yield return new WaitForSeconds(.35f);
            Check(Mathf.Abs(Mathf.Abs(Mathf.DeltaAngle(heading,app.origin.Camera.transform.eulerAngles.y))-30)<.2f,"snap turn was not exactly 30 degrees");
            var after=app.origin.Camera.transform.eulerAngles.y;yield return new WaitForSeconds(.35f);
            Check(Mathf.Abs(Mathf.DeltaAngle(after,app.origin.Camera.transform.eulerAngles.y))<.01f,"held stick repeated snap turn");
            Check(Vector3.Distance(initial,app.Feet)<.06f,"snap turn moved the viewer's feet");
            Send(right,Vector2.right,false);yield return new WaitForSeconds(.12f);
            Send(right,Vector2.right);yield return new WaitForSeconds(.3f);
            Check(Mathf.Abs(Mathf.DeltaAngle(after,app.origin.Camera.transform.eulerAngles.y))<.01f,"tracking recovery with held stick turned the rig");
            Send(right,Vector2.zero);yield return new WaitForSeconds(.1f);
            // Left stick is immediately available, also from an open panel.
            var beforeMove=app.Feet;var world=app.environment.localToWorldMatrix;var vehicle=app.car.localToWorldMatrix;
            app.ToggleMenu();yield return new WaitForSeconds(.1f);
            Send(left,Vector2.up);yield return new WaitForSeconds(.35f);
            Send(left,Vector2.zero);yield return new WaitForSeconds(.12f);
            Check(!app.MenuVisible && Vector3.Distance(beforeMove,app.Feet)>.25f,"left stick did not close menu and move the viewer");
            Check(app.environment.localToWorldMatrix==world && app.car.localToWorldMatrix==vehicle,"smooth movement moved the world or car");
            beforeMove=app.Feet;
            Send(left,Vector2.up,false);yield return new WaitForSeconds(.1f);
            Send(left,Vector2.up);yield return new WaitForSeconds(.2f);
            Check(Vector3.Distance(beforeMove,app.Feet)<.06f,"left stick moved on tracking recovery before returning to neutral");
            Send(left,Vector2.zero);yield return new WaitForSeconds(.1f);
            Send(right,Vector2.up);yield return new WaitForSeconds(.2f);
            Check(app.PointerVisible,"forward stick failed to show teleport arc");
            int count=app.Teleports;
            Send(right,Vector2.up,false);yield return new WaitForSeconds(.3f);
            Check(!app.PointerVisible && app.Teleports==count,"tracking loss committed a teleport");
            Send(right,Vector2.zero);yield return new WaitForSeconds(.1f);
            // Exercise actual input actions and the full aim/release/teleport
            // path, including the intermediate values produced by a real stick.
            Check(NavMesh.SamplePosition(app.Feet+Vector3.left*.7f,out var target,.4f,NavMesh.AllAreas) &&
                app.ValidateDestination(target.position,out _),"no reachable target for joystick teleport regression");
            var offset=app.origin.CameraFloorOffsetObject.transform;
            var handPosition=offset.InverseTransformPoint(target.position+Vector3.up*1.2f);
            var handRotation=Quaternion.Inverse(offset.rotation)*Quaternion.LookRotation(Vector3.down,Vector3.forward);
            var beforeTeleport=app.Feet;count=app.Teleports;
            app.ToggleMenu();
            Check(app.MenuVisible,"menu did not open before teleport regression");
            Send(right,Vector2.zero,true,handPosition,handRotation);yield return new WaitForSeconds(.1f);
            Send(right,Vector2.up,true,handPosition,handRotation);yield return new WaitForSeconds(.15f);
            Check(!app.MenuVisible,"startup menu blocked intentional teleport aiming");
            Check(app.PointerVisible && app.TeleportValid,"reachable floor was not a valid controller destination");
            Send(right,Vector2.up*.45f,true,handPosition,handRotation);yield return new WaitForSeconds(.12f);
            Check(app.PointerVisible && app.TeleportValid && app.Teleports==count,"partial stick release cancelled or committed teleport");
            Send(right,Vector2.zero,true,handPosition,handRotation);yield return new WaitForSeconds(.4f);
            Check(app.Teleports==count+1 && Vector3.Distance(beforeTeleport,app.Feet)>.35f,"neutral stick did not commit exactly one teleport");
            Check(Vector3.Distance(app.Feet,target.position)<.1f,"controller teleport missed the validated landing");
            yield return new WaitForSeconds(.15f);
            Check(app.Teleports==count+1,"neutral stick repeated teleport");
            // A one-frame HMD pose drop must stop controls without a black flash;
            // a sustained loss still fades and requires stable tracking to resume.
            Check(!app.TickTracking(false,.016f) && app.ComfortFade==0,"transient tracking loss flashed black or allowed locomotion");
            Check(!app.TickTracking(true,.06f) && app.TickTracking(true,.07f),"tracking resumed without the stable recovery interval");
            Check(!app.TickTracking(false,.3f) && app.ComfortFade>.99f,"sustained tracking loss did not fade out");
            Check(app.TickTracking(true,.2f) && app.ComfortFade==0,"stable tracking did not restore the view");
            Check(app.environment.localToWorldMatrix==world && app.car.localToWorldMatrix==vehicle && app.HighQuality,"controller review changed the world, model or high-quality preset");
            app.EndControllerReplay();InputSystem.RemoveDevice(left);InputSystem.RemoveDevice(right);
            Debug.Log("[CarAR VR Controller Smoke] PASS: real XR actions, default left-stick walk, held-input arming, snap turn, tracking loss/recovery without transient blackout and gradual-release teleport with a fixed model and room.");
        }
        static void VerifyTrackingHeight(VRShowroom app)
        {
            var head=app.origin.Camera.transform;
            var offset=app.origin.CameraFloorOffsetObject.transform;
            var headPosition=head.localPosition;var floorOffset=offset.localPosition;float cameraYOffset=app.origin.CameraYOffset;
            var world=app.environment.localToWorldMatrix;var vehicle=app.car.localToWorldMatrix;
            try
            {
                // Reproduce the device's stale origin: a tracked pose is finite
                // but puts the viewer more than fifteen metres below the room.
                head.localPosition=new Vector3(2,-17.21f,-3);
                Check(!VRShowroom.ValidStartupEyeHeight(app.origin.CameraInOriginSpaceHeight),"underground startup pose was accepted");
                Check(app.CalibrateDeviceHeight() && Mathf.Abs(app.origin.CameraInOriginSpaceHeight-1.65f)<.001f,"device-origin fallback left the viewer below the floor");
                Check(head.localPosition==new Vector3(2,-17.21f,-3),"height calibration overwrote the tracked head pose");
                head.localPosition+=Vector3.down*.4f;
                Check(Mathf.Abs(app.origin.CameraInOriginSpaceHeight-1.25f)<.001f,"height correction prevented physical crouching");
                Check(app.environment.localToWorldMatrix==world && app.car.localToWorldMatrix==vehicle,"height correction moved the room or vehicle");
                Check(!VRShowroom.ValidStartupEyeHeight(float.NaN) && !VRShowroom.ValidStartupEyeHeight(8) && VRShowroom.ValidStartupEyeHeight(1.2f),"startup height validation rejected seated height or accepted invalid height");
            }
            finally {head.localPosition=headPosition;app.origin.CameraYOffset=cameraYOffset;offset.localPosition=floorOffset;}
            Debug.Log("[CarAR VR Origin Smoke] PASS: underground tracked pose recovers to standing height; raw head pose, crouching and fixed scene preserved");
        }
        static void Send(UnityEngine.InputSystem.InputDevice device,Vector2 stick,bool tracked=true,Vector3? handPosition=null,Quaternion? handRotation=null)
        {
            using(StateEvent.From(device,out var state))
            {
                ((ButtonControl)device["isTracked"]).WriteValueIntoEvent(tracked?1f:0f,state);
                ((IntegerControl)device["trackingState"]).WriteValueIntoEvent(tracked?3:0,state);
                var position=handPosition ?? new Vector3(.2f,1.2f,.15f);
                ((Vector3Control)device["devicePosition"]).WriteValueIntoEvent(position,state);
                ((Vector3Control)device["pointerPosition"]).WriteValueIntoEvent(position,state);
                var rotation=handRotation ?? Quaternion.Euler(25,0,0);
                ((QuaternionControl)device["deviceRotation"]).WriteValueIntoEvent(rotation,state);
                ((QuaternionControl)device["pointerRotation"]).WriteValueIntoEvent(rotation,state);
                ((Vector2Control)device["primary2DAxis"]).WriteValueIntoEvent(stick,state);
                InputSystem.QueueEvent(state);
            }
        }
    }
}
