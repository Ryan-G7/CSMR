using UnityEngine;
using UnityEngine.XR;
using Unity.XR.CoreUtils;

namespace CarAR
{
    public sealed class XRReviewDiagnostics : MonoBehaviour
    {
        public CarShowroom showroom;
        public XROrigin origin;
        float nextSample, periodStarted, worstFrame;
        int frames;
        Vector3 parkedPosition, headPosition;
        Quaternion parkedRotation;
        bool baseline;
        void Update()
        {
            if (showroom.IsPreview) return;
            frames++; worstFrame = Mathf.Max(worstFrame, Time.unscaledDeltaTime);
            if (Time.unscaledTime < nextSample) return;
            nextSample = Time.unscaledTime + 5;
            var hmd = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            hmd.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState tracking);
            bool presenceAvailable = hmd.TryGetFeatureValue(CommonUsages.userPresence, out bool present);
            var camera = Camera.main.transform;
            float movement = baseline ? Vector3.Distance(showroom.car.position, parkedPosition) : 0;
            float rotation = baseline ? Quaternion.Angle(showroom.car.rotation, parkedRotation) : 0;
            float headMovement = baseline ? Vector3.Distance(camera.position, headPosition) : 0;
            float fps = frames / Mathf.Max(.001f, Time.unscaledTime - periodStarted);
            Debug.Log($"[CarAR Stability] placed={showroom.placement.IsPlaced} locked={!showroom.gestures.ManipulationEnabled} carDelta={movement:F5}m carAngle={rotation:F3}deg headDelta={headMovement:F4}m head={camera.position:F3} car={showroom.car.position:F3} HMDtracking={tracking} presence={(presenceAvailable ? present.ToString() : "unavailable")} focused={Application.isFocused} originMode={origin.CurrentTrackingOriginMode} origin={origin.transform.position:F3} offset={origin.CameraFloorOffsetObject.transform.localPosition:F3} fps={fps:F1} worstFrame={worstFrame * 1000:F1}ms controllers={GetComponent<MRTKControllerInput>().Status}");
            if (showroom.placement.IsPlaced && baseline && !showroom.gestures.ManipulationEnabled && (movement > .002f || rotation > .1f))
                Debug.Log("[CarAR Stability] Parked root changed since prior sample; correlate with an explicit placement/floor command. Head motion alone must not write this root.");
            parkedPosition = showroom.car.position; parkedRotation = showroom.car.rotation; headPosition = camera.position;
            baseline = showroom.placement.IsPlaced;
            frames = 0; worstFrame = 0; periodStarted = Time.unscaledTime;
        }
    }
}
