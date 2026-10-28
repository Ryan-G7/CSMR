using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarAR
{
    public sealed class ModelReview : MonoBehaviour
    {
        sealed class Override
        {
            public Color color;
            public float metallic, roughness;
        }
        public Transform modelRoot;
        public RuntimeGlbLoader loader;
        public bool Clipping { get; private set; }
        public bool HandClipping { get; private set; }
        public int SelectedIndex { get; private set; }
        public int SelectedMeshIndex { get; private set; }
        public float PlaneOffset { get; private set; }
        public Vector3 PlaneNormal { get; private set; } = Vector3.right;
        public IReadOnlyList<Material> Materials => materials;
        public event System.Action MeshVisibilityChanged;
        readonly List<Material> materials = new();
        readonly List<Renderer> renderers = new();
        readonly Dictionary<Renderer, Material[]> originals = new();
        readonly Dictionary<Material, Override> overrides = new();
        readonly Dictionary<Material, Material> edited = new();
        readonly Dictionary<Material, Material> clipped = new();
        Shader clipShader;
        Transform clippingHand;

        void Start()
        {
            clipShader = Resources.Load<Shader>("ReviewClip");
            if (loader != null) loader.ModelChanged += RefreshModel;
            RefreshModel();
        }
        public void RefreshModel()
        {
            SetClipping(false);
            HandClipping = false;
            clippingHand = null;
            foreach (var material in edited.Values) Destroy(material);
            foreach (var material in clipped.Values) Destroy(material);
            originals.Clear(); overrides.Clear(); edited.Clear(); clipped.Clear(); materials.Clear(); renderers.Clear();
            foreach (var renderer in modelRoot.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = true;
                renderers.Add(renderer);
                var source = renderer.sharedMaterials;
                originals[renderer] = source;
                foreach (var material in source)
                    if (material != null && !materials.Contains(material)) materials.Add(material);
            }
            SelectedIndex = 0;
            SelectedMeshIndex = 0;
            PlaneOffset = 0;
            PlaneNormal = Vector3.right;
        }
        public Material SelectedMaterial => materials.Count == 0 ? null : materials[Mathf.Clamp(SelectedIndex, 0, materials.Count - 1)];
        public string SelectedName => SelectedMaterial == null ? "无材质" : $"{SelectedIndex + 1}/{materials.Count}  {SelectedMaterial.name}";
        public Renderer SelectedMesh => renderers.Count == 0 ? null : renderers[Mathf.Clamp(SelectedMeshIndex, 0, renderers.Count - 1)];
        public string SelectedMeshName => SelectedMesh == null ? "无网格" : $"{SelectedMeshIndex + 1}/{renderers.Count}  {SelectedMesh.name} · {(SelectedMesh.enabled ? "显示" : "隐藏")}";
        public void SelectMaterial(int delta)
        {
            if (materials.Count > 0) SelectedIndex = (SelectedIndex + delta % materials.Count + materials.Count) % materials.Count;
        }
        public void SelectMesh(int delta)
        {
            if (renderers.Count > 0) SelectedMeshIndex = (SelectedMeshIndex + delta % renderers.Count + renderers.Count) % renderers.Count;
        }
        public void ToggleSelectedMesh()
        {
            if (SelectedMesh == null) return;
            SelectedMesh.enabled = !SelectedMesh.enabled;
            MeshVisibilityChanged?.Invoke();
        }
        public void ShowAllMeshes()
        {
            foreach (var renderer in renderers) renderer.enabled = true;
            MeshVisibilityChanged?.Invoke();
        }
        Override Values(Material material)
        {
            if (overrides.TryGetValue(material, out var value)) return value;
            var color = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.HasProperty("_Color") ? material.GetColor("_Color") : material.HasProperty("baseColorFactor") ? material.GetColor("baseColorFactor") : Color.white;
            value = new Override
            {
                color = color,
                metallic = material.HasProperty("_Metallic") ? material.GetFloat("_Metallic") : material.HasProperty("metallicFactor") ? material.GetFloat("metallicFactor") : 0,
                roughness = material.HasProperty("_Roughness") ? material.GetFloat("_Roughness") : material.HasProperty("roughnessFactor") ? material.GetFloat("roughnessFactor") : material.HasProperty("_Smoothness") ? 1 - material.GetFloat("_Smoothness") : .5f
            };
            overrides.Add(material, value);
            return value;
        }
        public Color ColorValue => SelectedMaterial == null ? Color.white : Values(SelectedMaterial).color;
        public float Metallic => SelectedMaterial == null ? 0 : Values(SelectedMaterial).metallic;
        public float Roughness => SelectedMaterial == null ? .5f : Values(SelectedMaterial).roughness;
        public void SetColor(Color color) { if (SelectedMaterial != null) { Values(SelectedMaterial).color = color; Apply(SelectedMaterial); } }
        public void SetMetallic(float value) { if (SelectedMaterial != null) { Values(SelectedMaterial).metallic = Mathf.Clamp01(value); Apply(SelectedMaterial); } }
        public void SetRoughness(float value) { if (SelectedMaterial != null) { Values(SelectedMaterial).roughness = Mathf.Clamp01(value); Apply(SelectedMaterial); } }
        public void SetOpacity(float value)
        {
            if (SelectedMaterial == null) return;
            var color = Values(SelectedMaterial).color; color.a = Mathf.Clamp01(value); Values(SelectedMaterial).color = color; Apply(SelectedMaterial);
        }
        public void ResetMaterial()
        {
            var material = SelectedMaterial;
            if (material == null) return;
            overrides.Remove(material);
            if (edited.TryGetValue(material, out var clone)) { Destroy(clone); edited.Remove(material); }
            if (clipped.TryGetValue(material, out var clip)) { Destroy(clip); clipped.Remove(material); }
            foreach (var pair in originals)
            {
                var current = (Material[])pair.Value.Clone();
                if (Clipping) for (int i = 0; i < current.Length; i++) if (current[i] == material) current[i] = ClipMaterial(material);
                pair.Key.sharedMaterials = current;
            }
        }
        void Apply(Material original)
        {
            if (RequiresBlendShader(original))
            {
                ApplyValues(ClipMaterial(original), original);
                if (!Clipping) Assign(original, clipped[original]);
                return;
            }
            if (!edited.TryGetValue(original, out var clone))
            {
                clone = new Material(original) { name = original.name + " (review)" };
                edited.Add(original, clone);
            }
            ApplyValues(clone, original);
            if (Clipping) ApplyValues(ClipMaterial(original), original);
            if (!Clipping) Assign(original, clone);
        }
        bool RequiresBlendShader(Material original) => !original.HasProperty("_Surface") && Values(original).color.a < .999f;
        void ApplyValues(Material target, Material original)
        {
            var value = Values(original);
            if (target.HasProperty("_BaseColor")) target.SetColor("_BaseColor", value.color);
            if (target.HasProperty("_Color")) target.SetColor("_Color", value.color);
            if (target.HasProperty("baseColorFactor")) target.SetColor("baseColorFactor", value.color);
            if (target.HasProperty("_Metallic")) target.SetFloat("_Metallic", value.metallic);
            if (target.HasProperty("metallicFactor")) target.SetFloat("metallicFactor", value.metallic);
            if (target.HasProperty("_Roughness")) target.SetFloat("_Roughness", value.roughness);
            if (target.HasProperty("roughnessFactor")) target.SetFloat("roughnessFactor", value.roughness);
            if (target.HasProperty("_Smoothness")) target.SetFloat("_Smoothness", 1 - value.roughness);
            bool transparent = value.color.a < .999f;
            if (target.shader == clipShader || target.HasProperty("_Surface"))
            {
                if (target.HasProperty("_Surface")) target.SetFloat("_Surface", transparent ? 1 : 0);
                target.SetFloat("_SrcBlend", (float)(transparent ? BlendMode.SrcAlpha : BlendMode.One));
                target.SetFloat("_DstBlend", (float)(transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
                target.SetFloat("_ZWrite", transparent ? 0 : 1);
                if (target.HasProperty("_Surface"))
                {
                    if (transparent) target.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); else target.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                }
                target.renderQueue = transparent ? (int)RenderQueue.Transparent : (int)RenderQueue.Geometry;
            }
        }
        void Assign(Material original, Material replacement)
        {
            foreach (var pair in originals)
            {
                var current = pair.Key.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < current.Length; i++)
                    if (current[i] == original || clipped.TryGetValue(original, out var clip) && current[i] == clip ||
                        edited.TryGetValue(original, out var edit) && current[i] == edit)
                    { current[i] = replacement; changed = true; }
                if (changed) pair.Key.sharedMaterials = current;
            }
        }
        Material ClipMaterial(Material original)
        {
            if (clipped.TryGetValue(original, out var material)) return material;
            material = new Material(clipShader) { name = original.name + " (clipped)" };
            if (original.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", original.GetTexture("_BaseMap"));
            else if (original.HasProperty("_MainTex")) material.SetTexture("_BaseMap", original.GetTexture("_MainTex"));
            else if (original.HasProperty("baseColorTexture")) material.SetTexture("_BaseMap", original.GetTexture("baseColorTexture"));
            if (original.HasProperty("_BaseMap")) material.SetTextureScale("_BaseMap", original.GetTextureScale("_BaseMap"));
            if (original.HasProperty("_BaseMap")) material.SetTextureOffset("_BaseMap", original.GetTextureOffset("_BaseMap"));
            if (original.HasProperty("baseColorTexture_ST"))
            {
                var transform = original.GetVector("baseColorTexture_ST");
                material.SetTextureScale("_BaseMap", new Vector2(transform.x, transform.y));
                material.SetTextureOffset("_BaseMap", new Vector2(transform.z, transform.w));
            }
            if (original.HasProperty("_BumpMap")) material.SetTexture("_BumpMap", original.GetTexture("_BumpMap"));
            else if (original.HasProperty("normalTexture")) material.SetTexture("_BumpMap", original.GetTexture("normalTexture"));
            if (original.HasProperty("_BumpScale")) material.SetFloat("_BumpScale", original.GetFloat("_BumpScale"));
            else if (original.HasProperty("normalScale")) material.SetFloat("_BumpScale", original.GetFloat("normalScale"));
            if (original.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", original.GetColor("_EmissionColor"));
            else if (original.HasProperty("emissiveFactor")) material.SetColor("_EmissionColor", original.GetColor("emissiveFactor"));
            if (original.HasProperty("alphaCutoff")) material.SetFloat("_Cutoff", original.GetFloat("alphaCutoff"));
            if (original.HasProperty("_Cull")) material.SetFloat("_Cull", original.GetFloat("_Cull"));
            else if (original.HasProperty("_CullMode")) material.SetFloat("_Cull", original.GetFloat("_CullMode"));
            if (!overrides.ContainsKey(original))
            {
                var value = Values(original);
                material.SetColor("_BaseColor", value.color);
                material.SetFloat("_Metallic", value.metallic);
                material.SetFloat("_Roughness", value.roughness);
            }
            else ApplyValues(material, original);
            bool transparent = Values(original).color.a < .999f || original.renderQueue >= (int)RenderQueue.Transparent;
            material.SetFloat("_SrcBlend", (float)(transparent ? BlendMode.SrcAlpha : BlendMode.One));
            material.SetFloat("_DstBlend", (float)(transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            material.SetFloat("_ZWrite", transparent ? 0 : 1);
            material.renderQueue = transparent ? (int)RenderQueue.Transparent : (int)RenderQueue.Geometry;
            clipped.Add(original, material);
            return material;
        }
        public void SetClipping(bool value)
        {
            if (Clipping == value) return;
            Clipping = value;
            foreach (var pair in originals)
            {
                var current = new Material[pair.Value.Length];
                for (int i = 0; i < current.Length; i++)
                {
                    var original = pair.Value[i];
                    if (original == null) continue;
                    current[i] = Clipping || RequiresBlendShader(original) ? ClipMaterial(original) :
                        edited.TryGetValue(original, out var clone) ? clone : original;
                }
                pair.Key.sharedMaterials = current;
            }
            if (!Clipping) Shader.SetGlobalVector("_CarARClipPlane", Vector4.zero);
        }
        public void SetPlaneAxis(Vector3 normal) { HandClipping = false; PlaneNormal = normal.normalized; PlaneOffset = 0; }
        public void MovePlane(float delta) { HandClipping = false; PlaneOffset = Mathf.Clamp(PlaneOffset + delta, -10, 10); }
        public void SetHandClipping(bool value, Transform hand)
        {
            HandClipping = value && hand != null;
            clippingHand = HandClipping ? hand : null;
            if (HandClipping) SetClipping(true);
        }
        void LateUpdate()
        {
            if (!Clipping || modelRoot == null) return;
            Vector3 normal, point;
            if (HandClipping && clippingHand != null)
            {
                normal = clippingHand.forward.normalized;
                point = clippingHand.position;
            }
            else
            {
                normal = modelRoot.TransformDirection(PlaneNormal).normalized;
                var centre = modelRoot.TransformPoint(ModelBounds.Local(modelRoot).center);
                point = centre + normal * PlaneOffset * modelRoot.localScale.x;
            }
            Shader.SetGlobalVector("_CarARClipPlane", new Vector4(normal.x, normal.y, normal.z, Vector3.Dot(normal, point)));
        }
        void OnDestroy()
        {
            if (loader != null) loader.ModelChanged -= RefreshModel;
            Shader.SetGlobalVector("_CarARClipPlane", Vector4.zero);
        }
    }
}
