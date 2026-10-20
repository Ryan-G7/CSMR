using UnityEngine;
using UnityEngine.XR;
using Unity.XR.CoreUtils;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.XR;
using CommonUsages = UnityEngine.XR.CommonUsages;

namespace CarAR
{
    [DefaultExecutionOrder(10000)]
    public sealed class XRReviewDiagnostics : MonoBehaviour
    {
        public CarShowroom showroom;
        public XROrigin origin;
        float nextSample, periodStarted, worstFrame, maxHeadStep, maxHeadTurn;
        int frames;
        Vector3 parkedPosition, headPosition;
        Quaternion parkedRotation;
        bool baseline;
        Vector3 frameHeadPosition;
        Quaternion frameHeadRotation;
        bool frameHeadValid;
        TrackedPoseDriver driver;
        PoseSample previousInput, previousView;
        bool hasInput, hasView;
        float nextAnomaly;
        float nextMarkerInputSample;
        string inputSource;
        bool subscribed;
        struct PoseSample
        {
            public Vector3 camera, local, raw, device, legacy, stereo;
            public Quaternion rotation;
            public InputTrackingState tracking;
            public bool rawAvailable, legacyAvailable, presenceAvailable, present, stereoAvailable;
            public TrackingOriginModeFlags mode;
            public int frame;
            public double time;
            public string phase, source;
            public override string ToString() =>
                $"frame={frame} t={time:F4} phase={phase} camera={camera:F4} local={local:F4} rawCenter={raw:F4} rawDevice={device:F4} rawAvailable={rawAvailable} legacy={legacy:F4} legacyAvailable={legacyAvailable} stereoCenter={stereo:F4} stereoAvailable={stereoAvailable} tracking={tracking} presence={(presenceAvailable ? present.ToString() : "unavailable")} mode={mode} source={source}";
        }
        void Start()
        {
            if (showroom.IsPreview) return;
            driver = origin.Camera.GetComponent<TrackedPoseDriver>();
            InputSystem.onAfterUpdate += AfterInput;
            Application.onBeforeRender += BeforeRender;
            subscribed = true;
        }
        void OnDisable()
        {
            if (!subscribed) return;
            InputSystem.onAfterUpdate -= AfterInput;
            Application.onBeforeRender -= BeforeRender;
            subscribed = false;
        }
        PoseSample ReadPose(string phase, bool readStereo)
        {
            var camera = origin.Camera;
            var head = (driver?.positionInput.action?.activeControl?.device as XRHMD) ?? InputSystem.GetDevice<XRHMD>();
            var legacy = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            var sample = new PoseSample
            {
                frame = Time.frameCount, time = Time.realtimeSinceStartupAsDouble, phase = phase,
                camera = camera.transform.position, local = camera.transform.localPosition,
                rotation = camera.transform.rotation, mode = origin.CurrentTrackingOriginMode,
                source = head != null ? head.deviceId + ":" + head.layout : "none"
            };
            if (head != null)
            {
                sample.rawAvailable = true;
                sample.raw = head.centerEyePosition.ReadValue();
                sample.device = head.devicePosition.ReadValue();
                sample.tracking = (InputTrackingState)head.trackingState.ReadValue();
            }
            sample.legacyAvailable = legacy.TryGetFeatureValue(UnityEngine.XR.CommonUsages.centerEyePosition, out sample.legacy);
            sample.presenceAvailable = legacy.TryGetFeatureValue(UnityEngine.XR.CommonUsages.userPresence, out sample.present);
            if (readStereo && camera.stereoEnabled)
            {
                Vector3 left = camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse.GetColumn(3);
                Vector3 right = camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right).inverse.GetColumn(3);
                sample.stereo = (left + right) * .5f;
                sample.stereoAvailable = true;
            }
            return sample;
        }
        void AfterInput()
        {
            var phase = InputState.currentUpdateType;
            if (phase != InputUpdateType.Dynamic && phase != InputUpdateType.BeforeRender) return;
            var sample = ReadPose(phase.ToString(), false);
            InspectPose(sample, previousInput, hasInput);
            previousInput = sample; hasInput = true;
        }
        [BeforeRenderOrder(10000)]
        void BeforeRender()
        {
            var sample = ReadPose("View", true);
            InspectPose(sample, previousView, hasView);
            previousView = sample; hasView = true;
        }
        void InspectPose(PoseSample sample, PoseSample previous, bool valid)
        {
            if (!valid) return;
            // The runtime omits velocity flags in BeforeRender; only pose validity matters here.
            const InputTrackingState poseFlags = InputTrackingState.Position | InputTrackingState.Rotation;
            bool changed = (sample.tracking & poseFlags) != (previous.tracking & poseFlags) || sample.mode != previous.mode ||
                sample.present != previous.present || sample.presenceAvailable != previous.presenceAvailable || sample.source != previous.source;
            if (changed) Debug.Log($"[CarAR Tracking State] before=({previous}) after=({sample})");
            bool cameraJump = Vector3.Distance(sample.camera, previous.camera) > .25f;
            bool rawJump = sample.rawAvailable && previous.rawAvailable && Vector3.Distance(sample.raw, previous.raw) > .25f;
            bool stereoJump = sample.stereoAvailable && previous.stereoAvailable && Vector3.Distance(sample.stereo, previous.stereo) > .25f;
            bool trackingPosition = (sample.tracking & InputTrackingState.Position) != 0;
            bool driverMismatch = sample.rawAvailable && trackingPosition && Vector3.Distance(sample.local, sample.raw) > .02f;
            bool viewMismatch = sample.stereoAvailable && Vector3.Distance(sample.camera, sample.stereo) > .1f;
            if (Huijing.HuijingMRBridge.Requested && sample.phase == "View" && Time.unscaledTime >= nextMarkerInputSample)
            {
                nextMarkerInputSample = Time.unscaledTime + 5;
                Debug.Log($"[HJXR Marker Input] correctedLocations={Huijing.HjxrMarkerTrackingFeature.CorrectedLocationCount} inputSpaces={Huijing.HjxrMarkerTrackingFeature.InputSpaceCount}");
            }
            if (!(cameraJump || rawJump || stereoJump || driverMismatch || viewMismatch) || Time.unscaledTime < nextAnomaly) return;
            nextAnomaly = Time.unscaledTime + .5f;
            Debug.LogWarning($"[CarAR Tracking Anomaly] cameraJump={cameraJump} rawJump={rawJump} stereoJump={stereoJump} driverMismatch={driverMismatch} viewMismatch={viewMismatch} dt={sample.time - previous.time:F4}s before=({previous}) after=({sample})");
        }
        void Update()
        {
            if (showroom.IsPreview) return;
            frames++; worstFrame = Mathf.Max(worstFrame, Time.unscaledDeltaTime);
            var camera = Camera.main.transform;
            if (frameHeadValid)
            {
                maxHeadStep = Mathf.Max(maxHeadStep, Vector3.Distance(camera.position, frameHeadPosition));
                maxHeadTurn = Mathf.Max(maxHeadTurn, Quaternion.Angle(camera.rotation, frameHeadRotation));
            }
            frameHeadPosition = camera.position; frameHeadRotation = camera.rotation; frameHeadValid = true;
            if (Time.unscaledTime < nextSample) return;
            nextSample = Time.unscaledTime + 5;
            if (driver != null)
            {
                var position = driver.positionInput.action;
                var rotationInput = driver.rotationInput.action;
                string source = $"position={position?.activeControl?.path} rotation={rotationInput?.activeControl?.path} positionControls={position?.controls.Count} rotationControls={rotationInput?.controls.Count} state={driver.trackingStateInput.action?.activeControl?.path} update={driver.updateType}";
                if (inputSource != source) { inputSource = source; Debug.Log("[CarAR Head Source] " + source); }
                if (hasView) Debug.Log("[CarAR View Sample] " + previousView);
            }
            var hmd = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            hmd.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState tracking);
            bool presenceAvailable = hmd.TryGetFeatureValue(CommonUsages.userPresence, out bool present);
            float movement = baseline ? Vector3.Distance(showroom.car.position, parkedPosition) : 0;
            float rotation = baseline ? Quaternion.Angle(showroom.car.rotation, parkedRotation) : 0;
            float headMovement = baseline ? Vector3.Distance(camera.position, headPosition) : 0;
            float fps = frames / Mathf.Max(.001f, Time.unscaledTime - periodStarted);
            Debug.Log($"[CarAR Stability] placed={showroom.placement.IsPlaced} locked={!showroom.gestures.ManipulationEnabled} carDelta={movement:F5}m carAngle={rotation:F3}deg headDelta={headMovement:F4}m headStepMax={maxHeadStep:F4}m headTurnMax={maxHeadTurn:F2}deg head={camera.position:F3} car={showroom.car.position:F3} HMDtracking={tracking} presence={(presenceAvailable ? present.ToString() : "unavailable")} focused={Application.isFocused} originMode={origin.CurrentTrackingOriginMode} origin={origin.transform.position:F3} offset={origin.CameraFloorOffsetObject.transform.localPosition:F3} fps={fps:F1} worstFrame={worstFrame * 1000:F1}ms controllers={GetComponent<MRTKControllerInput>().Status}");
            if (showroom.placement.IsPlaced && baseline && !showroom.gestures.ManipulationEnabled && (movement > .002f || rotation > .1f))
                Debug.Log("[CarAR Stability] Parked root changed since prior sample; correlate with an explicit placement/floor command. Head motion alone must not write this root.");
            parkedPosition = showroom.car.position; parkedRotation = showroom.car.rotation; headPosition = camera.position;
            baseline = showroom.placement.IsPlaced;
            frames = 0; worstFrame = 0; maxHeadStep = 0; maxHeadTurn = 0; periodStarted = Time.unscaledTime;
        }
    }
}
