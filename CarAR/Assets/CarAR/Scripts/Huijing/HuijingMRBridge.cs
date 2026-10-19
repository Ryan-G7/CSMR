using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
namespace CarAR.Huijing
{
    public class HuijingMRBridge : MonoBehaviour
    {
        public Transform virtualMarker;
        public CarPlacementManager placement;
        public Vector3 carOffsetFromMarker = new Vector3(0, 0, 4.5f);
        public string Status { get; private set; } = "Huijing MR not enabled";
        HjxrSession sdk;
        public static bool Requested => Array.IndexOf(Environment.GetCommandLineArgs(), "--hjxr") >= 0;
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
            var poseTiming = new HjxrLayerPoseTimeOffset { layer = 1, timeOffsetNs = long.MinValue };
            result = sdk.Command(HjxrCommand.GetLayerPoseTimeOffset, ref poseTiming);
            Debug.Log(result == HjxrResult.Success && poseTiming.timeOffsetNs != long.MinValue ?
                $"[HJXR Timing] VST pose offset = {poseTiming.timeOffsetNs / 1000000.0:F3}ms" : "[HJXR Timing] VST pose-offset readback unavailable; no calibration value guessed.");
            ulong vsync = ulong.MaxValue;
            result = sdk.Command(HjxrCommand.GetVstVsyncOffset, ref vsync);
            Debug.Log(result == HjxrResult.Success && vsync != ulong.MaxValue ?
                $"[HJXR Timing] VST VSYNC offset = {vsync / 1000000.0:F3}ms" : "[HJXR Timing] VST VSYNC-offset readback unavailable.");
            if (virtualMarker != null)
            {
                var pose = HjxrPose.FromUnity(virtualMarker.position, virtualMarker.rotation);
                result = sdk.Command(HjxrCommand.WorldSetMarkerPose, ref pose);
                Debug.Log("[HJXR] Virtual marker pose result: " + result + ". Press K with the real marker visible to request alignment.");
            }
            yield return null;
            yield return new WaitForSeconds(12);
            placement.LogVisibility();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.dataPath, "../xr-mirror.png"));
            CaptureScene();
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
            if (sdk == null || !sdk.IsInitialized || virtualMarker == null) return;
            var pose = HjxrPose.FromUnity(virtualMarker.position, virtualMarker.rotation);
            var result = sdk.Command(HjxrCommand.WorldSetMarkerPose, ref pose);
            int count = 1; // SDK 2.5.5 section 7.4.6: asynchronous marker-sync count.
            if (result == HjxrResult.Success) result = sdk.Command(HjxrCommand.WorldSync, ref count);
            if (result != HjxrResult.Success) { Status = "Marker alignment request failed: " + result; Debug.LogWarning("[HJXR] " + Status); return; }
            // WorldSync requests an asynchronous vendor operation, not proof that a physical marker was detected.
            Status = "Marker alignment requested; verify physical alignment";
            if (placement != null) placement.PlaceOnReferenceFloor(virtualMarker.TransformPoint(carOffsetFromMarker));
            Debug.Log("[HJXR] " + Status + ". Car uses the configured marker floor, not a detected AR plane.");
        }
        void Update()
        {
            if (Application.isFocused && Requested && Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame) RequestAlignment();
            if (Application.isFocused && Requested && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) placement.Recenter();
        }
        public void Shutdown()
        {
            if (sdk == null) return;
            // VST changes live in this process's API layer. We never call SaveConfigToFile.
            sdk.Dispose(); sdk = null;
        }
        void OnDestroy() => Shutdown();
    }
}
