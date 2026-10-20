using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CarAR
{
    public class MRPresentation : MonoBehaviour
    {
        public CarShowroom showroom;
        public Material shadowMaterial, contactMaterial;
        public ScriptableRendererFeature ambientOcclusion;
        public bool HighQuality { get; private set; } = true;
        Transform shadow, contact;

        void Start()
        {
            foreach (var renderer in showroom.car.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    foreach (var property in material.GetTexturePropertyNames())
                    {
                        var texture = material.GetTexture(property);
                        if (texture != null) { texture.anisoLevel = 16; texture.mipMapBias = 0; }
                    }
            if (!showroom.IsPreview)
            {
                shadow = MakePlane("Transparent ground shadow receiver", shadowMaterial);
                contact = MakePlane("Soft chassis contact shadow", contactMaterial);
            }
            Debug.Log("[CarAR Quality] Native resolution, 4x MSAA, SSAO, imported HDR diffuse SH and prefiltered environment reflections. Direct fill defaults to zero; local AR probes take priority when available.");
        }
        public void ToggleQuality()
        {
            HighQuality = !HighQuality;
            var pipeline = (UniversalRenderPipelineAsset)QualitySettings.renderPipeline;
            pipeline.renderScale = HighQuality ? 1f : .8f;
            if (ambientOcclusion != null) ambientOcclusion.SetActive(HighQuality);
            pipeline.mainLightShadowmapResolution = HighQuality ? 4096 : 2048;
            Debug.Log("[CarAR Quality] " + (HighQuality ? "Native resolution / 4096 shadows" : "80% resolution / 2048 shadows"));
        }
        Transform MakePlane(string name, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane); go.name = name;
            Destroy(go.GetComponent<Collider>());
            var renderer = go.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
            return go.transform;
        }
        void LateUpdate()
        {
            if (shadow == null) return;
            bool active = showroom.car.gameObject.activeInHierarchy && showroom.placement.IsPlaced;
            shadow.gameObject.SetActive(active); contact.gameObject.SetActive(active);
            if (!active) return;
            var car = showroom.car;
            float scale = car.localScale.x;
            shadow.position = new Vector3(car.position.x, showroom.placement.GroundHeight + .001f, car.position.z);
            shadow.localScale = Vector3.one * Mathf.Max(.5f, scale * .9f);
            contact.position = shadow.position + Vector3.up * .001f;
            contact.rotation = CarPlacementManager.FloorRotation(car.rotation);
            var size = showroom.gestures.ModelSize;
            contact.localScale = new Vector3(size.x * .095f, .1f, size.z * .095f) * scale;
            contactMaterial.SetFloat("_Strength", showroom.explode.Progress > .1f ? .04f : .16f);
        }
        void OnDestroy()
        {
            if (shadow != null) Destroy(shadow.gameObject);
            if (contact != null) Destroy(contact.gameObject);
        }
    }
}
