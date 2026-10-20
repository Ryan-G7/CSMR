using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;

namespace CarAR
{
    public class ARLightEstimation : MonoBehaviour
    {
        [SerializeField] ARCameraManager cameraManager;
        [SerializeField] Light sun;
        [SerializeField] float smoothTime = 0.4f;
        [SerializeField] float baseSunIntensity = 1f;
        [SerializeField] float[] environmentCoefficients;
        float targetIntensity, lastSample = -100f;
        float environmentIntensity = 1;
        SphericalHarmonicsL2 environmentAmbient;
        Color targetColor = Color.white;
        Quaternion targetRotation = Quaternion.Euler(50f, -30f, 0f);
        SphericalHarmonicsL2 targetAmbient;
        bool hasAmbient, usingLiveData;
        float nextSourceCheck;
        string reportedSource;
        public bool ManualLighting { get; private set; }
        public bool HasLiveData => Time.unscaledTime - lastSample < 3f;
        public bool HasCameraProvider => cameraManager != null && cameraManager.isActiveAndEnabled && cameraManager.subsystem != null && cameraManager.subsystem.running;
        public float Brightness { get; private set; }
        public float Temperature { get; private set; } = 6500;
        public bool PreviewLighting { get; private set; }
        public string DisplayStatus => ManualLighting || PreviewLighting ? $"静态 HDR + 补光 · {Brightness:F2} / {Temperature:F0}K" : HasLiveData ? "现实环境测光已接入" : HasCameraProvider ? "静态 HDR · 等待有效测光" : "静态 HDR · 现实测光未接入";
        public string Status => ManualLighting ? "Static HDR + manual fill" : PreviewLighting ? "Static HDR + preview fill" : HasLiveData ? "Live environment light data" : HasCameraProvider ? "Static HDR / waiting for light data" : "Static HDR / no running AR camera provider";
        void Awake()
        {
            // RenderSettings.ambientProbe assigned in the editor is not stored
            // in the scene. Persist the HDR integral explicitly for the player.
            environmentAmbient = RenderSettings.ambientProbe;
            if (environmentCoefficients?.Length == 27)
                for (int c = 0; c < 3; c++) for (int i = 0; i < 9; i++)
                    environmentAmbient[c, i] = environmentCoefficients[c * 9 + i];
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = environmentAmbient;
        }
        void OnEnable()
        {
            if (cameraManager == null) return;
            cameraManager.requestedLightEstimation = LightEstimation.AmbientIntensity | LightEstimation.AmbientColor |
                LightEstimation.AmbientSphericalHarmonics | LightEstimation.MainLightDirection | LightEstimation.MainLightIntensity;
            cameraManager.frameReceived += OnFrame;
        }
        void OnDisable() { if (cameraManager != null) cameraManager.frameReceived -= OnFrame; }
        void OnFrame(ARCameraFrameEventArgs args)
        {
            if (ManualLighting || PreviewLighting) return;
            var le = args.lightEstimation;
            bool received = false;
            float? lumens = le.mainLightIntensityLumens;
            if (lumens.HasValue && float.IsFinite(lumens.Value)) { targetIntensity = Mathf.Clamp(lumens.Value / 1000f, 0.01f, 4f) * baseSunIntensity; received = true; }
            else if (le.averageBrightness.HasValue && float.IsFinite(le.averageBrightness.Value)) { environmentIntensity = Mathf.Clamp(le.averageBrightness.Value, .01f, 4f); received = true; }
            else if (le.averageIntensityInLumens.HasValue && float.IsFinite(le.averageIntensityInLumens.Value)) { environmentIntensity = Mathf.Clamp(le.averageIntensityInLumens.Value / 1000f, .01f, 4f); received = true; }
            if (le.mainLightColor.HasValue && Finite(le.mainLightColor.Value)) { targetColor = le.mainLightColor.Value; received = true; }
            else if (le.colorCorrection.HasValue && Finite(le.colorCorrection.Value)) { targetColor = le.colorCorrection.Value; received = true; }
            else if (le.averageColorTemperature.HasValue && float.IsFinite(le.averageColorTemperature.Value) && le.averageColorTemperature.Value > 0)
            { Temperature = le.averageColorTemperature.Value; targetColor = Mathf.CorrelatedColorTemperatureToRGB(Temperature); received = true; }
            if (le.mainLightDirection.HasValue && float.IsFinite(le.mainLightDirection.Value.sqrMagnitude) && le.mainLightDirection.Value.sqrMagnitude > 0.001f)
            {
                var origin = cameraManager.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>();
                var direction = origin != null ? origin.transform.TransformDirection(le.mainLightDirection.Value) : le.mainLightDirection.Value;
                targetRotation = Quaternion.LookRotation(direction); received = true;
            }
            if (le.ambientSphericalHarmonics.HasValue && Finite(le.ambientSphericalHarmonics.Value)) { targetAmbient = le.ambientSphericalHarmonics.Value; hasAmbient = true; received = true; }
            if (received) { lastSample = Time.unscaledTime; usingLiveData = true; Brightness = targetIntensity; }
        }
        static bool Finite(Color color) => float.IsFinite(color.r) && float.IsFinite(color.g) && float.IsFinite(color.b) && float.IsFinite(color.a);
        static bool Finite(SphericalHarmonicsL2 ambient)
        {
            for (int c = 0; c < 3; c++) for (int i = 0; i < 9; i++) if (!float.IsFinite(ambient[c, i])) return false;
            return true;
        }
        void ClearLiveData() { usingLiveData = hasAmbient = false; lastSample = -100f; environmentIntensity = 1; }
        void UsePreset()
        {
            ClearLiveData();
            Brightness = targetIntensity = 0; Temperature = 6500; targetColor = Color.white;
            targetRotation = Quaternion.Euler(50f, -30f, 0f);
        }
        public void SetPreviewLight(float brightness, float temperature)
        {
            ClearLiveData(); ManualLighting = false; PreviewLighting = true;
            Brightness = targetIntensity = Mathf.Clamp(brightness, 0, 2); Temperature = Mathf.Clamp(temperature, 2500, 10000);
            targetColor = Mathf.CorrelatedColorTemperatureToRGB(Temperature);
        }
        public void AdjustManual(float brightnessDelta, float temperatureDelta)
        {
            ClearLiveData(); ManualLighting = true; PreviewLighting = false;
            Brightness = Mathf.Clamp(Brightness + brightnessDelta, 0, 2f);
            Temperature = Mathf.Clamp(Temperature + temperatureDelta, 2500, 10000);
            targetIntensity = Brightness; targetColor = Mathf.CorrelatedColorTemperatureToRGB(Temperature);
        }
        public void RequestAuto()
        {
            ManualLighting = PreviewLighting = false;
            // Manual samples are ignored; automatic mode requires a fresh frame.
            UsePreset();
        }
        public void RotateManual(float angle)
        {
            ClearLiveData(); ManualLighting = true; PreviewLighting = false;
            targetRotation = Quaternion.Euler(0, angle, 0) * targetRotation;
        }
        void Start() { if (!PreviewLighting && !ManualLighting && !usingLiveData) UsePreset(); }
        void Update()
        {
            if (usingLiveData && !HasLiveData && !ManualLighting && !PreviewLighting) UsePreset();
            if (Time.unscaledTime >= nextSourceCheck)
            {
                nextSourceCheck = Time.unscaledTime + 1;
                string provider = HasCameraProvider ? cameraManager.subsystem.subsystemDescriptor.id : "none";
                string source = $"{Status}; cameraProvider={provider}; measuredFrames={HasLiveData}; activeFeatures={(HasCameraProvider ? cameraManager.currentLightEstimation : LightEstimation.None)}";
                if (source != reportedSource)
                {
                    reportedSource = source;
                    Debug.Log("[CarAR Lighting] " + source);
                }
            }
            float t = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, smoothTime));
            if (sun != null)
            {
                sun.intensity = Mathf.Lerp(sun.intensity, targetIntensity, t);
                sun.color = Color.Lerp(sun.color, targetColor, t);
                sun.transform.rotation = Quaternion.Slerp(sun.transform.rotation, targetRotation, t);
            }
            // Retain HDR diffuse lighting in fallback/manual modes. Never stack a
            // flat grey ambient preset on top of the environment.
            RenderSettings.ambientMode = AmbientMode.Custom;
            var desired = hasAmbient ? targetAmbient : environmentAmbient * environmentIntensity;
            if (usingLiveData && !hasAmbient)
                for (int c = 0; c < 3; c++) for (int i = 0; i < 9; i++) desired[c, i] *= targetColor[c];
            var current = RenderSettings.ambientProbe;
            for (int c = 0; c < 3; c++) for (int i = 0; i < 9; i++)
                current[c, i] = Mathf.Lerp(current[c, i], desired[c, i], t);
            RenderSettings.ambientProbe = current;
            RenderSettings.reflectionIntensity = environmentIntensity;
        }
    }
}
