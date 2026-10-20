using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
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
        public CarPanel leftDoor, rightDoor, trunk, hood;
        public bool IsPreview => Application.platform != RuntimePlatform.Android && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "--xr") < 0 && !Huijing.HuijingMRBridge.Requested;
        float brightness = 0, temperature = 6500, yaw = 145, pitch = 22, distance = 6.4f;
        GUIStyle title, text, button;
        bool smokeControlsCamera;
        void Awake()
        {
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            QualitySettings.vSyncCount = 0;
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
                    if (vendor != null) vendor.ConfigureTrackingReference();
                    var manager = XRGeneralSettings.Instance.Manager;
                    yield return manager.InitializeLoader();
                    if (manager.activeLoader == null) { Debug.LogError("[CarAR] OpenXR initialization failed. Check the connected headset and runtime."); yield break; }
                    manager.StartSubsystems();
                    Debug.Log("[CarAR] PC OpenXR started: " + UnityEngine.XR.OpenXR.OpenXRRuntime.name + "; loader=" + manager.activeLoader.GetType().Name);
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
            if (k.digit1Key.wasPressedThisFrame) leftDoor.Toggle();
            if (k.digit2Key.wasPressedThisFrame) rightDoor.Toggle();
            if (k.digit3Key.wasPressedThisFrame) trunk.Toggle();
            if (k.digit4Key.wasPressedThisFrame) hood.Toggle();
            if (k.spaceKey.wasPressedThisFrame) explode.Toggle();
            if (k.escapeKey.wasPressedThisFrame) Application.Quit();
        }
        void OnGUI()
        {
            if (!IsPreview) return; // Headset interaction uses tracked 3D colliders, not a head-locked desktop overlay.
            title ??= new GUIStyle(GUI.skin.label) { fontSize = 27, fontStyle = FontStyle.Bold };
            text ??= new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            button ??= new GUIStyle(GUI.skin.button) { fontSize = 16, fixedHeight = 40 };
            float scale = Mathf.Max(.65f, Mathf.Min(Screen.width / 1280f, Screen.height / 800f));
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            GUILayout.BeginArea(new Rect(22, 20, 340, 720), GUI.skin.box);
            GUILayout.Space(10); GUILayout.Label("AMG GT / AR SHOWROOM", title);
            GUILayout.Label("DESKTOP PREVIEW", text); GUILayout.Space(12);
            GUILayout.Label("Drag right mouse to orbit. Scroll to zoom.", text);
            GUILayout.Space(14);
            GUI.enabled = car.gameObject.activeInHierarchy && !explode.IsBusy;
            if (GUILayout.Button("[1]  " + (leftDoor.IsOpen ? "Close left door" : "Open left door"), button)) leftDoor.Toggle();
            if (GUILayout.Button("[2]  " + (rightDoor.IsOpen ? "Close right door" : "Open right door"), button)) rightDoor.Toggle();
            if (GUILayout.Button("[3]  " + (trunk.IsOpen ? "Close rear hatch" : "Open rear hatch"), button)) trunk.Toggle();
            if (GUILayout.Button("[4]  " + (hood.IsOpen ? "Close hood" : "Open hood"), button)) hood.Toggle();
            GUI.enabled = car.gameObject.activeInHierarchy;
            if (GUILayout.Button("[Space]  " + (explode.IsExploded ? "Assemble vehicle" : "Explode vehicle"), button)) explode.Toggle();
            GUI.enabled = true; GUILayout.Space(16);
            GUILayout.Label("HDR ENVIRONMENT / OPTIONAL FILL", text);
            GUILayout.Label($"Direct fill  {brightness:F2}", text);
            float b = GUILayout.HorizontalSlider(brightness, 0, 1f);
            GUILayout.Label($"Color temperature  {temperature:F0} K", text);
            float t = GUILayout.HorizontalSlider(temperature, 2500, 10000);
            if (b != brightness || t != temperature) { brightness = b; temperature = t; lighting.SetPreviewLight(b, t); }
            GUILayout.Space(18); GUILayout.Label("Floor: " + placement.Status, text);
            GUILayout.Label("Light: " + lighting.Status, text);
            GUILayout.Label("Hands: " + gestures.Status, text);
            GUILayout.Space(16);
            GUILayout.Label("AMG.blend: original independent door, window, mirror, hood and hatch meshes. Default scale = 1:1.", text);
            GUILayout.Space(8);
            GUILayout.Label("HDR lighting is shared with MR. Direct fill is optional; live estimation requires a device provider.", text);
            GUILayout.EndArea();
        }
        IEnumerator Smoke()
        {
            yield return new WaitForSeconds(2);
            if (car.localScale != Vector3.one || gestures.ManipulationEnabled || !car.gameObject.activeInHierarchy)
                throw new System.InvalidOperationException("First activation did not retain a visible, locked 1:1 vehicle");
            if (RenderSettings.defaultReflectionMode != DefaultReflectionMode.Custom ||
                RenderSettings.customReflectionTexture == null || RenderSettings.customReflectionTexture.mipmapCount < 5 ||
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
            if (modelSize.z < 4.5f || modelSize.z > 4.9f || modelSize.y < 1.2f || modelSize.y > 1.5f)
                throw new System.InvalidOperationException("AMG physical scale / axis conversion is incorrect: " + modelSize);
            foreach (var panel in new[] { leftDoor, rightDoor })
            {
                bool shell = false, glass = false, mirror = false;
                foreach (var r in panel.GetComponentsInChildren<MeshRenderer>())
                {
                    shell |= r.name.StartsWith("Cube.002__") || r.name.StartsWith("Cube.135__");
                    glass |= r.name.StartsWith("Cube.017__");
                    mirror |= r.name.StartsWith("Cube.139__");
                }
                if (!shell || !glass || !mirror) throw new System.InvalidOperationException("AMG door is missing original shell, glass or mirror: " + panel.name);
            }
            if (explode.assemblyRoots.Length != 4) throw new System.InvalidOperationException("AMG wheel assemblies are missing");
            leftDoor.Toggle();
            yield return new WaitForSeconds(1);
            CaptureVehicle("left-door.png");
            leftDoor.Close();
            yield return new WaitForSeconds(1);
            var positions = new Vector3[parts.Length];
            var rotations = new Quaternion[parts.Length];
            var doorClosedPoints = new Vector3[4];
            var panels = new[] { leftDoor, rightDoor, trunk, hood };
            for (int i = 0; i < parts.Length; i++) positions[i] = parts[i].transform.localPosition;
            for (int i = 0; i < parts.Length; i++) rotations[i] = parts[i].transform.localRotation;
            for (int i = 0; i < panels.Length; i++) doorClosedPoints[i] = panels[i].GetComponentInChildren<MeshRenderer>().bounds.center;
            leftDoor.Toggle(); rightDoor.Toggle(); trunk.Toggle(); hood.Toggle();
            yield return new WaitForSeconds(1);
            if (!leftDoor.IsOpen || !rightDoor.IsOpen || !trunk.IsOpen || !hood.IsOpen) { Debug.LogError("[CarAR Smoke] Panel open failed"); Application.Quit(1); yield break; }
            for (int i = 0; i < panels.Length; i++)
                if (Vector3.Distance(doorClosedPoints[i], panels[i].GetComponentInChildren<MeshRenderer>().bounds.center) < .05f)
                    throw new System.InvalidOperationException("AMG animation did not move real panel geometry: " + panels[i].name);
            CaptureVehicle("panels.png");
            yield return new WaitForSeconds(.2f);
            explode.SetTarget(1);
            yield return new WaitForSeconds(2.8f);
            if (explode.Progress < .999f || !leftDoor.IsClosed || !rightDoor.IsClosed || !trunk.IsClosed || !hood.IsClosed) { Debug.LogError("[CarAR Smoke] Explode failed"); Application.Quit(1); yield break; }
            foreach (var r in parts)
                if (r.bounds.min.y < placement.GroundHeight - .01f)
                    throw new System.InvalidOperationException("AMG exploded part went below the review floor: " + r.name);
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
            Debug.Log($"[CarAR Smoke] PASS: parked root stable, locked transforms rejected, explicit scaling preserves ground pivot, {parts.Length} renderers, 4 panel animations, explode/assemble restores original positions, light intensity and color response.");
            yield return new WaitForSeconds(.5f); Application.Quit(0);
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
            if (file == "left-door.png")
            {
                camera.transform.position = leftDoor.transform.position + car.rotation * new Vector3(-2.4f, 1.15f, -1.5f) * car.localScale.x;
                camera.transform.LookAt(leftDoor.transform.position + Vector3.up * .05f * car.localScale.x);
            }
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
            if (ui == null || ui.buttons.Length != 28 || manipulator == null || manipulator.enabled)
                throw new System.InvalidOperationException("MRTK controls missing or car not locked initially");
            var originalPosition = car.position; var originalRotation = car.rotation; var originalScale = car.localScale;
            ui.buttons[4].OnClicked.Invoke();
            if (!manipulator.enabled) throw new System.InvalidOperationException("MRTK unlock button failed");
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
            first.EndManualInteraction(); second.EndManualInteraction();
            ui.buttons[4].OnClicked.Invoke();
            var released = car.position;
            yield return new WaitForSeconds(.3f);
            if (Vector3.Distance(released, car.position) > .0001f)
                throw new System.InvalidOperationException("MRTK parked vehicle drifted after release");
            Destroy(a); Destroy(b);
            car.SetPositionAndRotation(originalPosition, originalRotation); car.localScale = originalScale;
            ui.buttons[0].OnClicked.Invoke();
            yield return new WaitForSeconds(.8f);
            if (!leftDoor.IsOpen) throw new System.InvalidOperationException("MRTK door button binding failed");
            ui.buttons[0].OnClicked.Invoke(); yield return new WaitForSeconds(.8f);
            Debug.Log("[CarAR MRTK Smoke] PASS: locked 1:1 first activation, XRI selection, one-hand translation without attach-scale changes, deliberate two-hand scaling, coincident/recovered controller poses preserve scale, release and park.");
        }

    }
}
