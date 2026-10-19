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
        public const int ReflectionLayer = 30;
        public bool HighQuality { get; private set; } = true;
        Transform shadow, contact, reflectionRoom;
        ReflectionProbe fallbackProbe;
        Material roomMaterial, lightMaterial, floorMaterial;

        void Start()
        {
            foreach (var renderer in showroom.car.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    foreach (var property in material.GetTexturePropertyNames())
                    {
                        var texture = material.GetTexture(property);
                        if (texture != null) { texture.anisoLevel = 16; texture.mipMapBias = 0; }
                    }
            // Native Unity probe convolution produces the roughness mip chain for PBR paint.
            // This fallback represents a neutral hall, and never pretends to be a camera scan.
            CreateHallReflection();
            if (!showroom.IsPreview)
            {
                shadow = MakePlane("Transparent ground shadow receiver", shadowMaterial);
                contact = MakePlane("Soft chassis contact shadow", contactMaterial);
            }
            Debug.Log("[CarAR Quality] Native resolution, 4x MSAA, 4096 soft shadows, native 512px hall reflection probe. Hall reflection is a preset; live AR probes take priority if provided.");
        }
        Material Unlit(Color color)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", color); material.SetFloat("_Cull", 0);
            return material;
        }
        void Surface(string name, Vector3 position, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            go.layer = ReflectionLayer; go.transform.SetParent(reflectionRoom, false);
            go.transform.localPosition = position; go.transform.localScale = size;
            Destroy(go.GetComponent<Collider>());
            var renderer = go.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
        void CreateHallReflection()
        {
            reflectionRoom = new GameObject("Neutral hall reflection environment (preset only)").transform;
            roomMaterial = Unlit(new Color(.29f, .31f, .34f));
            floorMaterial = Unlit(new Color(.09f, .10f, .12f));
            lightMaterial = Unlit(new Color(1.15f, 1.18f, 1.2f));
            Surface("Floor", new Vector3(0, -1, 0), new Vector3(24, .1f, 32), floorMaterial);
            Surface("Ceiling", new Vector3(0, 7, 0), new Vector3(24, .1f, 32), roomMaterial);
            Surface("Left wall", new Vector3(-12, 3, 0), new Vector3(.1f, 8, 32), roomMaterial);
            Surface("Right wall", new Vector3(12, 3, 0), new Vector3(.1f, 8, 32), roomMaterial);
            Surface("Front wall", new Vector3(0, 3, 16), new Vector3(24, 8, .1f), roomMaterial);
            Surface("Rear wall", new Vector3(0, 3, -16), new Vector3(24, 8, .1f), roomMaterial);
            for (int i = -1; i <= 1; i++)
                Surface("Ceiling softbox", new Vector3(i * 5, 6.8f, 0), new Vector3(1.8f, .08f, 18), lightMaterial);
            Surface("Window bank", new Vector3(-11.9f, 3, 0), new Vector3(.06f, 3.5f, 12), lightMaterial);
            fallbackProbe = new GameObject("Hall fallback reflection probe (preset)").AddComponent<ReflectionProbe>();
            fallbackProbe.transform.position = Vector3.up * 1.2f;
            fallbackProbe.mode = ReflectionProbeMode.Realtime;
            fallbackProbe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            fallbackProbe.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            fallbackProbe.cullingMask = 1 << ReflectionLayer;
            fallbackProbe.resolution = 512; fallbackProbe.hdr = true;
            fallbackProbe.size = Vector3.one * 1000; fallbackProbe.importance = 0;
            fallbackProbe.boxProjection = false; fallbackProbe.intensity = .7f;
            fallbackProbe.clearFlags = ReflectionProbeClearFlags.SolidColor;
            fallbackProbe.backgroundColor = Color.gray * .3f;
            fallbackProbe.RenderProbe();
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
            bool active = showroom.car.gameObject.activeInHierarchy;
            shadow.gameObject.SetActive(active); contact.gameObject.SetActive(active);
            if (!active) return;
            var car = showroom.car;
            float scale = car.localScale.x;
            shadow.position = new Vector3(car.position.x, showroom.placement.GroundHeight + .001f, car.position.z);
            shadow.localScale = Vector3.one * Mathf.Max(.5f, scale * .9f);
            contact.position = shadow.position + Vector3.up * .001f;
            contact.rotation = car.rotation;
            var size = showroom.gestures.ModelSize;
            contact.localScale = new Vector3(size.x * .095f, .1f, size.z * .095f) * scale;
            contactMaterial.SetFloat("_Strength", showroom.explode.Progress > .1f ? .04f : .16f);
        }
        void OnDestroy()
        {
            if (shadow != null) Destroy(shadow.gameObject);
            if (contact != null) Destroy(contact.gameObject);
            if (fallbackProbe != null) Destroy(fallbackProbe.gameObject);
            if (reflectionRoom != null) Destroy(reflectionRoom.gameObject);
            if (roomMaterial != null) Destroy(roomMaterial);
            if (lightMaterial != null) Destroy(lightMaterial);
            if (floorMaterial != null) Destroy(floorMaterial);
        }
    }
}
