using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
namespace CarAR.Huijing
{
    public class HuijingMRBridge : MonoBehaviour
    {
        public CarPlacementManager placement;
        public string Status { get; private set; } = "Huijing MR not enabled";
        HjxrSession sdk;
        string alignmentPath;
        DateTime alignmentStamp;
        float nextAlignmentCheck;
        InputAction headTrackingState;
        [Serializable] sealed class AlignmentRequest
        {
            public int poseDeferFrames;
            public bool changeVideoTiming;
            public float videoPoseOffsetMs;
        }
        public static bool Requested => Array.IndexOf(Environment.GetCommandLineArgs(), "--hjxr") >= 0;
        public void ConfigureTrackingReference()
        {
            if (!Requested || placement == null) return;
            var origin = placement.GetComponent<Unity.XR.CoreUtils.XROrigin>();
            if (origin == null) return;
            // The manufacturer's Unity demo uses LOCAL with no artificial eye-height offset.
            // Keep Unity's camera/controllers and the vendor's internally-created LOCAL VST
            // reference in the same coordinates. Ground height remains a placement setting.
            origin.CameraYOffset = 0;
            // Before loader startup Unity may report incomplete supported modes. XROrigin
            // permanently discards an unsupported request, so select LOCAL after startup.
            origin.RequestedTrackingOriginMode = Unity.XR.CoreUtils.XROrigin.TrackingOriginMode.NotSpecified;
            origin.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            origin.CameraFloorOffsetObject.transform.localPosition = Vector3.zero;
            var driver = origin.Camera.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            if (driver != null && (driver.trackingStateInput.action == null || driver.trackingStateInput.action.bindings.Count == 0))
            {
                // An empty tracking-state action makes TPD treat every bound pose as valid,
                // including an invalid reset pose during tracking loss. Keep native pose driving.
                headTrackingState = new InputAction("HJXR Head Tracking State", InputActionType.Value,
                    "<XRHMD>/trackingState", expectedControlType: "Integer");
                driver.trackingStateInput = new InputActionProperty(headTrackingState);
                driver.ignoreTrackingState = false;
                Debug.Log("[HJXR Reference] Native TrackedPoseDriver now observes the HMD tracking-state flags.");
            }
            Debug.Log("[HJXR Reference] Prepared zero camera-height offset; reference selection deferred until OpenXR input starts.");
        }
        public void ApplyTrackingReference(UnityEngine.XR.XRInputSubsystem input)
        {
            if (!Requested || input == null || !input.running || placement == null) return;
            var supported = input.GetSupportedTrackingOriginModes();
            bool applied = input.TrySetTrackingOriginMode(UnityEngine.XR.TrackingOriginModeFlags.Device);
            var actual = input.GetTrackingOriginMode();
            var origin = placement.GetComponent<Unity.XR.CoreUtils.XROrigin>();
            if (origin != null)
            {
                origin.CameraYOffset = 0;
                origin.RequestedTrackingOriginMode = Unity.XR.CoreUtils.XROrigin.TrackingOriginMode.NotSpecified;
                origin.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                origin.CameraFloorOffsetObject.transform.localPosition = Vector3.zero;
            }
            Debug.Log($"[HJXR Reference] Active OpenXR input={input.subsystemDescriptor.id} supported={supported} LOCAL request={applied} actual={actual}. Session-local tracking; place the vehicle on the floor explicitly.");
        }
        public IEnumerator InitializeAfterOpenXR()
        {
            if (!Requested) yield break;
            // The SDK and API layer must share the same installed DLL. Never substitute the old patch automatically.
            string path = Environment.GetEnvironmentVariable("CARAR_HJXR_SDK");
            sdk = new HjxrSession();
            var result = sdk.Initialize(path);
            if (result != HjxrResult.Success) { Status = "HJXR SDK unavailable: " + result; Debug.LogError("[HJXR] " + Status); sdk.Dispose(); sdk = null; yield break; }
            Debug.Log("[HJXR] SDK interface " + sdk.Version + " loaded: " + path);
            // This installed SDK implements SetLayerEnable, but GetLayerEnable is a no-op
            // that incorrectly returns Success. Do not infer state from an enum entry alone.
            var info = new HjxrLayer { layer = 1, enabled = 1 };
            result = sdk.Command(HjxrCommand.SetLayerEnable, ref info);
            Status = result == HjxrResult.Success ? "VST enable requested; check headset camera image" : "VST request failed: " + result;
            Debug.Log("[HJXR] " + Status);
            var vr = new HjxrLayer { layer = 0, enabled = 1 };
            Debug.Log("[HJXR] VR enable: " + sdk.Command(HjxrCommand.SetLayerEnable, ref vr));
            var vstOrder = new HjxrLayerOrder { layer = 1, zorder = 0 };
            var vrOrder = new HjxrLayerOrder { layer = 0, zorder = 1 };
            Debug.Log("[HJXR] VST background order: " + sdk.Command(HjxrCommand.SetLayerZOrder, ref vstOrder));
            Debug.Log("[HJXR] VR foreground order: " + sdk.Command(HjxrCommand.SetLayerZOrder, ref vrOrder));
            int headLock = 0;
            Debug.Log("[HJXR] World-locked VR request (head lock off): " + sdk.Command(HjxrCommand.SetHeadLock, ref headLock));
            // The installed SDK queues xrLocateViews poses by this count before using them
            // for projection-layer submission. Avoid an extra stale pose in Unity's frame loop.
            int poseDefer = 0;
            Debug.Log("[HJXR Alignment] Layer pose defer=0 requested: " + sdk.Command(HjxrCommand.SetLayerPoseDefer, ref poseDefer));
            alignmentPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../hjxr-alignment.json"));
            CheckAlignmentRequest();
            Debug.Log("[HJXR] Native tracking and view poses; physical-marker calibration and input corrections removed.");
            var poseTiming = new HjxrLayerPoseTimeOffset { layer = 1, timeOffsetNs = long.MinValue };
            result = sdk.Command(HjxrCommand.GetLayerPoseTimeOffset, ref poseTiming);
            Debug.Log(result == HjxrResult.Success && poseTiming.timeOffsetNs != long.MinValue ?
                $"[HJXR Timing] VST pose offset = {poseTiming.timeOffsetNs / 1000000.0:F3}ms" : "[HJXR Timing] VST pose-offset readback unavailable; no calibration value guessed.");
            ulong vsync = ulong.MaxValue;
            result = sdk.Command(HjxrCommand.GetVstVsyncOffset, ref vsync);
            Debug.Log(result == HjxrResult.Success && vsync != ulong.MaxValue ?
                $"[HJXR Timing] VST VSYNC offset = {vsync / 1000000.0:F3}ms" : "[HJXR Timing] VST VSYNC-offset readback unavailable.");
            yield return null;
            yield return new WaitForSeconds(12);
            placement.LogVisibility();
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--xr-capture") >= 0)
            {
                // GPU readbacks and extra scene renders interrupt frame delivery; opt in for diagnostics.
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.dataPath, "../xr-mirror.png"));
                CaptureScene();
            }
        }
        void CaptureScene()
        {
            // The native compositor mirror is not always included in ScreenCapture.
            // Render a separate mono diagnostic with the current HMD pose; leave the XR camera untouched.
            var source = Camera.main;
            if (source == null) return;
            var go = new GameObject("MR diagnostic camera");
            var camera = go.AddComponent<Camera>();
            camera.CopyFrom(source);
            camera.enabled = false;
            camera.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            var data = camera.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.allowXRRendering = false;
            var target = RenderTexture.GetTemporary(1280, 800, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.aspect = 1.6f; camera.fieldOfView = 65;
                camera.ResetProjectionMatrix();
                camera.Render();
                RenderTexture.active = target;
                var texture = new Texture2D(1280, 800, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); texture.Apply();
                int transparent = 0, opaque = 0;
                foreach (var pixel in texture.GetPixels32()) { if (pixel.a < 5) transparent++; if (pixel.a > 250) opaque++; }
                Debug.Log($"[HJXR Alpha] preserveFramebufferAlpha={Graphics.preserveFramebufferAlpha} transparent={transparent} opaque={opaque} total={1280 * 800}. Diagnostic mono render; headset composition requires visual verification.");
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(Application.dataPath, "../xr-scene.png"), texture.EncodeToPNG());
                Destroy(texture);
                var controls = GetComponent<MRTKShowroomUI>();
                if (controls != null && controls.ControlPanel != null)
                {
                    camera.transform.SetPositionAndRotation(controls.ControlPanel.position - controls.ControlPanel.forward * 1.35f, controls.ControlPanel.rotation);
                    SaveDiagnosticView(camera, target, "xr-controls.png");
                }
                var showroom = GetComponent<CarShowroom>();
                if (showroom != null)
                {
                    camera.transform.position = showroom.car.position + new Vector3(1f, .85f, -1.25f) * Mathf.Max(.7f, showroom.car.localScale.x / .25f);
                    camera.transform.LookAt(showroom.car.position + Vector3.up * showroom.car.localScale.x * .6f);
                    SaveDiagnosticView(camera, target, "xr-car-quality.png");
                }
            }
            finally { camera.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); Destroy(go); }
        }
        void SaveDiagnosticView(Camera camera, RenderTexture target, string file)
        {
            camera.Render(); RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0); texture.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(Application.dataPath, "../" + file), texture.EncodeToPNG());
            Destroy(texture);
        }
        void Update()
        {
            if (Requested && sdk != null && sdk.IsInitialized && Time.unscaledTime >= nextAlignmentCheck)
            {
                nextAlignmentCheck = Time.unscaledTime + .5f;
                CheckAlignmentRequest();
            }
            if (Application.isFocused && Requested && Keyboard.current?.rKey.wasPressedThisFrame == true) placement.Recenter();
        }
        void CheckAlignmentRequest()
        {
            if (alignmentPath == null || !File.Exists(alignmentPath)) return;
            try
            {
                var stamp = File.GetLastWriteTimeUtc(alignmentPath);
                if (stamp == alignmentStamp) return;
                alignmentStamp = stamp;
                var request = JsonUtility.FromJson<AlignmentRequest>(File.ReadAllText(alignmentPath));
                if (request == null || request.poseDeferFrames < 0 || request.poseDeferFrames > 1 ||
                    float.IsNaN(request.videoPoseOffsetMs) || float.IsInfinity(request.videoPoseOffsetMs) ||
                    (request.changeVideoTiming && (request.videoPoseOffsetMs < -30 || request.videoPoseOffsetMs > 60)))
                {
                    Debug.LogWarning("[HJXR Alignment] Invalid session alignment request; ignored."); return;
                }
                int frames = request.poseDeferFrames;
                Debug.Log($"[HJXR Alignment] Session layer pose defer={frames}: " + sdk.Command(HjxrCommand.SetLayerPoseDefer, ref frames));
                if (request.changeVideoTiming)
                {
                    var timing = new HjxrLayerPoseTimeOffset { layer = 1, timeOffsetNs = (long)Math.Round(request.videoPoseOffsetMs * 1000000.0) };
                    Debug.Log($"[HJXR Alignment] Session VST pose offset={request.videoPoseOffsetMs:F2}ms: " + sdk.Command(HjxrCommand.SetLayerPoseTimeOffset, ref timing));
                }
                // These SDK setters change this process's resource manager. Never persist
                // them to the vendor's calibration or call SaveConfigToFile here.
            }
            catch (Exception error) when (error is IOException || error is ArgumentException)
            { Debug.LogWarning("[HJXR Alignment] Request unreadable: " + error.Message); }
        }
        public void Shutdown()
        {
            if (sdk == null) return;
            // VST changes live in this process's API layer. We never call SaveConfigToFile.
            sdk.Dispose(); sdk = null;
        }
        void OnDestroy()
        {
            Shutdown();
            if (headTrackingState != null)
            {
                var origin = placement != null ? placement.GetComponent<Unity.XR.CoreUtils.XROrigin>() : null;
                var camera = origin != null ? origin.Camera : null;
                var driver = camera != null ? camera.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>() : null;
                if (driver != null && driver.trackingStateInput.action == headTrackingState)
                    driver.trackingStateInput = default;
                headTrackingState.Dispose();
            }
        }
    }
}
