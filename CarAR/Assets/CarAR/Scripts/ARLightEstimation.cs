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
        bool hasAmbient;
        public bool ManualLighting { get; private set; }
        public bool HasLiveData => Time.unscaledTime - lastSample < 3f;
        public float Brightness { get; private set; } = .65f;
        public float Temperature { get; private set; } = 6500;
        public bool PreviewLighting { get; private set; }
        public string DisplayStatus => ManualLighting ? $"手动大厅光照 · {Brightness:F2} / {Temperature:F0}K" : HasLiveData ? "环境光照数据已接入" : "大厅光照预设 · 设备暂未提供实时测光";
        public string Status => ManualLighting ? "Manual hall lighting" : PreviewLighting ? "Preview light simulation" : Time.unscaledTime - lastSample < 3f ? "Live environment light data" : "No environment light data / default light";
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
            if (le.mainLightColor.HasValue) { targetColor = le.mainLightColor.Value; received = true; }
            else if (le.colorCorrection.HasValue) { targetColor = le.colorCorrection.Value; received = true; }
            else if (le.averageColorTemperature.HasValue) { targetColor = Mathf.CorrelatedColorTemperatureToRGB(le.averageColorTemperature.Value); received = true; }
            if (le.mainLightDirection.HasValue && le.mainLightDirection.Value.sqrMagnitude > 0.001f)
            {
                var origin = cameraManager.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>();
                var direction = origin != null ? origin.transform.TransformDirection(le.mainLightDirection.Value) : le.mainLightDirection.Value;
                targetRotation = Quaternion.LookRotation(direction); received = true;
            }
            if (le.ambientSphericalHarmonics.HasValue) { targetAmbient = le.ambientSphericalHarmonics.Value; hasAmbient = true; received = true; }
            if (received) { lastSample = Time.unscaledTime; PreviewLighting = false; }
        }
        public void SetPreviewLight(float brightness, float temperature)
        {
            PreviewLighting = true; hasAmbient = false;
            targetIntensity = brightness;
            targetColor = Mathf.CorrelatedColorTemperatureToRGB(temperature);
        }
        public void AdjustManual(float brightnessDelta, float temperatureDelta)
        {
            ManualLighting = true; PreviewLighting = false; hasAmbient = false;
            Brightness = Mathf.Clamp(Brightness + brightnessDelta, .05f, 2f);
            Temperature = Mathf.Clamp(Temperature + temperatureDelta, 2500, 10000);
            targetIntensity = Brightness; targetColor = Mathf.CorrelatedColorTemperatureToRGB(Temperature);
        }
        public void RequestAuto()
        {
            ManualLighting = PreviewLighting = false;
            if (!HasLiveData) { targetIntensity = .65f; targetColor = Color.white; }
        }
        public void RotateManual(float angle)
        {
            ManualLighting = true; PreviewLighting = false;
            targetRotation = Quaternion.Euler(0, angle, 0) * targetRotation;
        }
        void Start() { if (!PreviewLighting) targetIntensity = .65f; }
        void Update()
        {
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
