using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarAR.EditorTools
{
    // Appearance comes exclusively from the disposable Blender export. Missing or
    // invalid source data fails setup instead of silently substituting a preset.
    static class BlenderAppearanceImport
    {
        [Serializable] sealed class ModelData { public string source_sha256; public SourcePaint source_paint; public ViewMaterial[] view_materials; }
        [Serializable] sealed class SourcePaint
        {
            public float[] color_linear, principled_color_linear, coat_color_linear;
            public float principled_metallic, principled_roughness, principled_mix, coat_roughness, coat_ior;
            public string flake_roughness_texture, flake_normal_texture, coat_normal_texture;
        }
        [Serializable] sealed class ViewMaterial
        {
            public string name, response_texture, reflection_texture;
            public bool emission;
            public float[] color_linear;
            public float roughness;
        }
        [Serializable] sealed class EnvironmentData { public string source; public float strength; public EnvironmentSample[] samples; }
        [Serializable] sealed class EnvironmentSample { public float[] direction, radiance; public float solid_angle; }
        const string Settings = "Assets/CarAR/Settings/";

        static Color LinearColor(float[] values)
        {
            if (values == null || values.Length != 4 || Array.Exists(values, v => !float.IsFinite(v) || v < 0))
                throw new InvalidDataException("Invalid Blender source colour");
            return new Color(values[0], values[1], values[2], values[3]);
        }
        static Material MaterialAsset(string path, string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidDataException("Missing shader: " + shaderName);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            else
            {
                var clean = new Material(shader) { name = material.name };
                EditorUtility.CopySerialized(clean, material);
                UnityEngine.Object.DestroyImmediate(clean);
            }
            return material;
        }
        static Texture2D Texture(string path, bool normal = false, bool response = false)
        {
            if (string.IsNullOrEmpty(path)) return null;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new InvalidDataException("Missing Blender bake: " + path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = false; // These custom shader inputs contain linear data.
            importer.mipmapEnabled = !response;
            importer.wrapMode = response ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        public static void ApplyMaterials(Renderer[] renderers)
        {
            var data = JsonUtility.FromJson<ModelData>(File.ReadAllText("Assets/Models/AMG-interactive.json"));
            if (File.Exists("../AMG.blend"))
            {
                using var sha = System.Security.Cryptography.SHA256.Create();
                using var source = File.OpenRead("../AMG.blend");
                string hash = BitConverter.ToString(sha.ComputeHash(source)).Replace("-", "").ToLowerInvariant();
                if (hash != data.source_sha256) throw new InvalidDataException("AMG.blend changed. Run Tools/prepare_amg.py before setup/build.");
            }
            var p = data.source_paint ?? throw new InvalidDataException("Re-export AMG.blend: source paint metadata missing");
            var paint = MaterialAsset(Settings + "AMGCarPaint.mat", "CarAR/BlenderLayeredPaint");
            paint.SetVector("_BaseColor", LinearColor(p.color_linear));
            paint.SetVector("_PrincipledColor", LinearColor(p.principled_color_linear));
            paint.SetVector("_CoatColor", LinearColor(p.coat_color_linear));
            paint.SetFloat("_PrincipledMetallic", p.principled_metallic);
            paint.SetFloat("_PrincipledRoughness", p.principled_roughness);
            paint.SetFloat("_PrincipledMix", p.principled_mix);
            paint.SetFloat("_CoatRoughness", p.coat_roughness); paint.SetFloat("_CoatIOR", p.coat_ior);
            paint.SetTexture("_FlakeRoughness", Texture(p.flake_roughness_texture));
            paint.SetTexture("_FlakeNormal", Texture(p.flake_normal_texture, normal: true));
            paint.SetTexture("_CoatNormal", Texture(p.coat_normal_texture, normal: true));
            EditorUtility.SetDirty(paint);
            var replacements = new System.Collections.Generic.Dictionary<string, Material> { { "Black Carpaint", paint } };
            foreach (var view in data.view_materials)
            {
                var material = MaterialAsset(Settings + "Blender_" + view.name.Replace(' ', '_') + ".mat", "CarAR/BlenderViewSurface");
                material.SetVector("_BaseColor", LinearColor(view.color_linear));
                material.SetFloat("_Roughness", view.roughness);
                material.SetFloat("_EmissionOnly", view.emission ? 1 : 0);
                material.SetFloat("_ZWrite", view.emission ? 1 : 0);
                material.SetOverrideTag("RenderType", view.emission ? "Opaque" : "Transparent");
                material.SetTexture("_Response", Texture(view.response_texture, response: true));
                material.SetTexture("_BaseMap", Texture(view.reflection_texture));
                material.renderQueue = view.emission ? (int)RenderQueue.Geometry : (int)RenderQueue.Transparent;
                EditorUtility.SetDirty(material); replacements.Add(view.name, material);
            }
            int count = 0;
            foreach (var renderer in renderers)
            {
                var materials = renderer.sharedMaterials;
                bool opaque = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null) throw new InvalidDataException("Missing source material: " + renderer.name);
                    if (replacements.TryGetValue(materials[i].name, out var source)) { materials[i] = source; count++; }
                    opaque |= materials[i].renderQueue < (int)RenderQueue.Transparent;
                }
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = opaque ? ShadowCastingMode.On : ShadowCastingMode.Off;
                renderer.receiveShadows = true;
            }
            if (count == 0) throw new InvalidDataException("Blender source materials were not found on the imported model");
            Debug.Log($"[CarAR Source Materials] Authored layered paint: Principled mix={p.principled_mix}, roughness={p.principled_roughness}; {count} layered/view surfaces, remaining source Principled materials retained.");
        }
        public static float[] ApplyEnvironment()
        {
            var data = JsonUtility.FromJson<EnvironmentData>(File.ReadAllText("Assets/Environment/environment.json"));
            string path = "Assets/Environment/" + data.source;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureShape = TextureImporterShape.TextureCube;
            // The source is a 2:1 latitude/longitude panorama, not a mirror ball.
            importer.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
            var textureSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(textureSettings);
            textureSettings.cubemapConvolution = TextureImporterCubemapConvolution.Specular;
            textureSettings.seamlessCubemap = true;
            importer.SetTextureSettings(textureSettings);
            importer.sRGBTexture = false; importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            if (cube == null || cube.mipmapCount < 5) throw new InvalidDataException("HDR cubemap convolution failed");
            // Integrate the same panorama for diffuse lighting. Calibrate against a
            // unit white environment to avoid API-dependent SH normalization factors.
            SphericalHarmonicsL2 ambient = default, white = default;
            foreach (var sample in data.samples)
            {
                var d = new Vector3(sample.direction[0], sample.direction[1], sample.direction[2]);
                var c = new Color(sample.radiance[0], sample.radiance[1], sample.radiance[2]);
                ambient.AddDirectionalLight(d, c, sample.solid_angle);
                white.AddDirectionalLight(d, Color.white, sample.solid_angle);
            }
            var reference = new Color[1]; white.Evaluate(new[] { Vector3.up }, reference);
            if (!float.IsFinite(reference[0].r) || reference[0].r <= 0) throw new InvalidDataException("Invalid HDR SH integration");
            ambient *= data.strength / reference[0].r;
            var sky = MaterialAsset(Settings + "HDREnvironment.mat", "Skybox/Cubemap");
            sky.SetTexture("_Tex", cube); sky.SetFloat("_Exposure", data.strength); sky.SetFloat("_Rotation", 0);
            sky.SetColor("_Tint", Color.gray); EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = cube;
            RenderSettings.reflectionIntensity = data.strength;
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = ambient;
            Debug.Log($"[CarAR HDR] {data.source}: {cube.width}px faces, {cube.mipmapCount} specular mips, integrated diffuse SH, strength={data.strength}; camera background stays transparent.");
            var coefficients = new float[27];
            for (int c = 0; c < 3; c++) for (int i = 0; i < 9; i++) coefficients[c * 9 + i] = ambient[c, i];
            return coefficients;
        }
    }
}
