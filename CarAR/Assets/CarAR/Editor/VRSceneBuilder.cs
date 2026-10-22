using System;
using System.Collections.Generic;
using System.IO;
using MixedReality.Toolkit;
using MixedReality.Toolkit.SpatialManipulation;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;

namespace CarAR.EditorTools
{
    public static class VRSceneBuilder
    {
        const string Dir="Assets/Environment/VR/";
        const string Settings="Assets/CarAR/Settings/VR/";
        const string ScenePath="Assets/CarAR/Scenes/CarVR.unity";
        const string SceneVersion="vr-carroom-2-native-materials";
        [Serializable] sealed class Source
        {
            public string source,source_path,source_sha256,gltf_sha256,world_hdr,diffuse_hdr,view_transform,look;
            public float world_strength,exposure,lut_min,lut_max,sun_intensity;
            public int lut_size;
            public float[] car_position,car_rotation,sun_direction,sun_color,entry,entry_look,navigation_center,navigation_size;
            public Sample[] samples;
        }
        [Serializable] sealed class Sample {public float[] direction,radiance;public float solid_angle;}
        [Serializable] sealed class Materials {public Pbr[] materials;}
        [Serializable] sealed class LightingStamp
        {
            public string source_sha256,gltf_sha256,layout_sha256;
            public int atlas_count;
            public bool baked_diffuse_color;
        }
        [Serializable] sealed class Pbr
        {
            public string name,base_map,normal_map,metal_smooth_map,emission_map;
            public float[] base_color,emission,scale,offset;
            public float normal_scale,metallic,smoothness;
        }

        public static void BuildScene(bool prepareLighting=false)
        {
            if(!File.Exists(Dir+"CarRoom.glb") || !File.Exists(Dir+"carroom.json"))
                throw new FileNotFoundException("Export CarRoom-2 with Tools/Export-VRScene.ps1 before building VR.");
            var source=JsonUtility.FromJson<Source>(File.ReadAllText(Dir+"carroom.json"));
            using(var stream=File.OpenRead(Dir+"CarRoom.glb"))
            using(var sha=System.Security.Cryptography.SHA256.Create())
                if(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant()!=source.gltf_sha256)
                    throw new InvalidDataException("VR export is incomplete or stale. Run Tools/Export-VRScene.ps1 again.");
            if(File.Exists(source.source_path))
            {
                using var stream=File.OpenRead(source.source_path);using var sha=System.Security.Cryptography.SHA256.Create();
                if(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant()!=source.source_sha256)
                    throw new InvalidDataException("The VR .blend changed; export it again before building.");
            }
            string stamp=source.source_sha256+source.gltf_sha256+SceneVersion;
            if(File.Exists(ScenePath) && File.Exists(Settings+"source-stamp.txt") && File.ReadAllText(Settings+"source-stamp.txt")==stamp)
            {
                Debug.Log("[CarAR VR Build] Reusing validated static lighting and navigation");
                var cached=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
                CreateRoomMaterials();
                var cachedPipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(Settings+"Pipeline.asset");
                ConfigureSoftShadows(cachedPipeline);
                var cachedAgX=AssetDatabase.LoadAssetAtPath<Material>(Settings+"AgX.mat");
                cachedAgX.SetFloat("_ExposureEV",0);EditorUtility.SetDirty(cachedAgX);
                ConfigureOcclusion(AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Settings+"Renderer.asset"));
                foreach(var root in cached.GetRootGameObjects())
                {
                    var light=root.GetComponent<Light>();
                    if(light==null || light.type!=LightType.Directional)continue;
                    light.shadowBias=.3f;light.shadowNormalBias=.3f;
                }
                foreach(var root in cached.GetRootGameObjects())
                {
                    if(root==null)continue;
                    var review=root.GetComponent<VRShowroom>();
                    if(review==null)continue;
                    var leftover=review.car.GetComponent<ConstraintManager>();
                    if(leftover!=null){UnityEngine.Object.DestroyImmediate(leftover);EditorSceneManager.SaveScene(cached,ScenePath);}
                    review.origin.Camera.nearClipPlane=.02f;
                    if(review.buttons.Length!=16)
                    {
                        UnityEngine.Object.DestroyImmediate(review.menu.gameObject);CreateMenu(review);
                    }
                    // Navigation changes must also reach the lighting-cache path.
                    BakeNavigation(review,review.environment.gameObject,source);
                }
                if(!prepareLighting)AttachCyclesLighting(source);
                EditorSceneManager.SaveScene(cached,ScenePath);
                BuildLauncher();return;
            }
            Directory.CreateDirectory(Settings);AssetDatabase.Refresh();
            var mr=SceneManager.GetActiveScene();
            var vehicle=UnityEngine.Object.Instantiate(GameObject.Find("CarRoot") ?? FindInactiveRoot(mr,"CarRoot"));
            vehicle.name="AMG GT review (authored placement)";
            foreach(var component in vehicle.GetComponents<Component>())
                if(component is ObjectManipulator || component is MRTKCarInteraction || component is TransformConstraint)
                    UnityEngine.Object.DestroyImmediate(component);
            var constraintManager=vehicle.GetComponent<ConstraintManager>();
            if(constraintManager!=null)UnityEngine.Object.DestroyImmediate(constraintManager);
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            SceneManager.MoveGameObjectToScene(vehicle,scene);
            EditorSceneManager.CloseScene(mr,true);
            SceneManager.SetActiveScene(scene);
            vehicle.SetActive(true);
            vehicle.transform.SetPositionAndRotation(new Vector3(source.car_position[0],source.car_position[1],source.car_position[2]),
                new Quaternion(source.car_rotation[0],source.car_rotation[1],source.car_rotation[2],source.car_rotation[3]));
            foreach(var t in vehicle.GetComponentsInChildren<Transform>(true))t.gameObject.layer=VRShowroom.VehicleLayer;

            // Import geometry once. Generate UV2 only for architectural receivers;
            // dense props receive probes while still contributing to baked GI.
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Dir+"CarRoom.glb");
            if(prefab==null)throw new InvalidDataException("Cannot import VR environment");
            var environment=(GameObject)PrefabUtility.InstantiatePrefab(prefab);
            environment.name="CarRoom-2 authored room (static)";
            var materials=CreateRoomMaterials();
            foreach(var filter in environment.GetComponentsInChildren<MeshFilter>())
            {
                if(filter.sharedMesh==null)continue;
                var renderer=filter.GetComponent<MeshRenderer>();
                var assigned=renderer.sharedMaterials;
                for(int i=0;i<assigned.Length;i++)assigned[i]=materials[assigned[i].name];
                renderer.sharedMaterials=assigned;
                bool near=renderer.bounds.center.sqrMagnitude<120*120;
                bool lightmap=near && filter.sharedMesh.vertexCount<=160000;
                if(lightmap)
                {
                    // UV seams can push a mesh past the 16-bit index limit even
                    // when its original vertex count fits. Preserve indices while
                    // promoting that mesh before asking Unity to unwrap it again.
                    if(!AssetDatabase.IsValidFolder(Dir+"LightmapMeshes"))AssetDatabase.CreateFolder(Dir.TrimEnd('/'),"LightmapMeshes");
                    var path=Dir+"LightmapMeshes/"+source.gltf_sha256.Substring(0,12)+"_"+filter.name+".asset";
                    var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if(mesh==null)
                    {
                        mesh=UnityEngine.Object.Instantiate(filter.sharedMesh);
                        var indices=new int[mesh.subMeshCount][];
                        for(int i=0;i<indices.Length;i++)indices[i]=mesh.GetIndices(i);
                        mesh.indexFormat=IndexFormat.UInt32;mesh.subMeshCount=indices.Length;
                        for(int i=0;i<indices.Length;i++)mesh.SetIndices(indices[i],MeshTopology.Triangles,i);
                        Debug.Log("[CarAR VR Build] Lightmap UV: "+filter.name+" / "+mesh.vertexCount+" vertices");
                        if(!Unwrapping.GenerateSecondaryUVSet(mesh))throw new Exception("Cannot unwrap VR mesh "+filter.name);
                        SaveAsset(mesh,path);
                    }
                    filter.sharedMesh=mesh;
                }
                filter.gameObject.layer=VRShowroom.EnvironmentLayer;
                var collider=filter.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=filter.sharedMesh;
                var flags=StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccluderStatic|StaticEditorFlags.OccludeeStatic|StaticEditorFlags.ReflectionProbeStatic;
                if(near)flags|=StaticEditorFlags.ContributeGI;
                GameObjectUtility.SetStaticEditorFlags(filter.gameObject,flags);
                renderer.receiveGI=lightmap?ReceiveGI.Lightmaps:ReceiveGI.LightProbes;
                renderer.scaleInLightmap=lightmap ? (filter.name.Contains("stones") ? .5f : 1) : 0;
                renderer.shadowCastingMode=ShadowCastingMode.TwoSided;
                foreach(var material in renderer.sharedMaterials)
                {
                    if(material==null)continue;
                    material.doubleSidedGI=true;
                    material.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;
                    foreach(var property in material.GetTexturePropertyNames())
                    {
                        var texture=material.GetTexture(property);if(texture!=null){texture.anisoLevel=16;texture.mipMapBias=0;}
                    }
                }
            }
            ConfigurePipeline(source,out var pipeline);
            var coefficients=ConfigureEnvironment(source);
            Debug.Log("[CarAR VR Build] Source materials / HDR ready; building rig and navigation");
            var solar=new GameObject("Source HDR solar disc (dynamic shadows)",typeof(Light));
            var sun=solar.GetComponent<Light>();sun.type=LightType.Directional;
            sun.color=new Color(source.sun_color[0],source.sun_color[1],source.sun_color[2]);sun.intensity=source.sun_intensity;
            solar.transform.rotation=Quaternion.LookRotation(-new Vector3(source.sun_direction[0],source.sun_direction[1],source.sun_direction[2]));
            sun.lightmapBakeType=LightmapBakeType.Mixed;sun.shadows=LightShadows.Soft;sun.shadowBias=.3f;sun.shadowNormalBias=.3f;
            RenderSettings.sun=sun;
            var rig=new GameObject("VR XR Origin",typeof(XROrigin));
            var origin=rig.GetComponent<XROrigin>();
            var offset=new GameObject("Camera floor offset");offset.transform.SetParent(rig.transform,false);
            var cameraObject=new GameObject("VR Main Camera",typeof(Camera),typeof(AudioListener),typeof(TrackedPoseDriver));
            cameraObject.tag="MainCamera";cameraObject.transform.SetParent(offset.transform,false);
            var camera=cameraObject.GetComponent<Camera>();camera.nearClipPlane=.02f;camera.farClipPlane=120;
            camera.clearFlags=CameraClearFlags.Skybox;camera.allowHDR=true;camera.allowMSAA=true;
            cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=true;
            var driver=cameraObject.GetComponent<TrackedPoseDriver>();
            driver.positionInput=new InputActionProperty(new InputAction("VR Head position",InputActionType.Value,"<XRHMD>/centerEyePosition",expectedControlType:"Vector3"));
            driver.rotationInput=new InputActionProperty(new InputAction("VR Head rotation",InputActionType.Value,"<XRHMD>/centerEyeRotation",expectedControlType:"Quaternion"));
            driver.trackingStateInput=new InputActionProperty(new InputAction("VR Head tracking",InputActionType.Value,"<XRHMD>/trackingState",expectedControlType:"Integer"));
            driver.ignoreTrackingState=false;
            origin.Camera=camera;origin.CameraFloorOffsetObject=offset;origin.CameraYOffset=1.65f;
            origin.RequestedTrackingOriginMode=XROrigin.TrackingOriginMode.Floor;
            new GameObject("VR XRI interaction manager",typeof(XRInteractionManager));
            new GameObject("VR desktop event system",typeof(UnityEngine.EventSystems.EventSystem),typeof(InputSystemUIInputModule));
            var app=new GameObject("VR CarRoom experience").AddComponent<VRShowroom>();
            app.origin=origin;app.car=vehicle.transform;app.environment=environment.transform;
            app.explode=vehicle.GetComponent<ExplodeController>();
            app.panels=new[]{FindPanel(vehicle,"DoorLeft"),FindPanel(vehicle,"DoorRight"),FindPanel(vehicle,"RearHatch"),FindPanel(vehicle,"Hood")};
            app.pipeline=pipeline;app.ambientCoefficients=coefficients;
            app.entry=V(source.entry);app.entryLook=V(source.entry_look);
            app.viewpoints=new[]{new Vector3(-3.25f,0,2.5f),new Vector3(-2.8f,0,-2.6f),new Vector3(2.8f,0,-2.8f),new Vector3(3.2f,0,2.6f)};
            app.desktopFont=AssetDatabase.LoadAssetAtPath<Font>("Assets/CarAR/Fonts/SimHei.ttf");
            app.rayMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/CarAR/Settings/ControllerRay.mat");
            app.comfortMaterial=SaveMaterial(Settings+"Comfort.mat","CarAR/VRComfort");
            CreateMenu(app);
            Debug.Log("[CarAR VR Build] Building walkable navigation from source geometry");
            BakeNavigation(app,environment,source);
            Debug.Log("[CarAR VR Build] Navigation ready; preparing static GI");
            BakeLightProbes();
            var volume=new GameObject("VR highlight bloom",typeof(Volume)).GetComponent<Volume>();volume.isGlobal=true;
            var profile=ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom=profile.Add<Bloom>(true);bloom.intensity.Override(.045f);bloom.threshold.Override(1.8f);bloom.scatter.Override(.45f);
            SaveAsset(profile,Settings+"Volume.asset");volume.sharedProfile=profile;
            var lightSettings=new LightingSettings {name="CarRoom high quality static GI",bakedGI=true,realtimeGI=false,
                lightmapper=LightingSettings.Lightmapper.ProgressiveGPU,lightmapResolution=24,lightmapMaxSize=4096,
                directSampleCount=64,indirectSampleCount=512,environmentSampleCount=256,maxBounces=6,
                ao=true,aoMaxDistance=.3f,aoExponentIndirect=.6f,aoExponentDirect=0,
                mixedBakeMode=MixedLightingMode.Shadowmask};
            LightmapSettings.lightmapsMode=LightmapsMode.CombinedDirectional;
            SaveAsset(lightSettings,Settings+"Lighting.asset");Lightmapping.lightingSettings=lightSettings;
            RenderSettings.ambientMode=AmbientMode.Skybox;
            RenderSettings.ambientIntensity=1;
            EditorSceneManager.SaveScene(scene,ScenePath);
            Debug.Log("[CarAR VR Build] Baking source HDR / emissive indirect lighting");
            if(!Lightmapping.Bake())throw new Exception("VR lightmap bake failed");
            if(LightmapSettings.lightmaps.Length==0)throw new Exception("VR bake did not produce lightmaps");
            RenderSettings.ambientMode=AmbientMode.Custom;
            BakeReflection(new Vector3(0,1.2f,0),new Vector3(13,6,13),"Room");
            EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();
            File.WriteAllText(Settings+"source-stamp.txt",stamp);
            if(!prepareLighting){AttachCyclesLighting(source);EditorSceneManager.SaveScene(scene,ScenePath);}
            Debug.Log($"[CarAR VR Build] Scene saved: {LightmapSettings.lightmaps.Length} lightmaps; source={source.source}; view={source.view_transform}/{source.look}; exposure={source.exposure}");
            BuildLauncher();
        }
        static Vector3 V(float[] value)=>new Vector3(value[0],value[1],value[2]);
        static void AttachCyclesLighting(Source source)
        {
            var app=UnityEngine.Object.FindFirstObjectByType<VRShowroom>();
            const string rebuild="Run Tools/Bake-VRLighting.ps1 after exporting the source scene.";
            if(!File.Exists(Dir+"cycles-lighting.json") || !File.Exists(Dir+"interior-lighting.json"))
                throw new InvalidDataException("Missing source Cycles lighting. "+rebuild);
            var stamp=JsonUtility.FromJson<LightingStamp>(File.ReadAllText(Dir+"cycles-lighting.json"));
            var interiorStamp=JsonUtility.FromJson<LightingStamp>(File.ReadAllText(Dir+"interior-lighting.json"));
            if(!stamp.baked_diffuse_color || stamp.source_sha256!=source.source_sha256 || stamp.gltf_sha256!=source.gltf_sha256 ||
                stamp.layout_sha256!=VRBakeGeometry.LayoutHash(app) || interiorStamp.source_sha256!=source.source_sha256)
                throw new InvalidDataException("Source Cycles lighting does not match the scene / lightmap UV layout. "+rebuild);
            var maps=new List<Texture2D>();
            for(int i=0;i<stamp.atlas_count;i++)
            {
                string path=Dir+$"CyclesLightmap-{i}.exr";
                if(!File.Exists(path))throw new FileNotFoundException(rebuild,path);
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType=TextureImporterType.Default;importer.sRGBTexture=false;importer.mipmapEnabled=false;
                importer.maxTextureSize=4096;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
                maps.Add(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            }
            app.sourceLightmaps=maps.ToArray();app.ApplySourceLightmaps();
            string interior=Dir+"RoomInterior.exr";
            AssetDatabase.ImportAsset(interior,ImportAssetOptions.ForceSynchronousImport);
            var skyImporter=(TextureImporter)AssetImporter.GetAtPath(interior);
            skyImporter.textureShape=TextureImporterShape.TextureCube;skyImporter.generateCubemap=TextureImporterGenerateCubemap.Cylindrical;
            var settings=new TextureImporterSettings();skyImporter.ReadTextureSettings(settings);
            settings.cubemapConvolution=TextureImporterCubemapConvolution.Specular;settings.seamlessCubemap=true;skyImporter.SetTextureSettings(settings);
            skyImporter.sRGBTexture=false;skyImporter.mipmapEnabled=true;skyImporter.maxTextureSize=2048;
            skyImporter.textureCompression=TextureImporterCompression.Uncompressed;skyImporter.SaveAndReimport();
            var cube=AssetDatabase.LoadAssetAtPath<Cubemap>(interior);
            foreach(var probe in UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None))
            {probe.mode=ReflectionProbeMode.Custom;probe.customBakedTexture=cube;probe.intensity=1;}
            RenderSettings.customReflectionTexture=cube;RenderSettings.reflectionIntensity=1;
            var interiorLighting=JsonUtility.FromJson<Source>(File.ReadAllText(Dir+"interior-lighting.json"));
            SphericalHarmonicsL2 sh=default,white=default;
            foreach(var sample in interiorLighting.samples)
            {
                var d=V(sample.direction);sh.AddDirectionalLight(d,new Color(sample.radiance[0],sample.radiance[1],sample.radiance[2]),sample.solid_angle);
                white.AddDirectionalLight(d,Color.white,sample.solid_angle);
            }
            var unit=new Color[1];white.Evaluate(new[]{Vector3.up},unit);sh*=1/unit[0].r;
            app.ambientCoefficients=new float[27];for(int c=0;c<3;c++)for(int i=0;i<9;i++)app.ambientCoefficients[c*9+i]=sh[c,i];
            foreach(var renderer in app.car.GetComponentsInChildren<Renderer>())renderer.lightProbeUsage=LightProbeUsage.Off;
            ConfigureVehicleShadows(app,source);
            Debug.Log("[CarAR VR Build] Applied source Cycles diffuse atlases, interior SH and source HDR reflection");
        }
        static void ConfigureVehicleShadows(VRShowroom app,Source source)
        {
            // Source lamps are emissive mesh strips, not Blender Light objects.
            // Derive a dominant shadow direction from their upward/downward
            // emitting triangles. This is an area-light visibility approximation;
            // the original Cycles bake remains the source of scene illumination.
            Vector3 weighted=Vector3.zero;float total=0;
            foreach(var filter in app.environment.GetComponentsInChildren<MeshFilter>())
            {
                var renderer=filter.GetComponent<Renderer>();if(renderer==null)continue;
                var mesh=filter.sharedMesh;if(mesh==null)continue;
                var materials=renderer.sharedMaterials;var vertices=mesh.vertices;
                for(int sub=0;sub<mesh.subMeshCount && sub<materials.Length;sub++)
                {
                    var material=materials[sub];
                    if(material==null || !material.name.StartsWith("emission.",StringComparison.Ordinal) || !material.HasProperty("_BaseColor"))continue;
                    var radiance=material.GetColor("_BaseColor");
                    float luminance=.2126f*radiance.r+.7152f*radiance.g+.0722f*radiance.b;
                    var triangles=mesh.GetTriangles(sub);
                    for(int i=0;i<triangles.Length;i+=3)
                    {
                        var a=filter.transform.TransformPoint(vertices[triangles[i]]);
                        var b=filter.transform.TransformPoint(vertices[triangles[i+1]]);
                        var c=filter.transform.TransformPoint(vertices[triangles[i+2]]);
                        var centre=(a+b+c)/3;
                        if(centre.y<app.entry.y+2.2f)continue;
                        float weight=Mathf.Abs(Vector3.Cross(b-a,c-a).y)*.5f*luminance;
                        weighted+=centre*weight;total+=weight;
                    }
                }
                // Architectural occlusion is already in the bake. Realtime
                // shadow maps contain only animated vehicle geometry.
                renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=true;
            }
            var sun=RenderSettings.sun;
            if(sun==null)throw new InvalidDataException("VR dynamic shadow light is missing");
            if(source.sun_intensity<=0)
            {
                if(total<=0)throw new InvalidDataException("No source ceiling emitters found for vehicle shadows");
                var emitter=weighted/total;
                sun.transform.rotation=Quaternion.LookRotation(app.car.position+Vector3.up*.6f-emitter);
                sun.name="Source ceiling emission (dynamic vehicle visibility)";
                // Keep the light visible to URP shadow culling, with negligible
                // added radiance: source illumination is already baked/probed.
                sun.color=Color.white;sun.intensity=.001f;
                Debug.Log($"[CarAR VR Build] Vehicle shadow proxy from source emitters: {emitter:F3}; weight={total:F3}");
            }
            sun.lightmapBakeType=LightmapBakeType.Realtime;sun.shadows=LightShadows.Soft;
            sun.shadowStrength=1;sun.shadowBias=.02f;sun.shadowNormalBias=.03f;sun.shadowNearPlane=.05f;
            foreach(var renderer in app.car.GetComponentsInChildren<Renderer>())
            {renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;}
            var pipeline=app.pipeline;pipeline.renderScale=1;pipeline.mainLightShadowmapResolution=4096;
            EditorUtility.SetDirty(sun);EditorUtility.SetDirty(pipeline);
        }
        static Dictionary<string,Material> CreateRoomMaterials()
        {
            const string folder=Dir+"Textures/";
            var result=new Dictionary<string,Material>();
            var records=JsonUtility.FromJson<Materials>(File.ReadAllText(Dir+"materials.json"));
            Texture2D Load(string name,bool srgb,bool normal=false)
            {
                if(string.IsNullOrEmpty(name))return null;
                string path=folder+name;
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
                importer.sRGBTexture=srgb;importer.mipmapEnabled=true;importer.alphaSource=TextureImporterAlphaSource.FromInput;
                importer.maxTextureSize=4096;importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=16;
                importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.SaveAndReimport();
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            foreach(var p in records.materials)
            {
                var m=SaveMaterial(Settings+p.name.Replace('.','_')+".mat","Universal Render Pipeline/Lit");
                m.shaderKeywords=Array.Empty<string>();
                m.name=p.name;result.Add(p.name,m);
                bool emitter=p.emission[0]+p.emission[1]+p.emission[2]>0;
                m.shader=Shader.Find(emitter?"Universal Render Pipeline/Unlit":p.metallic==0?"CarAR/VRBakedRoom":"Universal Render Pipeline/Lit");
                if(emitter)
                {
                    // Illumination already comes from the source Cycles bake.
                    // An explicit unlit HDR surface keeps fixture radiance
                    // independent of stripped lighting variants in the player.
                    m.SetColor("_BaseColor",new Color(p.emission[0],p.emission[1],p.emission[2]));
                    m.SetTexture("_BaseMap",Load(p.emission_map,false));m.SetFloat("_Cull",0);
                    EditorUtility.SetDirty(m);continue;
                }
                // The center probe has no per-wall visibility. Avoid leaking
                // the exterior aperture's specular energy through the rocks;
                // wood and metal keep the local probe's view-dependent gloss.
                bool matteWall=p.name=="stone" || p.name=="wall.001" || p.name=="wall.side" || p.name=="floor_plaster";
                m.SetFloat("_EnvironmentReflections",matteWall?0:1);
                if(matteWall)m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                m.SetColor("_BaseColor",emitter?Color.black:new Color(p.base_color[0],p.base_color[1],p.base_color[2],p.base_color[3]));
                m.SetTexture("_BaseMap",Load(p.base_map,true));
                if(p.metallic==0)
                {
                    // Cycles evaluates geometry-dependent AO and the complete
                    // source color graph on the final UV2 surface. Do not apply
                    // the portable tile albedo a second time in URP.
                    m.SetColor("_BaseColor",Color.white);m.SetTexture("_BaseMap",null);
                }
                m.SetFloat("_Metallic",p.metallic);m.SetFloat("_Smoothness",p.smoothness);
                m.SetFloat("_Cull",0);m.doubleSidedGI=true;
                var packed=Load(p.metal_smooth_map,false);
                m.SetTexture("_MetallicGlossMap",packed);
                if(packed!=null){m.EnableKeyword("_METALLICSPECGLOSSMAP");m.SetFloat("_Smoothness",1);}
                var normal=Load(p.normal_map,false,true);m.SetTexture("_BumpMap",normal);m.SetFloat("_BumpScale",p.normal_scale);
                if(normal!=null)m.EnableKeyword("_NORMALMAP");
                m.SetTexture("_EmissionMap",Load(p.emission_map,false));
                m.SetColor("_EmissionColor",new Color(p.emission[0],p.emission[1],p.emission[2]));
                if(emitter)m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags=emitter?MaterialGlobalIlluminationFlags.BakedEmissive:MaterialGlobalIlluminationFlags.None;
                // glTF UVs use a top-left origin; glTFast's mesh conversion flips
                // V back to Unity, so reverse the KHR transform's V translation.
                m.SetTextureScale("_BaseMap",new Vector2(p.scale[0],p.scale[1]));
                m.SetTextureOffset("_BaseMap",new Vector2(p.offset[0],1-p.scale[1]-p.offset[1]));
                EditorUtility.SetDirty(m);
            }
            return result;
        }
        static GameObject FindInactiveRoot(Scene scene,string name)
        {
            foreach(var root in scene.GetRootGameObjects())if(root.name==name)return root;
            throw new InvalidDataException("Build the MR scene before generating VR");
        }
        static CarPanel FindPanel(GameObject root,string name)
        {
            foreach(var p in root.GetComponentsInChildren<CarPanel>(true))if(p.name==name)return p;
            throw new InvalidDataException("Missing articulated panel "+name);
        }
        static void ConfigurePipeline(Source source,out UniversalRenderPipelineAsset pipeline)
        {
            var renderer=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/CarAR/Settings/URP_Renderer.asset"));
            renderer.name="VR forward renderer";
            // Clone features as well: MR and VR never share mutable quality settings.
            var features=renderer.rendererFeatures.ToArray();renderer.rendererFeatures.Clear();
            SaveAsset(renderer,Settings+"Renderer.asset");
            foreach(var feature in features)
            {
                var copy=UnityEngine.Object.Instantiate(feature);AssetDatabase.AddObjectToAsset(copy,renderer);renderer.rendererFeatures.Add(copy);
            }
            var agx=ScriptableObject.CreateInstance<BlenderAgXFeature>();agx.name="Blender source AgX";
            AssetDatabase.AddObjectToAsset(agx,renderer);renderer.rendererFeatures.Add(agx);
            var lutImage=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
            if(!lutImage.LoadImage(File.ReadAllBytes(Dir+"AgX.png")))throw new InvalidDataException("Invalid Blender AgX LUT");
            var values=new Color[source.lut_size*source.lut_size*source.lut_size];
            for(int b=0;b<source.lut_size;b++)for(int g=0;g<source.lut_size;g++)for(int r=0;r<source.lut_size;r++)
                values[r+source.lut_size*(g+source.lut_size*b)]=lutImage.GetPixel(b*source.lut_size+r,g).linear;
            var lut=new Texture3D(source.lut_size,source.lut_size,source.lut_size,TextureFormat.RGBAHalf,false){name="Blender authored AgX",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear};
            lut.SetPixels(values);lut.Apply(false,true);SaveAsset(lut,Settings+"AgX.asset");UnityEngine.Object.DestroyImmediate(lutImage);
            agx.material=SaveMaterial(Settings+"AgX.mat","CarAR/BlenderAgX");
            agx.material.SetTexture("_Lut",lut);agx.material.SetVector("_Domain",new Vector4(source.lut_min,source.lut_max,source.lut_size,0));
            agx.material.SetFloat("_ExposureEV",0);
            ConfigureOcclusion(renderer);
            renderer.SetDirty();EditorUtility.SetDirty(renderer);EditorUtility.SetDirty(agx);
            pipeline=UniversalRenderPipelineAsset.Create(renderer);pipeline.name="VR HDR pipeline";
            pipeline.supportsHDR=true;pipeline.msaaSampleCount=4;pipeline.renderScale=1;
            ConfigureSoftShadows(pipeline);
            pipeline.mainLightShadowmapResolution=4096;pipeline.shadowDistance=35;pipeline.shadowCascadeCount=4;
            pipeline.supportsCameraDepthTexture=true;pipeline.supportsCameraOpaqueTexture=true;
            var serialized=new SerializedObject(pipeline);serialized.FindProperty("m_ReflectionProbeBlending").boolValue=true;
            serialized.FindProperty("m_ReflectionProbeBoxProjection").boolValue=true;
            serialized.FindProperty("m_ColorGradingMode").intValue=1;serialized.ApplyModifiedPropertiesWithoutUndo();
            SaveAsset(pipeline,Settings+"Pipeline.asset");QualitySettings.renderPipeline=pipeline;
        }
        static float[] ConfigureEnvironment(Source source)
        {
            string path=Dir+source.world_hdr;
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureShape=TextureImporterShape.TextureCube;importer.generateCubemap=TextureImporterGenerateCubemap.Cylindrical;
            var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);
            settings.cubemapConvolution=TextureImporterCubemapConvolution.Specular;settings.seamlessCubemap=true;importer.SetTextureSettings(settings);
            importer.sRGBTexture=false;importer.mipmapEnabled=true;importer.maxTextureSize=2048;
            importer.filterMode=FilterMode.Trilinear;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
            var cube=AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            var sky=SaveMaterial(Settings+"Sky.mat","Skybox/Cubemap");sky.SetTexture("_Tex",cube);sky.SetFloat("_Exposure",source.world_strength);sky.SetColor("_Tint",Color.gray);
            // The visual/reflection HDR retains the solar disc. GI uses a sky with
            // that energy removed; the directional light above carries it instead.
            string diffusePath=Dir+source.diffuse_hdr;
            AssetDatabase.ImportAsset(diffusePath,ImportAssetOptions.ForceSynchronousImport);
            var diffuseImporter=(TextureImporter)AssetImporter.GetAtPath(diffusePath);
            diffuseImporter.textureShape=TextureImporterShape.TextureCube;diffuseImporter.generateCubemap=TextureImporterGenerateCubemap.Cylindrical;
            diffuseImporter.sRGBTexture=false;diffuseImporter.mipmapEnabled=true;diffuseImporter.maxTextureSize=2048;
            diffuseImporter.textureCompression=TextureImporterCompression.Uncompressed;diffuseImporter.SaveAndReimport();
            sky.SetTexture("_Tex",AssetDatabase.LoadAssetAtPath<Cubemap>(diffusePath));
            RenderSettings.skybox=sky;RenderSettings.defaultReflectionMode=DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture=cube;RenderSettings.reflectionIntensity=source.world_strength;
            RenderSettings.ambientMode=AmbientMode.Custom;
            SphericalHarmonicsL2 sh=default,white=default;
            foreach(var sample in source.samples)
            {
                var d=new Vector3(sample.direction[0],sample.direction[1],sample.direction[2]);
                sh.AddDirectionalLight(d,new Color(sample.radiance[0],sample.radiance[1],sample.radiance[2]),sample.solid_angle);
                white.AddDirectionalLight(d,Color.white,sample.solid_angle);
            }
            var reference=new Color[1];white.Evaluate(new[]{Vector3.up},reference);sh*=source.world_strength/reference[0].r;
            RenderSettings.ambientProbe=sh;
            var result=new float[27];for(int c=0;c<3;c++)for(int i=0;i<9;i++)result[c*9+i]=sh[c,i];return result;
        }
        static void ConfigureOcclusion(UniversalRendererData renderer)
        {
            foreach(var feature in renderer.rendererFeatures)
            {
                if(feature.GetType().Name!="ScreenSpaceAmbientOcclusion")continue;
                var settings=new SerializedObject(feature);
                settings.FindProperty("m_Settings.Radius").floatValue=.3f;
                settings.FindProperty("m_Settings.Intensity").floatValue=.65f;
                settings.FindProperty("m_Settings.DirectLightingStrength").floatValue=.12f;
                settings.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(feature);
            }
            renderer.SetDirty();EditorUtility.SetDirty(renderer);
        }
        static void ConfigureSoftShadows(UniversalRenderPipelineAsset pipeline)
        {
            var settings=new SerializedObject(pipeline);
            settings.FindProperty("m_SoftShadowsSupported").boolValue=true;
            settings.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(pipeline);
        }
        static void CreateMenu(VRShowroom app)
        {
            var panel=new GameObject("VR review menu",typeof(RectTransform),typeof(Canvas));
            var canvas=panel.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=app.origin.Camera;
            var rect=panel.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(700,680);rect.localScale=Vector3.one*.001f;app.menu=rect;
            var bg=new GameObject("Background",typeof(RectTransform),typeof(Image));bg.transform.SetParent(rect,false);
            var br=bg.GetComponent<RectTransform>();br.anchorMin=Vector2.zero;br.anchorMax=Vector2.one;br.offsetMin=br.offsetMax=Vector2.zero;
            bg.GetComponent<Image>().color=new Color(.015f,.025f,.04f,.94f);bg.GetComponent<Image>().raycastTarget=false;
            var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/CarAR/Fonts/ShowroomChinese.asset");
            var text=new GameObject("VR instructions",typeof(RectTransform),typeof(TextMeshProUGUI));text.transform.SetParent(rect,false);
            text.GetComponent<RectTransform>().sizeDelta=new Vector2(670,180);text.GetComponent<RectTransform>().anchoredPosition=new Vector2(0,235);
            app.status=text.GetComponent<TMP_Text>();app.status.font=font;app.status.fontSize=24;app.status.alignment=TextAlignmentOptions.Center;app.status.raycastTarget=false;
            string[] labels={"移动：连续 + 传送","步速：正常","返回入口","车头视角","右侧视角","右后视角","车尾视角","左车门","右车门","后备厢尾门","发动机盖","整车爆炸","画质：精细","收起面板","进入驾驶位","录屏优化：关闭"};
            app.buttons=new StatefulInteractable[labels.Length];
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Packages/org.mixedrealitytoolkit.uxcomponents/Button/Prefabs/Action Button.prefab");
            for(int i=0;i<labels.Length;i++)
            {
                var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab);go.name=labels[i];go.transform.SetParent(rect,false);
                var r=go.GetComponent<RectTransform>();r.sizeDelta=new Vector2(205,62);r.anchoredPosition=new Vector2((i%3-1)*220,90-i/3*78);
                app.buttons[i]=go.GetComponent<StatefulInteractable>();app.buttons[i].UseGazeDwell=false;
                foreach(var label in go.GetComponentsInChildren<TMP_Text>(true))
                {
                    label.gameObject.SetActive(label.name=="Text");if(label.name!="Text")continue;
                    label.text=labels[i];label.font=font;label.fontSize=24;
                }
            }
        }
        static void BakeNavigation(VRShowroom app,GameObject environment,Source source)
        {
            var sources=new List<NavMeshBuildSource>();
            foreach(var filter in environment.GetComponentsInChildren<MeshFilter>())
                sources.Add(new NavMeshBuildSource {shape=NavMeshBuildSourceShape.Mesh,sourceObject=filter.sharedMesh,transform=filter.transform.localToWorldMatrix,area=0});
            // This is an inspection showroom: retain room/floor boundaries,
            // while allowing the visitor to pass through the car into its cabin.
            var settings=NavMesh.GetSettingsByID(0);settings.agentRadius=.25f;settings.agentHeight=1.8f;settings.agentClimb=.18f;settings.agentSlope=40;
            settings.overrideVoxelSize=true;settings.voxelSize=.08f;
            var data=NavMeshBuilder.BuildNavMeshData(settings,sources,new Bounds(V(source.navigation_center),V(source.navigation_size)),Vector3.zero,Quaternion.identity);
            if(data==null)throw new Exception("VR navigation bake failed");
            data.name="CarRoom walkable ground";SaveAsset(data,Dir+"Navigation.asset");app.navigation=data;
            var instance=NavMesh.AddNavMeshData(data);
            if(!NavMesh.SamplePosition(app.entry,out var start,.8f,NavMesh.AllAreas)){instance.Remove();throw new Exception("VR entry has no walkable ground");}
            app.entry=start.position;instance.Remove();
        }
        static void BakeLightProbes()
        {
            var probes=new GameObject("CarRoom vehicle and visitor light probes").AddComponent<LightProbeGroup>();
            var points=new List<Vector3>();
            for(float x=-4;x<=4;x+=1.5f)for(float z=-4;z<=4;z+=1.5f)foreach(float y in new[]{.25f,.9f,1.65f,2.7f})points.Add(new Vector3(x,y,z));
            probes.probePositions=points.ToArray();
        }
        static void BakeReflection(Vector3 position,Vector3 size,string name)
        {
            var go=new GameObject(name+" local HDR reflection");go.transform.position=position;
            var probe=go.AddComponent<ReflectionProbe>();probe.mode=ReflectionProbeMode.Baked;probe.hdr=true;
            probe.cullingMask=1<<VRShowroom.EnvironmentLayer;
            probe.resolution=1024;probe.size=size;probe.boxProjection=true;probe.blendDistance=1;probe.nearClipPlane=.1f;probe.farClipPlane=30;
            string path=Dir+name+"Reflection.exr";
            if(!Lightmapping.BakeReflectionProbe(probe,path))throw new Exception("Cannot bake "+name+" reflection");
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            // A manually baked texture needs an explicit reference in the scene;
            // bakedTexture is maintained separately by Unity's GI data store.
            probe.mode=ReflectionProbeMode.Custom;
            probe.customBakedTexture=AssetDatabase.LoadAssetAtPath<Texture>(path);
        }
        static void BuildLauncher()
        {
            QualitySettings.renderPipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/CarAR/Settings/URP.asset");
            // URP's build stripper discovers pipeline variants from configured
            // quality levels. Register VR as well as MR so HDR, local reflections
            // and this renderer's features survive player shader stripping.
            var quality=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            var levels=quality.FindProperty("m_QualitySettings");int vrIndex=-1;
            for(int i=0;i<levels.arraySize;i++)if(levels.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue.StartsWith("VR "))vrIndex=i;
            if(vrIndex<0){vrIndex=levels.arraySize;levels.arraySize++;}
            var vrLevel=levels.GetArrayElementAtIndex(vrIndex);vrLevel.FindPropertyRelative("name").stringValue="VR CarRoom";
            vrLevel.FindPropertyRelative("customRenderPipeline").objectReferenceValue=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(Settings+"Pipeline.asset");
            quality.ApplyModifiedPropertiesWithoutUndo();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            new GameObject("Choose MR or VR experience",typeof(XRLaunch));
            const string path="Assets/CarAR/Scenes/Launch.unity";EditorSceneManager.SaveScene(scene,path);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(path,true),new EditorBuildSettingsScene("Assets/CarAR/Scenes/CarAR.unity",true),new EditorBuildSettingsScene(ScenePath,true)};
        }
        static Material SaveMaterial(string path,string shader)
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(Shader.Find(shader));AssetDatabase.CreateAsset(material,path);}
            return material;
        }
        static void SaveAsset(UnityEngine.Object value,string path)
        {
            var previous=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if(previous!=null)AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(value,path);
        }
    }
}
