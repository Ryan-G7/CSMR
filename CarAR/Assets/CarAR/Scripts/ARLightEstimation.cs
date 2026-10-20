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
        [SerializeField] float baseSunIntensity = 1.2f;
        float targetIntensity = 1.2f, lastSample = -100f;
        Color targetColor = Color.white;
        Quaternion targetRotation = Quaternion.Euler(50f, -30f, 0f);
        SphericalHarmonicsL2 targetAmbient;
        bool hasAmbient, usingLiveData;
        public bool ManualLighting { get; private set; }
        public bool HasLiveData => Time.unscaledTime - lastSample < 3f;
        public float Brightness { get; private set; } = .65f;
        public float Temperature { get; private set; } = 6500;
        public bool PreviewLighting { get; private set; }
        public string DisplayStatus => ManualLighting ? $"手动大厅光照 · {Brightness:F2} / {Temperature:F0}K" : HasLiveData ? "环境光照数据已接入" : "大厅光照预设 · 设备暂未提供实时测光";
        public string Status => ManualLighting ? "Manual hall lighting" : PreviewLighting ? "Preview light simulation" : HasLiveData ? "Live environment light data" : "No environment light data / default light";
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
            float? lumens = le.mainLightIntensityLumens ?? le.averageIntensityInLumens;
            if (lumens.HasValue && float.IsFinite(lumens.Value)) { targetIntensity = Mathf.Clamp(lumens.Value / 1000f, 0.01f, 4f) * baseSunIntensity; received = true; }
            else if (le.averageBrightness.HasValue && float.IsFinite(le.averageBrightness.Value)) { targetIntensity = Mathf.Clamp(le.averageBrightness.Value, 0.01f, 4f) * baseSunIntensity; received = true; }
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
        void ClearLiveData() { usingLiveData = hasAmbient = false; lastSample = -100f; }
        void UsePreset()
        {
            ClearLiveData();
            Brightness = targetIntensity = .65f; Temperature = 6500; targetColor = Color.white;
            targetRotation = Quaternion.Euler(50f, -30f, 0f);
        }
        public void SetPreviewLight(float brightness, float temperature)
        {
            ClearLiveData(); ManualLighting = false; PreviewLighting = true;
            Brightness = targetIntensity = brightness; Temperature = temperature;
            targetColor = Mathf.CorrelatedColorTemperatureToRGB(temperature);
        }
        public void AdjustManual(float brightnessDelta, float temperatureDelta)
        {
            ClearLiveData(); ManualLighting = true; PreviewLighting = false;
            Brightness = Mathf.Clamp(Brightness + brightnessDelta, .05f, 2f);
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
            float t = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, smoothTime));
            if (sun != null)
            {
                sun.intensity = Mathf.Lerp(sun.intensity, targetIntensity, t);
                sun.color = Color.Lerp(sun.color, targetColor, t);
                sun.transform.rotation = Quaternion.Slerp(sun.transform.rotation, targetRotation, t);
            }
            if (hasAmbient)
            {
                RenderSettings.ambientMode = AmbientMode.Custom;
                var current = RenderSettings.ambientProbe;
                for (int c = 0; c < 3; c++) for (int i = 0; i < 9; i++) current[c, i] = Mathf.Lerp(current[c, i], targetAmbient[c, i], t);
                RenderSettings.ambientProbe = current;
            }
            else
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.Lerp(RenderSettings.ambientLight, targetColor * (0.12f + targetIntensity * 0.18f), t);
            }
        }
    }
}
