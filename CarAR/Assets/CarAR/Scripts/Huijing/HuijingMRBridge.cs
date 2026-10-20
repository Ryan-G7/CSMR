using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
namespace CarAR.Huijing
{
    public class HuijingMRBridge : MonoBehaviour
    {
        public Transform virtualMarker;
        public CarPlacementManager placement;
        public Vector3 carOffsetFromMarker = new Vector3(0, 0, 4.5f);
        // Physical black-square side, excluding the white border; measured by the user as 6.78 cm.
        // Keep this outline consistent with the installed vendor marker YAML and Core.marker.size.
        public float markerLength = .0678f;
        public string Status { get; private set; } = "Huijing MR not enabled";
        public string AlignmentStatus { get; private set; } = "设备空间定位 · 未做实体标记对齐";
        public bool AlignmentPending { get; private set; }
        public bool HasMarkerRequest { get; private set; }
        public bool MarkerRestartRequired { get; private set; }
        HjxrSession sdk;
        string alignmentPath;
        DateTime alignmentStamp;
        float nextAlignmentCheck;
        InputAction headTrackingState;
        GameObject markerOutline;
        float markerRequestTime;
        float markerOutlineTime;
        float nextMarkerPoll;
        HjxrMarkerFeedback markerFeedback;
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
            Debug.Log($"[HJXR Reference] Active OpenXR input={input.subsystemDescriptor.id} supported={supported} LOCAL request={applied} actual={actual}. Session reference only; physical registration requires headset verification.");
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
            int trackedInput = 0;
            Debug.Log("[HJXR Marker Input] Vendor render alignment mode (trackLocateSpace=0): " + sdk.Command(HjxrCommand.SetTrackLocateSpace, ref trackedInput) +
                "; Unity input hook=" + HjxrMarkerTrackingFeature.HookInstalled + ". Rendering remains native.");
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
        public void RequestAlignment()
        {
            if (AlignmentPending || MarkerRestartRequired) return;
            if (sdk == null || !sdk.IsInitialized || virtualMarker == null)
            {
                AlignmentStatus = "标记对齐不可用，请检查头显连接";
                return;
            }
            if (!HjxrMarkerTrackingFeature.HookInstalled)
            { FinishMarkerRequest("定位输入适配未启动，请重启程序", true); return; }
            var pose = HjxrPose.FromUnity(virtualMarker.position, virtualMarker.rotation);
            var nativeMarker = Matrix4x4.TRS(new Vector3(pose.px, pose.py, pose.pz), new Quaternion(pose.qx, pose.qy, pose.qz, pose.qw), Vector3.one);
            markerFeedback?.Dispose();
            var requestedAt = DateTime.Now;
            markerFeedback = new HjxrMarkerFeedback(Application.consoleLogPath, nativeMarker, requestedAt);
            // The supplied board is a single DICT_6X6_50 / ID 0 marker, not a cube.
            int cube = 0;
            var result = sdk.Command(HjxrCommand.SetUseCubeMarker, ref cube);
            Debug.Log("[HJXR Marker] Single-board mode requested: " + result);
            if (result != HjxrResult.Success) { FinishMarkerRequest("定位板模式设置失败", false); return; }
            int trackedSpaces = 0;
            result = sdk.Command(HjxrCommand.SetTrackLocateSpace, ref trackedSpaces);
            Debug.Log("[HJXR Marker] Vendor render alignment requested (trackLocateSpace=0): " + result + ". Head/controller inputs will use the same calculated transform.");
            if (result != HjxrResult.Success) { FinishMarkerRequest("设备坐标对齐请求失败", false); return; }
            result = sdk.Command(HjxrCommand.WorldSetMarkerPose, ref pose);
            int count = 1; // SDK 2.5.5 section 7.4.6: asynchronous marker-sync count.
            if (result == HjxrResult.Success) result = sdk.Command(HjxrCommand.WorldSync, ref count);
            if (result != HjxrResult.Success) { Status = "Marker alignment request failed: " + result; FinishMarkerRequest("标记对齐请求失败", false); Debug.LogWarning("[HJXR] " + Status); return; }
            // WorldSync requests an asynchronous vendor operation, not proof that a physical marker was detected.
            Status = "Marker alignment requested; verify physical alignment";
            AlignmentStatus = "定位板校准已请求 · 请对准板子保持不动";
            HasMarkerRequest = true; AlignmentPending = true; markerRequestTime = Time.unscaledTime;
            ShowMarkerOutline();
            // Accepting WorldSync is not a marker-detection acknowledgement. In particular,
            // never move the parked car to a guessed marker position on this return value.
            Debug.Log($"[HJXR] {Status}; requestLocalTime={requestedAt:yyyy-MM-dd HH:mm:ss.fff}. Parked model transform preserved; no marker-detection success has been inferred.");
        }
        void ShowMarkerOutline()
        {
            if (markerOutline == null)
            {
                markerOutline = new GameObject("Physical marker registration check (vertical XY plane)");
                markerOutline.transform.SetParent(virtualMarker, false);
                var outline = markerOutline.AddComponent<LineRenderer>();
                outline.sharedMaterial = GetComponent<MRTKControllerInput>().rayMaterial;
                outline.useWorldSpace = false; outline.positionCount = 5;
                outline.widthMultiplier = .002f;
                outline.startColor = outline.endColor = new Color(.15f, 1f, .7f);
                float h = markerLength * .5f;
                // Native marker coordinates use XY. This reference is vertical, not a floor plane.
                outline.SetPositions(new[] { new Vector3(-h,h,0), new Vector3(h,h,0), new Vector3(h,-h,0), new Vector3(-h,-h,0), new Vector3(-h,h,0) });
                outline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                outline.receiveShadows = false;
            }
            markerOutline.SetActive(true);
            markerOutlineTime = Time.unscaledTime;
        }
        void Update()
        {
            if (AlignmentPending && Time.unscaledTime >= nextMarkerPoll)
            {
                nextMarkerPoll = Time.unscaledTime + .25f;
                markerFeedback?.Poll();
                if (markerFeedback?.State == HjxrMarkerFeedback.Outcome.CameraUnavailable)
                    FinishMarkerRequest("校准失败：识别相机图像不可用，请重启程序", true);
                else if (markerFeedback?.State == HjxrMarkerFeedback.Outcome.DetectionStopped)
                    FinishMarkerRequest("识别已结束，未收到对齐结果，请对准定位板后重试", false);
                else if (markerFeedback?.State == HjxrMarkerFeedback.Outcome.AlignmentCalculated)
                {
                    HjxrMarkerTrackingFeature.SetAlignment(markerFeedback.NativeAlignment);
                    placement.RefreshPlacementPreviewAfterAlignment();
                    Debug.Log("[HJXR Marker Input] Applied complete native alignment to tracked input; spaces=" + HjxrMarkerTrackingFeature.InputSpaceCount +
                        $"; evidence={markerFeedback.Evidence}; evidenceAt={markerFeedback.EvidenceAt:HH:mm:ss.fff}; completedAt={markerFeedback.CompletedAt:HH:mm:ss.fff}; staleRecordsIgnored={markerFeedback.StaleRecordsIgnored}. Native xrLocateViews remains responsible for rendered eye poses.");
                    FinishMarkerRequest("已识别定位板 · 请核对绿框，再选择停放位置", false);
                    ShowMarkerOutline();
                }
                else if (Time.unscaledTime - markerRequestTime >= 30f)
                    FinishMarkerRequest("校准超时，未收到原厂结果，请重启程序后重试", true);
                else if (Time.unscaledTime - markerRequestTime >= 12f)
                    AlignmentStatus = "尚未收到对齐结果 · 请继续看清完整定位板";
            }
            if (markerOutline != null && markerOutline.activeSelf && Time.unscaledTime - markerOutlineTime >= 30f)
                markerOutline.SetActive(false);
            if (Requested && sdk != null && sdk.IsInitialized && Time.unscaledTime >= nextAlignmentCheck)
            {
                nextAlignmentCheck = Time.unscaledTime + .5f;
                CheckAlignmentRequest();
            }
            if (Application.isFocused && Requested && Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame) RequestAlignment();
            if (Application.isFocused && Requested && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) placement.Recenter();
        }
        void FinishMarkerRequest(string message, bool restartRequired)
        {
            AlignmentPending = false; MarkerRestartRequired = restartRequired;
            AlignmentStatus = message;
            markerFeedback?.Dispose(); markerFeedback = null;
            if (markerOutline != null) markerOutline.SetActive(false);
            Debug.Log("[HJXR Marker Feedback] " + message);
            StartCoroutine(ShowMarkerResult(message));
        }
        IEnumerator ShowMarkerResult(string message)
        {
            // Let the native TPD consume the new input coordinates before positioning UI.
            yield return null;
            GetComponent<MRTKShowroomUI>().ShowAlignmentResult(message);
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
            markerFeedback?.Dispose(); markerFeedback = null;
            if (sdk == null) return;
            // VST changes live in this process's API layer. We never call SaveConfigToFile.
            sdk.Dispose(); sdk = null;
        }
        void OnDestroy()
        {
            Shutdown();
            var driver = placement != null ? placement.GetComponent<Unity.XR.CoreUtils.XROrigin>()?.Camera.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>() : null;
            if (headTrackingState != null)
            {
                if (driver != null && driver.trackingStateInput.action == headTrackingState)
                    driver.trackingStateInput = default;
                headTrackingState.Dispose();
            }
        }
    }
}
