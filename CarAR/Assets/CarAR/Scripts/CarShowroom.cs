using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
namespace CarAR
{
    [DefaultExecutionOrder(-200)]
    public class CarShowroom : MonoBehaviour
    {
        public Transform car;
        public CarPlacementManager placement;
        public ARLightEstimation lighting;
        public MRTKCarInteraction gestures;
        public ExplodeController explode;
        public RuntimeGlbLoader modelLoader;
        public ModelReview review;
        public ReviewSnapshot snapshot;
        public ModelAnnotations annotations;
        public ModelMeasurement measurement;
        public bool IsPreview => Application.platform != RuntimePlatform.Android && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "--xr") < 0 &&
            !Huijing.HuijingMRBridge.Requested && !VarjoSupport.VarjoMRBridge.Requested;
        float brightness = 0, temperature = 6500, yaw = 145, pitch = 22, distance = 6.4f;
        GUIStyle title, text, button;
        Vector2 toolsScroll;
        bool smokeControlsCamera;
        void Awake()
        {
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            QualitySettings.vSyncCount = 0;
            modelLoader = gameObject.AddComponent<RuntimeGlbLoader>();
            modelLoader.modelRoot = car; modelLoader.interaction = gestures; modelLoader.explode = explode; modelLoader.placement = placement;
            review = gameObject.AddComponent<ModelReview>(); review.modelRoot = car; review.loader = modelLoader;
            annotations = gameObject.AddComponent<ModelAnnotations>();
            annotations.modelRoot = car; annotations.loader = modelLoader; annotations.viewCamera = Camera.main; annotations.IsDesktop = IsPreview;
            annotations.font = GetComponent<MRTKShowroomUI>()?.status?.font; annotations.AimAtModel = AimAtModel;
            snapshot = gameObject.AddComponent<ReviewSnapshot>(); snapshot.sourceCamera = Camera.main; snapshot.loader = modelLoader; snapshot.annotations = annotations;
            measurement = gameObject.AddComponent<ModelMeasurement>();
            measurement.modelRoot = car; measurement.loader = modelLoader; measurement.viewCamera = Camera.main; measurement.IsDesktop = IsPreview; measurement.AimAtModel = AimAtModel;
            if (!IsPreview) return;
            foreach (var manager in FindObjectsByType<ARSession>(FindObjectsSortMode.None)) manager.enabled = false;
            foreach (var manager in FindObjectsByType<AREnvironmentProbeManager>(FindObjectsSortMode.None)) manager.enabled = false;
            foreach (var manager in FindObjectsByType<ARPlaneManager>(FindObjectsSortMode.None)) manager.enabled = false;
            foreach (var manager in FindObjectsByType<ARCameraManager>(FindObjectsSortMode.None)) manager.enabled = false;
            foreach (var manager in FindObjectsByType<ARCameraBackground>(FindObjectsSortMode.None)) manager.enabled = false;
            var driver = Camera.main.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            if (driver != null) driver.enabled = false;
            var origin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (origin != null) origin.enabled = false;
            Camera.main.backgroundColor = new Color(.055f,.075f,.105f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Desktop preview floor"; floor.transform.localScale = Vector3.one * 4;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.color = new Color(.13f,.17f,.21f); material.SetFloat("_Smoothness", .3f);
            floor.GetComponent<Renderer>().sharedMaterial = material;

        }
        IEnumerator Start()
        {
            if (!IsPreview)
            {
                if (Application.platform != RuntimePlatform.Android)
                {
                    var vendor = GetComponent<Huijing.HuijingMRBridge>();
                    var varjo = GetComponent<VarjoSupport.VarjoMRBridge>();
                    if (vendor != null) vendor.ConfigureTrackingReference();
                    var manager = XRGeneralSettings.Instance.Manager;
                    VarjoSupport.VarjoMRBridge.PrepareLoader(manager);
                    yield return manager.InitializeLoader();
                    if (manager.activeLoader == null) { Debug.LogError("[CarAR] XR initialization failed. Check the connected headset and runtime."); yield break; }
                    if (!VarjoSupport.VarjoMRBridge.ValidateLoader(manager.activeLoader)) yield break;
                    manager.StartSubsystems();
                    string runtime = VarjoSupport.VarjoMRBridge.Requested ? "Varjo Base" : UnityEngine.XR.OpenXR.OpenXRRuntime.name;
                    Debug.Log("[CarAR] PC XR started: " + runtime + "; loader=" + manager.activeLoader.GetType().Name);
                    if (VarjoSupport.VarjoMRBridge.MixedRealityRequested && varjo != null)
                    {
                        yield return null;
                        if (!varjo.StartMixedReality()) yield break;
                    }
                    if (vendor != null)
                    {
                        yield return null;
                        vendor.ApplyTrackingReference(manager.activeLoader.GetLoadedSubsystem<UnityEngine.XR.XRInputSubsystem>());
                        yield return vendor.InitializeAfterOpenXR();
                    }
                    yield return new WaitForSeconds(3);
                    Debug.Log("[CarAR] Hand state: " + gestures.Status);
                }
                yield break;
            }
            bool smoke = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "--carar-smoke") >= 0;
            if (smoke)
            {
                Application.logMessageReceived += (condition, stack, type) => { if (type == LogType.Exception) Application.Quit(1); };
                // Exercise the same inactive-to-first-placement lifecycle used in MR.
                yield return new WaitForSeconds(.25f);
                if (car.gameObject.activeInHierarchy || car.localScale != Vector3.one)
                    throw new System.InvalidOperationException("Vehicle became visible or changed scale before initial placement");
            }
            placement.PlaceAt(Vector3.zero, true);
            if (smoke)
                yield return Smoke();
        }
        void OnDestroy()
        {
            if (IsPreview || Application.platform == RuntimePlatform.Android) return;
            var varjo = GetComponent<VarjoSupport.VarjoMRBridge>();
            if (varjo != null) varjo.Shutdown();
            var vendor = GetComponent<Huijing.HuijingMRBridge>();
            if (vendor != null) vendor.Shutdown();
            var settings = XRGeneralSettings.Instance;
            if (settings != null && settings.Manager != null && settings.Manager.isInitializationComplete)
            { settings.Manager.StopSubsystems(); settings.Manager.DeinitializeLoader(); }
        }
        void LateUpdate()
        {
            if (!IsPreview || smokeControlsCamera) return;
            var mouse = Application.isFocused ? Mouse.current : null;
            if (mouse != null)
            {
                if (mouse.rightButton.isPressed) { yaw += mouse.delta.ReadValue().x * .2f; pitch = Mathf.Clamp(pitch - mouse.delta.ReadValue().y * .2f, 5, 75); }
                distance = Mathf.Clamp(distance - mouse.scroll.ReadValue().y * .004f, 1.5f, 15f);
            }
            var target = car.position + Vector3.up * .65f;
            Camera.main.transform.position = target + Quaternion.Euler(pitch, yaw, 0) * (Vector3.back * distance);
            var look = Quaternion.LookRotation(target - Camera.main.transform.position);
            Camera.main.transform.LookAt(target - look * Vector3.right * .3f);
            var k = Application.isFocused ? Keyboard.current : null;
            if (k == null) return;
            if (k.spaceKey.wasPressedThisFrame) explode.Toggle();
            if (k.escapeKey.wasPressedThisFrame) Application.Quit();
        }
        bool AimAtModel(out RaycastHit hit)
        {
            var input = GetComponent<MRTKControllerInput>();
            int mask = 1 << MRTKControllerInput.VehicleLayer;
            if (input.RightRayVisible && Physics.Raycast(input.RightRay.transform.position, input.RightRay.transform.forward,
                out hit, input.RightRay.maxRaycastDistance, mask)) return true;
            if (input.LeftRayVisible && Physics.Raycast(input.LeftRay.transform.position, input.LeftRay.transform.forward,
                out hit, input.LeftRay.maxRaycastDistance, mask)) return true;
            hit = default;
            return false;
        }
        public void ToggleHandClipping()
        {
            var input = GetComponent<MRTKControllerInput>();
            if (input == null) return;
            var hand = input.RightRayVisible || !input.LeftRayVisible ? input.RightRay : input.LeftRay;
            review.SetHandClipping(!review.HandClipping, hand.transform);
        }
        void OnGUI()
        {
            if (!IsPreview) return; // Headset interaction uses tracked 3D colliders, not a head-locked desktop overlay.
            title ??= new GUIStyle(GUI.skin.label) { fontSize = 27, fontStyle = FontStyle.Bold };
            text ??= new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            button ??= new GUIStyle(GUI.skin.button) { fontSize = 16, fixedHeight = 40 };
            float scale = Mathf.Max(.65f, Mathf.Min(Screen.width / 1280f, Screen.height / 800f));
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            GUILayout.BeginArea(new Rect(22, 20, 360, 750), GUI.skin.box);
            toolsScroll = GUILayout.BeginScrollView(toolsScroll);
            GUILayout.Space(10); GUILayout.Label("GLB / MR MODEL REVIEW", title);
            GUILayout.Label(modelLoader.Status, text); GUILayout.Space(8);
            if (GUILayout.Button("导入 GLB 模型", button)) modelLoader.PickFile();
            if (GUILayout.Button("VR 场景漫游", button)) XRLaunch.OpenVRPreview();
            GUILayout.Label("右键环视 · 滚轮缩放", text);
            GUI.enabled = car.gameObject.activeInHierarchy;
            if (GUILayout.Button("[空格] " + (explode.IsExploded ? "组装还原" : "模型爆炸"), button)) explode.Toggle();
            if (GUILayout.Button(review.Clipping ? "关闭剖切" : "开启剖切", button)) review.SetClipping(!review.Clipping);
            if (review.Clipping)
            {
                GUILayout.Label($"剖切面 {review.PlaneNormal} · 偏移 {review.PlaneOffset:F2}m", text);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("X")) review.SetPlaneAxis(Vector3.right);
                if (GUILayout.Button("Y")) review.SetPlaneAxis(Vector3.up);
                if (GUILayout.Button("Z")) review.SetPlaneAxis(Vector3.forward);
                if (GUILayout.Button("-10cm")) review.MovePlane(-.1f);
                if (GUILayout.Button("+10cm")) review.MovePlane(.1f);
                GUILayout.EndHorizontal();
            }
            GUILayout.Label(review.SelectedName, text);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("上一材质")) review.SelectMaterial(-1);
            if (GUILayout.Button("下一材质")) review.SelectMaterial(1);
            if (GUILayout.Button("还原")) review.ResetMaterial();
            GUILayout.EndHorizontal();
            var color = review.ColorValue;
            GUILayout.Label($"金属度 {review.Metallic:F2} · 粗糙度 {review.Roughness:F2} · 透明度 {color.a:F2}", text);
            float metallic = GUILayout.HorizontalSlider(review.Metallic, 0, 1);
            float roughness = GUILayout.HorizontalSlider(review.Roughness, 0, 1);
            float opacity = GUILayout.HorizontalSlider(color.a, 0, 1);
            if (metallic != review.Metallic) review.SetMetallic(metallic);
            if (roughness != review.Roughness) review.SetRoughness(roughness);
            if (opacity != color.a) review.SetOpacity(opacity);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("红色")) review.SetColor(new Color(.8f, .04f, .03f, color.a));
            if (GUILayout.Button("白色")) review.SetColor(new Color(.9f, .9f, .9f, color.a));
            if (GUILayout.Button("蓝色")) review.SetColor(new Color(.03f, .25f, .8f, color.a));
            GUILayout.EndHorizontal();
            if (GUILayout.Button("保存快照 PNG", button))
            { snapshot.Capture(); modelLoader.GetComponent<MRTKShowroomUI>()?.Notice(snapshot.LastPath); }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(annotations.AwaitingPlacement ? "单击模型选点…" : "添加文字标注")) annotations.BeginPlacement();
            if (GUILayout.Button("撤销标注")) annotations.RemoveLast();
            GUILayout.EndHorizontal();
            GUILayout.Label($"已保存标注 {annotations.Count} 个", text);
            if (GUILayout.Button("注释当前快照", button)) annotations.BeginSnapshotAnnotation(snapshot);
            GUILayout.Label(measurement.Status, text);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("两点距离")) measurement.Begin(ModelMeasurement.MeasurementMode.Distance);
            if (GUILayout.Button("曲线")) measurement.Begin(ModelMeasurement.MeasurementMode.Curve);
            if (GUILayout.Button("曲面面积")) measurement.Begin(ModelMeasurement.MeasurementMode.Surface);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("完成曲线")) measurement.Finish();
            if (GUILayout.Button("清除测量")) measurement.Clear();
            GUILayout.EndHorizontal();
            GUILayout.Label(review.SelectedMeshName, text);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("上一网格")) review.SelectMesh(-1);
            if (GUILayout.Button("下一网格")) review.SelectMesh(1);
            if (GUILayout.Button("显示 / 隐藏")) review.ToggleSelectedMesh();
            if (GUILayout.Button("全部显示")) review.ShowAllMeshes();
            GUILayout.EndHorizontal();
            GUI.enabled = true;
            GUILayout.Label($"补光 {brightness:F2}", text);
            float b = GUILayout.HorizontalSlider(brightness, 0, 1f);
            GUILayout.Label($"色温 {temperature:F0} K", text);
            float t = GUILayout.HorizontalSlider(temperature, 2500, 10000);
            if (b != brightness || t != temperature) { brightness = b; temperature = t; lighting.SetPreviewLight(b, t); }
            GUILayout.Label("地面: " + placement.Status, text);
            GUILayout.Label("快照目录: " + System.IO.Path.Combine(Application.persistentDataPath, "Snapshots"), text);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
        IEnumerator Smoke()
        {
            yield return new WaitForSeconds(2);
            if (!string.IsNullOrWhiteSpace(XRLaunch.ModelPath))
            {
                float deadline = Time.time + 30;
                while (modelLoader.IsLoading && Time.time < deadline) yield return null;
                if (modelLoader.IsLoading || modelLoader.CurrentPath != Path.GetFullPath(XRLaunch.ModelPath) ||
                    modelLoader.RendererCount == 0 || modelLoader.RendererCount != car.GetComponentsInChildren<MeshRenderer>(true).Length)
                    throw new System.InvalidOperationException("External GLB did not completely replace the default model");
                yield return ReviewToolsSmoke();
            }
            if (car.localScale != Vector3.one || gestures.ManipulationEnabled || !car.gameObject.activeInHierarchy)
                throw new System.InvalidOperationException("First activation did not retain a visible, locked 1:1 vehicle");
            if (RenderSettings.defaultReflectionMode != DefaultReflectionMode.Custom ||
                RenderSettings.customReflectionTexture == null || RenderSettings.customReflectionTexture.mipmapCount < 5 ||
                RenderSettings.skybox == null || RenderSettings.skybox.GetTexture("_Tex") != RenderSettings.customReflectionTexture ||
                RenderSettings.ambientMode != AmbientMode.Custom || RenderSettings.sun.intensity > .01f)
                throw new System.InvalidOperationException("HDR diffuse/specular environment or zero-fill default is missing");
            CaptureVehicle("hdr-default.png");
            yield return MRTKInteractionSmoke();
            smokeControlsCamera = true;
            yield return ControllerInputSmoke.Run(this);
            yield return ReviewRegressionSmoke.Run(this);
            smokeControlsCamera = false;
            var parked = car.position;
            var parkedScale = car.localScale;
            var parkedRotation = car.rotation;
            gestures.SetManipulation(false);
            gestures.ScaleBy(1.2f); gestures.RotateBy(15);
            if (car.localScale != parkedScale || Quaternion.Angle(car.rotation, parkedRotation) > .001f)
            { Debug.LogError("[CarAR Smoke] Locked car accepted a transform command"); Application.Quit(1); yield break; }
            yield return new WaitForSeconds(.5f);
            if (Vector3.Distance(car.position, parked) > .0001f)
            { Debug.LogError("[CarAR Smoke] Parked car drifted"); Application.Quit(1); yield break; }
            gestures.SetManipulation(true); gestures.ScaleBy(1.2f); gestures.RotateBy(15);
            if (Mathf.Abs(car.localScale.x - parkedScale.x * 1.2f) > .0001f || Vector3.Distance(car.position, parked) > .0001f)
            { Debug.LogError("[CarAR Smoke] Unlocked scaling or ground pivot failed"); Application.Quit(1); yield break; }
            car.localScale = parkedScale; car.rotation = parkedRotation; gestures.SetManipulation(false);
            var parts = car.GetComponentsInChildren<MeshRenderer>();
            foreach (var r in parts) Debug.Log($"[CarAR Render] {r.name} active={r.gameObject.activeInHierarchy} enabled={r.enabled} bounds={r.bounds} shader={r.sharedMaterial.shader.name}");
            if (parts.Length == 0) { Debug.LogError("[CarAR Smoke] No renderers"); Application.Quit(1); yield break; }
            var modelSize = gestures.ModelSize;
            if (modelSize.x <= 0 || modelSize.y <= 0 || modelSize.z <= 0)
                throw new System.InvalidOperationException("Model bounds are invalid: " + modelSize);
            var positions = new Vector3[parts.Length];
            var rotations = new Quaternion[parts.Length];
            for (int i = 0; i < parts.Length; i++) positions[i] = parts[i].transform.localPosition;
            for (int i = 0; i < parts.Length; i++) rotations[i] = parts[i].transform.localRotation;
            yield return new WaitForSeconds(.2f);
            explode.SetTarget(1);
            yield return new WaitForSeconds(2.8f);
            if (explode.Progress < .999f || explode.AssemblyCount == 0) { Debug.LogError("[CarAR Smoke] Explode failed"); Application.Quit(1); yield break; }
            foreach (var r in parts)
                if (r.bounds.min.y < placement.GroundHeight - .01f)
                    throw new System.InvalidOperationException("Exploded model part went below the review floor: " + r.name);
            CaptureVehicle("exploded.png");
            yield return new WaitForSeconds(.2f);
            explode.SetTarget(0);
            yield return new WaitForSeconds(2);
            for (int i = 0; i < parts.Length; i++)
                if (Vector3.Distance(positions[i], parts[i].transform.localPosition) > .0001f || Quaternion.Angle(rotations[i], parts[i].transform.localRotation) > .001f)
                { Debug.LogError("[CarAR Smoke] Assembly did not restore transforms"); Application.Quit(1); yield break; }
            CaptureVehicle("assembled.png");
            yield return new WaitForSeconds(.2f);
            lighting.SetPreviewLight(.08f, 2800);
            yield return new WaitForSeconds(2);
            if (RenderSettings.sun.intensity > .12f || RenderSettings.sun.color.b > .6f) { Debug.LogError("[CarAR Smoke] Light response failed"); Application.Quit(1); yield break; }
            CaptureVehicle("warm-dim.png");
            Debug.Log($"[CarAR Smoke] PASS: parked root stable, locked transforms rejected, explicit scaling preserves ground pivot, {parts.Length} renderers, generic explode/assemble restores original positions, light intensity and color response.");
            yield return new WaitForSeconds(.5f); Application.Quit(0);
        }

        IEnumerator ReviewToolsSmoke()
        {
            while (annotations.Count > 0) annotations.RemoveLast();
            if (review.SelectedMaterial == null) throw new System.InvalidOperationException("External GLB has no editable material");
            var originalColor = review.ColorValue;
            float originalMetallic = review.Metallic, originalRoughness = review.Roughness;
            review.SetColor(new Color(.21f, .43f, .67f, .62f));
            review.SetMetallic(.73f); review.SetRoughness(.41f);
            var edited = ActiveReviewMaterial();
            float actualMetallic = edited != null && edited.HasProperty("_Metallic") ? edited.GetFloat("_Metallic") : edited != null ? edited.GetFloat("metallicFactor") : -1;
            float actualRoughness = edited != null && edited.HasProperty("_Roughness") ? edited.GetFloat("_Roughness") : edited != null && edited.HasProperty("roughnessFactor") ? edited.GetFloat("roughnessFactor") : edited != null ? 1 - edited.GetFloat("_Smoothness") : -1;
            var actualColor = edited != null && edited.HasProperty("_BaseColor") ? edited.GetColor("_BaseColor") : edited != null ? edited.GetColor("baseColorFactor") : Color.clear;
            if (Mathf.Abs(actualMetallic - .73f) > .001f || Mathf.Abs(actualRoughness - .41f) > .001f || Mathf.Abs(actualColor.a - .62f) > .001f)
                throw new System.InvalidOperationException($"Material edits failed: selected={review.SelectedName}; shader={edited?.shader?.name}; metallic={actualMetallic}; roughness={actualRoughness}; color={actualColor}; overrides={review.Metallic}/{review.Roughness}/{review.ColorValue}");
            bool blended = edited.GetFloat("_SrcBlend") == (float)UnityEngine.Rendering.BlendMode.SrcAlpha &&
                edited.GetFloat("_DstBlend") == (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha &&
                edited.renderQueue == (int)UnityEngine.Rendering.RenderQueue.Transparent &&
                (edited.shader.name == "CarAR/ReviewClip" || edited.HasProperty("_Surface") && edited.GetFloat("_Surface") == 1);
            if (!blended)
                throw new System.InvalidOperationException($"glTF opacity did not activate blending: shader={edited.shader.name}; queue={edited.renderQueue}; src={edited.GetFloat("_SrcBlend")}; dst={edited.GetFloat("_DstBlend")}");
            review.SetOpacity(1);
            var opaque = ActiveReviewMaterial();
            if (opaque == null || opaque.renderQueue == (int)UnityEngine.Rendering.RenderQueue.Transparent ||
                opaque.HasProperty("_Surface") && opaque.GetFloat("_Surface") == 1)
                throw new System.InvalidOperationException("Opaque restore retained transparent rendering");
            review.SetOpacity(.62f);
            review.SetClipping(true); review.SetPlaneAxis(Vector3.up); review.MovePlane(.1f);
            yield return null;
            foreach (var renderer in car.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    if (material != null && material.shader.name != "CarAR/ReviewClip")
                        throw new System.InvalidOperationException("Clipping did not cover every external GLB material");
            review.SetClipping(false);
            if (ActiveReviewMaterial() == null) throw new System.InvalidOperationException("Clipping discarded the edited material");
            review.ResetMaterial();
            if (Vector4.Distance(review.ColorValue, originalColor) > .001f || Mathf.Abs(review.Metallic - originalMetallic) > .001f ||
                Mathf.Abs(review.Roughness - originalRoughness) > .001f)
                throw new System.InvalidOperationException("Material reset did not restore original values");
            var targetRenderer = car.GetComponentInChildren<MeshRenderer>();
            Vector2 screen = Camera.main.WorldToScreenPoint(targetRenderer.bounds.center);
            measurement.Begin(ModelMeasurement.MeasurementMode.Distance);
            yield return new WaitForSeconds(.3f);
            yield return Click(screen);
            yield return Click(screen + new Vector2(12, 0));
            if (measurement.AwaitingPoint || measurement.LastLength <= 0)
                throw new System.InvalidOperationException("Two-point surface measurement failed");
            measurement.Begin(ModelMeasurement.MeasurementMode.Curve);
            yield return new WaitForSeconds(.3f);
            yield return Click(screen);
            yield return Click(screen + new Vector2(12, 0));
            measurement.Finish();
            if (measurement.AwaitingPoint || measurement.LastLength <= 0)
                throw new System.InvalidOperationException("Curve measurement failed");
            measurement.Begin(ModelMeasurement.MeasurementMode.Surface);
            yield return new WaitForSeconds(.3f);
            yield return Click(screen);
            if (measurement.LastSurfaceArea <= 0 || measurement.LastMeshArea < measurement.LastSurfaceArea)
                throw new System.InvalidOperationException("Triangle and mesh surface-area measurement failed: " + measurement.Status);
            measurement.Clear();
            var selectedMesh = review.SelectedMesh;
            review.ToggleSelectedMesh();
            if (selectedMesh.enabled) throw new System.InvalidOperationException("Mesh visibility toggle did not hide the selected renderer");
            review.ShowAllMeshes();
            foreach (var renderer in car.GetComponentsInChildren<Renderer>(true))
                if (!renderer.enabled) throw new System.InvalidOperationException("Show-all did not restore every renderer");
            review.SetClipping(true);
            var hand = new GameObject("Smoke clipping hand").transform;
            hand.SetPositionAndRotation(car.position + Vector3.forward, Quaternion.identity);
            review.SetHandClipping(true, hand);
            yield return null;
            var planeBefore = Shader.GetGlobalVector("_CarARClipPlane");
            hand.position += Vector3.right * .2f;
            hand.rotation = Quaternion.Euler(0, 45, 0);
            yield return null;
            var planeAfter = Shader.GetGlobalVector("_CarARClipPlane");
            if (Vector4.Distance(planeBefore, planeAfter) < .01f)
                throw new System.InvalidOperationException("Clipping plane did not follow hand translation and rotation");
            review.SetHandClipping(false, null);
            review.SetClipping(false);
            Destroy(hand.gameObject);
            annotations.BeginPlacement();
            yield return new WaitForSeconds(.3f);
            yield return Click(screen);
            if (!annotations.Editing) throw new System.InvalidOperationException("Ray selection did not start annotation editing");
            annotations.Submit("Generic GLB note");
            if (annotations.Count != 1) throw new System.InvalidOperationException("Annotation was not created");
            string path = snapshot.Capture();
            var png = File.ReadAllBytes(path);
            if (png.Length < 100 || png[0] != 137 || png[1] != 80 || png[2] != 78 || png[3] != 71)
                throw new System.InvalidOperationException("Snapshot is not a valid PNG: " + path);
            annotations.BeginSnapshotAnnotation(snapshot);
            annotations.Submit("Snapshot review note");
            string infoPath = path + ".json";
            if (snapshot.Notes.Count != 1 || !File.Exists(infoPath) || !File.ReadAllText(infoPath).Contains("Snapshot review note"))
                throw new System.InvalidOperationException("Snapshot annotation was not saved with model information");
            _ = modelLoader.Load(modelLoader.CurrentPath);
            float deadline = Time.time + 30;
            while (modelLoader.IsLoading && Time.time < deadline) yield return null;
            if (modelLoader.IsLoading || annotations.Count != 1 || modelLoader.RendererCount != car.GetComponentsInChildren<MeshRenderer>(true).Length)
                throw new System.InvalidOperationException("Model reload did not restore saved annotations or isolated model content");
            annotations.RemoveLast();
            Debug.Log("[CarAR Review Tools] PASS: external GLB, materials, manual/hand clipping, distance/curve/surface measurement, mesh visibility, persisted model and snapshot annotations, model reload and PNG: " + path);
        }

        IEnumerator Click(Vector2 screen)
        {
            var mouse = Mouse.current;
            if (mouse == null) throw new System.InvalidOperationException("Review regression requires a mouse");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = screen, buttons = 1 });
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = screen });
            yield return null;
        }

        Material ActiveReviewMaterial()
        {
            foreach (var renderer in car.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    if (material != null && (material.name.EndsWith("(review)") || material.name.EndsWith("(clipped)"))) return material;
            return null;
        }

        void CaptureVehicle(string file)
        {
            // A minimized Windows player can have a black presentation surface.
            // Explicitly render the native URP scene to inspect the real materials.
            var go = new GameObject("Vehicle validation camera");
            var camera = go.AddComponent<Camera>(); camera.enabled = false;
            camera.CopyFrom(Camera.main);
            camera.transform.SetPositionAndRotation(Camera.main.transform.position, Camera.main.transform.rotation);
            camera.fieldOfView = 45;
            camera.transform.LookAt(car.position + Vector3.up * .65f);
            if (explode.Progress > .5f)
            {
                var centre = car.position + Vector3.up * .8f;
                camera.transform.position = centre + (camera.transform.position - centre) * 1.4f;
                camera.transform.LookAt(centre);
            }
            go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().allowXRRendering = false;
            var target = RenderTexture.GetTemporary(1440, 900, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 4);
            var previous = RenderTexture.active;
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            var image = new Texture2D(1440, 900, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, 1440, 900), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(Application.dataPath, "../" + file), image.EncodeToPNG());
            camera.targetTexture = null; RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target); Destroy(image); Destroy(go);
        }

        IEnumerator MRTKInteractionSmoke()
        {
            var ui = GetComponent<MRTKShowroomUI>();
            var manipulator = gestures.manipulator;
            if (ui == null || ui.buttons.Length != 56 || manipulator == null || manipulator.enabled)
                throw new System.InvalidOperationException("MRTK controls missing or car not locked initially");
            var originalPosition = car.position; var originalRotation = car.rotation; var originalScale = car.localScale;
            ui.buttons[4].OnClicked.Invoke();
            if (!manipulator.enabled) throw new System.InvalidOperationException("MRTK unlock button failed");
            gestures.ScaleBy(3);
            if (Mathf.Abs(car.localScale.x - 3) > .0001f)
                throw new System.InvalidOperationException("Explicit scaling is still capped at 2x");
            gestures.ScaleBy(100);
            if (car.localScale != Vector3.one * 10 || car.position != originalPosition)
                throw new System.InvalidOperationException("Explicit scaling failed to clamp at 10x around the ground pivot");
            gestures.ScaleBy(.000001f);
            if (car.localScale != Vector3.one * .1f)
                throw new System.InvalidOperationException("Explicit scaling lost its 0.1x minimum");
            ui.buttons[16].OnClicked.Invoke();
            if (car.localScale != originalScale)
                throw new System.InvalidOperationException("1:1 button failed after scaling to the limits");
            var a = new GameObject("Smoke left interactor");
            var b = new GameObject("Smoke right interactor");
            a.transform.position = car.position + Vector3.left * .2f + Vector3.up * .2f;
            b.transform.position = car.position + Vector3.right * .2f + Vector3.up * .2f;
            a.AddComponent<SphereCollider>().isTrigger = true; b.AddComponent<SphereCollider>().isTrigger = true;
            a.AddComponent<ActionBasedController>().enableInputTracking = false;
            b.AddComponent<ActionBasedController>().enableInputTracking = false;
            var first = a.AddComponent<XRDirectInteractor>(); var second = b.AddComponent<XRDirectInteractor>();
            first.interactionLayers = MRTKControllerInput.VehicleLayer; second.interactionLayers = MRTKControllerInput.VehicleLayer;
            first.StartManualInteraction((IXRSelectInteractable)manipulator);
            yield return null;
            a.transform.position += Vector3.right * .25f;
            yield return new WaitForSeconds(.6f);
            if (!manipulator.isSelected || car.position.x < originalPosition.x + .1f)
                throw new System.InvalidOperationException("MRTK selection / one-hand translation failed");
            var singleHandScale = car.localScale;
            var attach = first.GetAttachTransform(manipulator);
            var attachScale = attach.localScale;
            attach.localScale = Vector3.one * .25f;
            yield return new WaitForSeconds(.15f);
            if (car.localScale != singleHandScale)
                throw new System.InvalidOperationException("One-hand ray attach scale resized the vehicle");
            attach.localScale = attachScale;
            second.StartManualInteraction((IXRSelectInteractable)manipulator);
            yield return null;
            float scale = car.localScale.x;
            a.transform.position += Vector3.left * .15f; b.transform.position += Vector3.right * .15f;
            yield return new WaitForSeconds(.6f);
            if (manipulator.interactorsSelecting.Count != 2 || car.localScale.x <= scale * 1.1f)
                throw new System.InvalidOperationException("MRTK two-hand scaling failed");
            var beforeCoincidence = car.localScale;
            var secondPosition = b.transform.position;
            b.transform.position = a.transform.position;
            yield return new WaitForSeconds(.15f);
            if (Vector3.Distance(car.localScale, beforeCoincidence) > .0001f)
                throw new System.InvalidOperationException("Coincident controller poses collapsed the vehicle scale: " + car.localScale);
            b.transform.position = secondPosition;
            yield return new WaitForSeconds(.15f);
            if (Vector3.Distance(car.localScale, beforeCoincidence) > .0001f)
                throw new System.InvalidOperationException("Recovered controller poses jumped the vehicle scale");
            a.transform.position += Vector3.left * 20; b.transform.position += Vector3.right * 20;
            // MRTK smooths toward the constrained scale; allow that transition to settle.
            float scaleDeadline = Time.time + 3;
            while (Vector3.Distance(car.localScale, Vector3.one * 10) > .001f && Time.time < scaleDeadline)
            {
                if (car.localScale.x > 10.0001f)
                    throw new System.InvalidOperationException("MRTK two-hand scaling exceeded 10x");
                yield return null;
            }
            if (Vector3.Distance(car.localScale, Vector3.one * 10) > .001f)
                throw new System.InvalidOperationException("MRTK two-hand scaling did not reach and clamp at 10x: " + car.localScale.ToString("F6"));
            first.EndManualInteraction(); second.EndManualInteraction();
            ui.buttons[4].OnClicked.Invoke();
            var released = car.position;
            yield return new WaitForSeconds(.3f);
            if (Vector3.Distance(released, car.position) > .0001f)
                throw new System.InvalidOperationException("MRTK parked vehicle drifted after release");
            Destroy(a); Destroy(b);
            car.SetPositionAndRotation(originalPosition, originalRotation); car.localScale = originalScale;
            ui.buttons[1].OnClicked.Invoke();
            if (!review.Clipping) throw new System.InvalidOperationException("MRTK clipping button binding failed");
            ui.buttons[1].OnClicked.Invoke();
            if (review.Clipping) throw new System.InvalidOperationException("MRTK clipping restore failed");
            Debug.Log("[CarAR MRTK Smoke] PASS: locked 1:1 first activation, explicit 0.1x/10x limits and 1:1 reset, XRI selection, one-hand translation without attach-scale changes, deliberate two-hand scaling clamped at 10x, coincident/recovered controller poses preserve scale, release and park.");
        }

    }
}
