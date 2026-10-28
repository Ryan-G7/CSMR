using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using Unity.XR.CoreUtils;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using MixedReality.Toolkit;
using MixedReality.Toolkit.Editor;
using MixedReality.Toolkit.SpatialManipulation;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.UI;
using TMPro;

namespace CarAR.EditorTools
{
    public static class CarARSetupWizard
    {
        const string SettingsDir = "Assets/CarAR/Settings";
        const string PrefabDir = "Assets/CarAR/Prefabs";
        const string SceneDir = "Assets/CarAR/Scenes";

        [MenuItem("CarAR/Setup Project and Scene")]
        public static void Setup()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            try
            {
                ConfigurePlayer();
                ConfigureUrp();
                AssignOpenXRLoader();
                EnableHandTrackingFeatures();
                BuildScene();
                AssetDatabase.SaveAssets();
                EditorUtility.DisplayDialog("CarAR",
                    "Setup complete.\n\nScene: Assets/CarAR/Scenes/CarAR.unity\nBuild target: Windows / OpenXR\n\nUse CarARSetupWizard.BuildPreview to build the desktop and HJXR player.",
                    "OK");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("CarAR", "Setup failed, see Console.\n" + e.Message, "OK");
            }
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "CarAR";
            PlayerSettings.productName = "CarAR AMG GT";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.preserveFramebufferAlpha = true;
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64 &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                throw new Exception("Cannot activate the Windows build target");
            PlayerSettings.runInBackground = true;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets != null && assets.Length > 0)
            {
                var so = new SerializedObject(assets[0]);
                var prop = so.FindProperty("activeInputHandler");
                if (prop != null)
                {
                    prop.intValue = 1; // Input System (new)
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        static void ConfigureUrp()
        {
            EnsureFolder(SettingsDir);
            const string rendererPath = SettingsDir + "/URP_Renderer.asset";
            const string urpPath = SettingsDir + "/URP.asset";

            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, rendererPath);
            }

            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(urpPath);
            if (urp == null)
            {
                urp = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(urp, urpPath);
            }

            // The vendor compositor needs transparent pixels around the car. R11G11B10 HDR
            // intermediates cannot carry alpha, and an opaque final blit hides the VST layer.
            urp.supportsHDR = false;
            urp.msaaSampleCount = 4;
            urp.renderScale = 1f;
            urp.mainLightShadowmapResolution = 4096;
            urp.shadowDistance = 15f;
            urp.shadowCascadeCount = 4;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            QualitySettings.lodBias = 2f;
            var urpSettings = new SerializedObject(urp);
            urpSettings.FindProperty("m_SoftShadowsSupported").boolValue = true;
            urpSettings.FindProperty("m_SoftShadowQuality").intValue = 3;
            urpSettings.FindProperty("m_ReflectionProbeBlending").boolValue = true;
            urpSettings.FindProperty("m_AllowPostProcessAlphaOutput").boolValue = true;
            urpSettings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);
            // Use URP's maintained SSAO implementation before opaques, preserving MR alpha.
            var occlusion = rendererData.rendererFeatures.Find(feature => feature is ScreenSpaceAmbientOcclusion);
            if (occlusion == null)
            {
                occlusion = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                occlusion.name = "Vehicle ambient occlusion";
                AssetDatabase.AddObjectToAsset(occlusion, rendererData);
                rendererData.rendererFeatures.Add(occlusion);
            }
            var ao = new SerializedObject(occlusion);
            ao.FindProperty("m_Settings.AfterOpaque").boolValue = false;
            ao.FindProperty("m_Settings.Downsample").boolValue = true;
            ao.FindProperty("m_Settings.Source").intValue = 0;
            ao.FindProperty("m_Settings.Intensity").floatValue = .7f;
            ao.FindProperty("m_Settings.Radius").floatValue = .15f;
            ao.FindProperty("m_Settings.DirectLightingStrength").floatValue = .08f;
            ao.ApplyModifiedPropertiesWithoutUndo();
            occlusion.SetActive(true); rendererData.SetDirty();
            EditorUtility.SetDirty(rendererData);
            GraphicsSettings.defaultRenderPipeline = urp;
            QualitySettings.renderPipeline = urp;
        }

        static void AssignOpenXRLoader()
        {
            const string settingsKey = "UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget";
            if (!EditorBuildSettings.TryGetConfigObject(settingsKey, out XRGeneralSettingsPerBuildTarget perBuildTarget) || perBuildTarget == null)
            {
                EnsureFolder("Assets/XR");
                perBuildTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perBuildTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(settingsKey, perBuildTarget, true);
            }

            var androidSettings = perBuildTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
            if (androidSettings == null)
            {
                androidSettings = ScriptableObject.CreateInstance<XRGeneralSettings>();
                androidSettings.name = "Android Settings";
                AssetDatabase.AddObjectToAsset(androidSettings, perBuildTarget);
                perBuildTarget.SetSettingsForBuildTarget(BuildTargetGroup.Android, androidSettings);
            }

            var manager = androidSettings.AssignedSettings;
            if (manager == null)
            {
                manager = ScriptableObject.CreateInstance<XRManagerSettings>();
                AssetDatabase.CreateAsset(manager, "Assets/XR/XRManagerSettings_Android.asset");
                androidSettings.AssignedSettings = manager;
            }
            androidSettings.InitManagerOnStart = true;
            var desktop = perBuildTarget.SettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (desktop == null)
            {
                desktop = ScriptableObject.CreateInstance<XRGeneralSettings>();
                desktop.name = "Standalone Settings";
                AssetDatabase.AddObjectToAsset(desktop, perBuildTarget);
                perBuildTarget.SetSettingsForBuildTarget(BuildTargetGroup.Standalone, desktop);
            }
            if (desktop.AssignedSettings == null)
            {
                var dm = ScriptableObject.CreateInstance<XRManagerSettings>();
                AssetDatabase.CreateAsset(dm, "Assets/XR/XRManagerSettings_Standalone.asset");
                desktop.AssignedSettings = dm;
            }
            desktop.InitManagerOnStart = false; // Explicit --xr launches the headset; ordinary launches remain a preview.
            // HJXR retains its marker singleton after Unity destroys the temporary PreInit
            // instance, leaving it with a freed camera configuration (zero image dimensions).
            // Unity supplies this loader for runtimes that cannot survive PreInit teardown.
            EnsureFolder("Assets/XR/Loaders");
            const string desktopLoaderPath = "Assets/XR/Loaders/OpenXRLoaderNoPreInit.asset";
            var desktopLoader = AssetDatabase.LoadAssetAtPath<OpenXRLoaderNoPreInit>(desktopLoaderPath);
            if (desktopLoader == null)
            {
                desktopLoader = ScriptableObject.CreateInstance<OpenXRLoaderNoPreInit>();
                AssetDatabase.CreateAsset(desktopLoader, desktopLoaderPath);
            }
            if (!desktop.AssignedSettings.TrySetLoaders(new System.Collections.Generic.List<XRLoader> { desktopLoader }))
                throw new Exception("Cannot assign Unity OpenXR loader without PreInit");
            // General settings belong inside the per-target container. Separate
            // main assets trigger XR Management's legacy upgrader on reimport.
            EditorUtility.SetDirty(desktop); EditorUtility.SetDirty(desktop.AssignedSettings);
            EditorUtility.SetDirty(androidSettings); EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(perBuildTarget); AssetDatabase.SaveAssets();
            UnityEditor.XR.OpenXR.Features.FeatureHelpers.RefreshFeatures(BuildTargetGroup.Standalone);
            var ds = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            var dh = ds != null ? ds.GetFeature<UnityEngine.XR.Hands.OpenXR.HandTracking>() : null;
            if (dh != null) dh.enabled = true;
            // Pimax normally exposes its controllers through Touch, Index or Vive compatibility.
            if (ds != null)
            {
                foreach (var feature in ds.GetFeatures<UnityEngine.XR.OpenXR.Features.OpenXRInteractionFeature>())
                    feature.enabled = feature is UnityEngine.XR.OpenXR.Features.Interactions.OculusTouchControllerProfile ||
                        feature is UnityEngine.XR.OpenXR.Features.Interactions.ValveIndexControllerProfile ||
                        feature is UnityEngine.XR.OpenXR.Features.Interactions.HTCViveControllerProfile ||
                        feature is UnityEngine.XR.OpenXR.Features.Interactions.KHRSimpleControllerProfile ||
                        feature is UnityEngine.XR.OpenXR.Features.Interactions.MicrosoftMotionControllerProfile;
            }
            EditorUtility.SetDirty(desktop); EditorUtility.SetDirty(desktop.AssignedSettings);
            if (ds != null) EditorUtility.SetDirty(ds);

            XRPackageMetadataStore.AssignLoader(manager, "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Android);

            EditorUtility.SetDirty(perBuildTarget);
            EditorUtility.SetDirty(androidSettings);
            EditorUtility.SetDirty(manager);
        }

        static void EnableHandTrackingFeatures()
        {
            UnityEditor.XR.OpenXR.Features.FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (settings == null)
            {
                Debug.LogWarning("[CarAR] OpenXRSettings instance not found; enable Hand Tracking manually in Project Settings > XR Plug-in Management > OpenXR.");
                return;
            }
            var hands = settings.GetFeature<UnityEngine.XR.Hands.OpenXR.HandTracking>();
            if (hands != null) hands.enabled = true;
            else Debug.LogWarning("[CarAR] Hand tracking feature unavailable; check XR Hands installation.");
            EditorUtility.SetDirty(settings);
        }

        static void BuildScene()
        {
            EnsureFolder(PrefabDir);
            EnsureFolder(SceneDir);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            new GameObject("MR XRI interaction manager", typeof(XRInteractionManager));
            new GameObject("AR Session", typeof(ARSession), typeof(ARInputManager));

            ConfigureMRTKProfile();
            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/org.mixedrealitytoolkit.input/Assets/Prefabs/MRTK XR Rig.prefab");
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
            // The vendor compositor needs transparent alpha and an explicit floor reference.
            // MRTK's display classification sees this VST headset as opaque and would overwrite both.
            foreach (var manager in rig.GetComponentsInChildren<CameraSettingsManager>(true)) manager.enabled = false;
            foreach (var mode in rig.GetComponentsInChildren<MixedReality.Toolkit.Input.UnboundedTrackingMode>(true)) mode.enabled = false;
            // Replace the prefab's hand/gaze interaction objects with physical XRI controllers.
            foreach (var modes in rig.GetComponentsInChildren<MixedReality.Toolkit.Input.InteractionModeManager>(true)) modes.enabled = false;
            foreach (var controller in rig.GetComponentsInChildren<XRBaseController>(true)) controller.gameObject.SetActive(false);
            foreach (var interactor in rig.GetComponentsInChildren<XRBaseInteractor>(true)) interactor.enabled = false;
            var origin = rig.GetComponentInChildren<XROrigin>();
            var originGo = origin.gameObject;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

            var offsetGo = origin.CameraFloorOffsetObject;
            var cam = origin.Camera;
            var camGo = cam.gameObject;
            camGo.AddComponent<ARCameraManager>();
            camGo.AddComponent<ARCameraBackground>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.clear;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 80f;

            origin.CameraFloorOffsetObject = offsetGo;
            origin.Camera = cam;

            originGo.AddComponent<AREnvironmentProbeManager>().automaticPlacementRequested = true;
            var planeManager = originGo.AddComponent<ARPlaneManager>();
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
            planeManager.planePrefab = CreatePlanePrefab();

            var sunGo = new GameObject("Optional direct fill (off by default)", typeof(Light));
            var sun = sunGo.GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 0;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = .5f;
            sun.shadowBias = .015f;
            sun.shadowNormalBias = .02f;
            sun.shadowNearPlane = .05f;
            sunGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.sun = sun;
            var environmentCoefficients = BlenderAppearanceImport.ApplyEnvironment();

            var carRoot = new GameObject("CarRoot");
            carRoot.transform.localScale = Vector3.one;

            const string modelPath = "Assets/Models/AMG-interactive.glb";
            AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceUpdate);
            var glb = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (glb == null) throw new Exception(modelPath + " not found. Export AMG.blend with Tools/prepare_amg.py first.");
            var car = (GameObject)PrefabUtility.InstantiatePrefab(glb);
            car.name = "AMG GT";
            car.transform.SetParent(carRoot.transform, false);

            // Put the root pivot on the wheel contact plane so scaling preserves floor contact.
            var modelRenderers = car.GetComponentsInChildren<Renderer>();
            BlenderAppearanceImport.ApplyMaterials(modelRenderers);
            var bounds = modelRenderers[0].bounds;
            foreach (var r in modelRenderers) bounds.Encapsulate(r.bounds);
            car.transform.position += Vector3.up * (carRoot.transform.position.y - bounds.min.y);
            Debug.Log($"[CarAR Model] Default GLB size={bounds.size:F3}m");
            foreach (var filter in car.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                var collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
            }
            var explode = carRoot.AddComponent<ExplodeController>();
            var manipulator = carRoot.AddComponent<ObjectManipulator>();
            manipulator.colliders.Clear();
            foreach (var collider in car.GetComponentsInChildren<Collider>()) manipulator.colliders.Add(collider);
            manipulator.selectMode = InteractableSelectMode.Multiple;
            manipulator.interactionLayers = MRTKControllerInput.VehicleLayer;
            manipulator.RotationAnchorFar = ObjectManipulator.RotateAnchorType.RotateAboutObjectCenter;
            manipulator.ReleaseBehavior = ObjectManipulator.ReleaseBehaviorType.None;
            var vehicleLogic = manipulator.ManipulationLogicTypes;
            vehicleLogic.scaleLogicType = typeof(VehicleScaleLogic);
            manipulator.ManipulationLogicTypes = vehicleLogic;
            var moveConstraint = carRoot.AddComponent<MoveAxisConstraint>();
            moveConstraint.ConstraintOnMovement = AxisFlags.YAxis;
            var rotateConstraint = carRoot.AddComponent<RotationAxisConstraint>();
            rotateConstraint.ConstraintOnRotation = AxisFlags.XAxis | AxisFlags.ZAxis;
            var scaleConstraint = carRoot.AddComponent<MinMaxScaleConstraint>();
            scaleConstraint.RelativeToInitialState = false;
            scaleConstraint.MinimumScale = Vector3.one * MRTKCarInteraction.MinimumScale;
            scaleConstraint.MaximumScale = Vector3.one * MRTKCarInteraction.MaximumScale;
            manipulator.enabled = false;
            var gesture = carRoot.AddComponent<MRTKCarInteraction>();
            gesture.manipulator = manipulator;

            var placement = originGo.AddComponent<CarPlacementManager>();
            SetField(placement, "planeManager", planeManager);
            SetField(placement, "carRoot", carRoot.transform);

            var lightEst = originGo.AddComponent<ARLightEstimation>();
            SetField(lightEst, "cameraManager", camGo.GetComponent<ARCameraManager>());
            SetField(lightEst, "sun", sun);
            SetField(lightEst, "environmentCoefficients", environmentCoefficients);

            var showroom = new GameObject("Showroom").AddComponent<CarShowroom>();
            showroom.car = carRoot.transform; showroom.placement = placement; showroom.lighting = lightEst;
            showroom.gestures = gesture; showroom.explode = explode;
            var presentation = showroom.gameObject.AddComponent<MRPresentation>();
            presentation.showroom = showroom;
            presentation.ambientOcclusion = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(SettingsDir + "/URP_Renderer.asset").rendererFeatures.Find(feature => feature is ScreenSpaceAmbientOcclusion);
            presentation.shadowMaterial = new Material(Shader.Find("CarAR/ShadowCatcher"));
            presentation.contactMaterial = new Material(Shader.Find("CarAR/ContactShadow"));
            AssetDatabase.CreateAsset(presentation.shadowMaterial, SettingsDir + "/ShadowCatcher.mat");
            AssetDatabase.CreateAsset(presentation.contactMaterial, SettingsDir + "/ContactShadow.mat");
            CreateMRTKControls(showroom);
            var keyboard = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Packages/org.mixedrealitytoolkit.uxcomponents/Experimental/NonNativeKeyboard/NonNativeKeyboard.prefab"));
            keyboard.name = "Review spatial keyboard";
            PrefabUtility.UnpackPrefabInstance(keyboard, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var image in keyboard.GetComponentsInChildren<Image>(true))
            {
                var serialized = new SerializedObject(image);
                serialized.FindProperty("m_Sprite").objectReferenceValue = null;
                serialized.FindProperty("m_Sprite").objectReferenceEntityIdValue = default;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(image);
            }
            var controllerInput = showroom.gameObject.AddComponent<MRTKControllerInput>();
            controllerInput.ui = showroom.GetComponent<MRTKShowroomUI>();
            controllerInput.ui.controllers = controllerInput;
            controllerInput.trackingOrigin = offsetGo.transform;
            const string rayPath = SettingsDir + "/ControllerRay.mat";
            var rayMaterial = AssetDatabase.LoadAssetAtPath<Material>(rayPath);
            if (rayMaterial == null)
            {
                rayMaterial = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                rayMaterial.SetColor("_BaseColor", Color.white);
                AssetDatabase.CreateAsset(rayMaterial, rayPath);
            }
            rayMaterial.SetColor("_BaseColor", Color.white);
            rayMaterial.SetFloat("_Cull", (float)CullMode.Off);
            rayMaterial.SetFloat("_CameraFadingEnabled", 0);
            rayMaterial.SetFloat("_SoftParticlesEnabled", 0);
            EditorUtility.SetDirty(rayMaterial);
            controllerInput.rayMaterial = rayMaterial;
            var diagnostics = showroom.gameObject.AddComponent<XRReviewDiagnostics>();
            diagnostics.showroom = showroom; diagnostics.origin = origin;
            var vendor = showroom.gameObject.AddComponent<Huijing.HuijingMRBridge>();
            vendor.placement = placement;
            carRoot.SetActive(false);

            const string scenePath = SceneDir + "/CarAR.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
            Debug.Log($"[CarAR] Scene saved to {scenePath}");
        }

        static void ConfigureMRTKProfile()
        {
            EnsureFolder("Assets/MRTK.Generated");
            const string path = "Assets/MRTK.Generated/CarARProfile.asset";
            var profile = AssetDatabase.LoadAssetAtPath<MRTKProfile>(path);
            if (profile == null)
            {
                AssetDatabase.CopyAsset("Packages/org.mixedrealitytoolkit.core/Configuration/Default Profiles/MRTKProfile.asset", path);
                profile = AssetDatabase.LoadAssetAtPath<MRTKProfile>(path);
            }
            var settings = AssetDatabase.LoadAssetAtPath<MRTKSettings>("Assets/MRTK.Generated/MRTKSettings.asset");
            profile.LoadedSubsystems.Clear();
            profile.LoadedSubsystems.Add(new SystemType(typeof(MixedReality.Toolkit.Input.UnityHandsSubsystem)));
            profile.LoadedSubsystems.Add(new SystemType(typeof(MixedReality.Toolkit.Input.SyntheticHandsSubsystem)));
            profile.LoadedSubsystems.Add(new SystemType(typeof(MixedReality.Toolkit.Input.MRTKHandsAggregatorSubsystem)));
            EditorUtility.SetDirty(profile);
            if (settings == null) { settings = ScriptableObject.CreateInstance<MRTKSettings>(); AssetDatabase.CreateAsset(settings, "Assets/MRTK.Generated/MRTKSettings.asset"); }
            settings.SetProfileForBuildTarget(BuildTargetGroup.Standalone, profile);
            EditorUtility.SetDirty(settings);
        }

        static void CreateMRTKControls(CarShowroom showroom)
        {
            if (AssetDatabase.LoadAssetAtPath<TMP_Settings>("Assets/TextMesh Pro/Resources/TMP Settings.asset") == null)
            {
                var ugui = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
                AssetDatabase.ImportPackage(System.IO.Path.Combine(ugui.resolvedPath, "Package Resources/TMP Essential Resources.unitypackage"), false);
                AssetDatabase.Refresh();
            }
            var controls = showroom.gameObject.AddComponent<MRTKShowroomUI>();
            controls.showroom = showroom;
            var font = CreateShowroomFont();
            var panel = new GameObject("MRTK Showroom Controls", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = panel.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            panel.AddComponent<TrackedDeviceGraphicRaycaster>();
            var rect = panel.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(700, 610);
            var background = new GameObject("Panel background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(rect, false);
            var backRect = background.GetComponent<RectTransform>(); backRect.anchorMin = Vector2.zero; backRect.anchorMax = Vector2.one;
            backRect.offsetMin = Vector2.zero; backRect.offsetMax = Vector2.zero;
            var image = background.GetComponent<Image>(); image.color = new Color(.025f, .045f, .06f, .94f); image.raycastTarget = false;
            rect.localScale = Vector3.one * .001f;
            controls.controlPanel = rect;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/org.mixedrealitytoolkit.uxcomponents/Button/Prefabs/Action Button.prefab");
            string[] labels = { "导入 GLB", "开启剖切", "上一材质", "模型爆炸", "解除锁定", "选择停放位置", "向左 10cm", "向右 10cm", "远离 10cm", "靠近 10cm", "缩小 10%", "放大 10%", "左转 5°", "右转 5°", "地面 +1cm", "地面 -1cm", "1:1 尺寸", "1:4 展示", "补光调亮", "补光调暗", "色温调暖", "色温调冷", "光线方向", "HDR / 自动光照", "切换流畅画质", "精细调整", "收起面板", "保存快照", "下一材质", "切换颜色", "金属度 +", "金属度 -", "粗糙度 +", "粗糙度 -", "透明度 +", "透明度 -", "添加标注", "撤销标注", "剖面 X", "剖面 Y", "剖面 Z", "剖面 -10cm", "剖面 +10cm", "材质调整" };
            controls.buttons = new StatefulInteractable[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                var button = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                button.name = labels[i]; button.transform.SetParent(rect, false);
                var r = button.GetComponent<RectTransform>();
                r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f);
                r.sizeDelta = new Vector2(205, 62); r.anchoredPosition = new Vector2((i % 3 - 1) * 220, 120 - i / 3 * 78);
                controls.buttons[i] = button.GetComponent<StatefulInteractable>();
                controls.buttons[i].UseGazeDwell = false;
                foreach (var label in button.GetComponentsInChildren<TMP_Text>(true))
                {
                    label.font = font; label.fontSharedMaterial = font.material;
                    if (label.gameObject.name == "Text")
                    {
                        label.gameObject.SetActive(true);
                        label.text = labels[i]; label.fontSize = 24; label.font = font;
                    }
                    else label.gameObject.SetActive(false);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(label);
                }
            }
            var textGo = new GameObject("Instructions and provider status", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(rect, false);
            var textRect = textGo.GetComponent<RectTransform>(); textRect.sizeDelta = new Vector2(680, 165); textRect.anchoredPosition = new Vector2(0, 215);
            controls.status = textGo.GetComponent<TMP_Text>(); controls.status.fontSize = 21;
            controls.status.font = font;
            controls.status.alignment = TextAlignmentOptions.Center;
            controls.status.raycastTarget = false;
            foreach (var child in panel.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 5;
        }

        static TMP_FontAsset CreateShowroomFont()
        {
            const string path = "Assets/CarAR/Fonts/ShowroomChinese.asset";
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font != null) return font;
            var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/CarAR/Fonts/SimHei.ttf");
            if (source == null) throw new Exception("Chinese font asset is missing");
            font = TMP_FontAsset.CreateFontAsset(source, 64, 6, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 2048, 2048);
            AssetDatabase.CreateAsset(font, path);
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var texture in font.atlasTextures) AssetDatabase.AddObjectToAsset(texture, font);
            EditorUtility.SetDirty(font);
            return font;
        }

        // Headless entry point: generate a reproducible scene, validate it and build a runnable desktop preview.
        public static void PrepareVRLighting()
        {
            ConfigurePlayer();ConfigureUrp();AssignOpenXRLoader();EnableHandTrackingFeatures();BuildScene();
            VRSceneBuilder.BuildScene(true);AssetDatabase.SaveAssets();
        }

        public static void BuildPreview()=>BuildPlayer(false);
        public static void BuildPresentation()=>BuildPlayer(true);

        static void BuildPlayer(bool presentation)
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            const BuildTarget target=BuildTarget.StandaloneWindows64;
            var apis=PlayerSettings.GetGraphicsAPIs(target);
            bool automaticApis=PlayerSettings.GetUseDefaultGraphicsAPIs(target);
            string output=presentation?"Build/Presentation":"Build/Windows";
            int exitCode=1;
            try
            {
                ConfigurePlayer();
                PlayerSettings.SetUseDefaultGraphicsAPIs(target,false);
                PlayerSettings.SetGraphicsAPIs(target,new[]{GraphicsDeviceType.Direct3D11});
                HuijingInteropValidation.Validate();
                ConfigureUrp(); AssignOpenXRLoader(); EnableHandTrackingFeatures(); BuildScene();
                VRSceneBuilder.BuildScene();
                AssetDatabase.SaveAssets();
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = System.Array.ConvertAll(EditorBuildSettings.scenes, scene => scene.path),
                    locationPathName = output+"/CarAR.exe",
                    target = target, options = presentation?BuildOptions.None:BuildOptions.Development
                });
                if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                    throw new Exception("Player build failed: " + report.summary.result);
                string boot = System.IO.File.ReadAllText(output+"/CarAR_Data/boot.config");
                if (boot.Contains("xrsdk-pre-init-library=UnityOpenXR"))
                    throw new Exception("HJXR build must use Unity's OpenXR loader without PreInit");
                Debug.Log("[HJXR Validation] PASS: built player does not bootstrap a temporary OpenXR instance");
                Debug.Log(presentation?"[CarAR Presentation] RELEASE BUILD PASS":"[CarAR] PREVIEW BUILD PASSED");
                System.IO.File.WriteAllText(output+"/carar-capabilities.json", "{\"vr\":true,\"mr\":true,\"scene\":\"CarVR\",\"version\":1}");
                exitCode=0;
            }
            catch (Exception e) { Debug.LogException(e); }
            finally
            {
                PlayerSettings.SetGraphicsAPIs(target,apis);
                PlayerSettings.SetUseDefaultGraphicsAPIs(target,automaticApis);
                AssetDatabase.SaveAssets();
            }
            if(Application.isBatchMode)EditorApplication.Exit(exitCode);
        }

        static GameObject CreatePlanePrefab()
        {
            const string matPath = PrefabDir + "/PlaneVisual.mat";
            const string prefabPath = PrefabDir + "/ARPlaneVisual.prefab";

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null) return existing;

            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetColor("_BaseColor", new Color(0.1f, 0.9f, 0.6f, 0.3f));
                mat.renderQueue = (int)RenderQueue.Transparent;
                AssetDatabase.CreateAsset(mat, matPath);
            }

            var go = new GameObject("ARPlaneVisual", typeof(ARPlane), typeof(MeshFilter), typeof(MeshRenderer), typeof(ARPlaneMeshVisualizer));
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            UnityEngine.Object.DestroyImmediate(go);
            return prefab;
        }

        static void SetField(object target, string fieldName, object value)
        {
            var f = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null)
            {
                Debug.LogWarning($"[CarAR] Field '{fieldName}' not found on {target.GetType().Name}; set it in the Inspector.");
                return;
            }
            f.SetValue(target, value);
            if (target is UnityEngine.Object uo) EditorUtility.SetDirty(uo);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
