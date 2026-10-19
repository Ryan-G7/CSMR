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
        public CarPanel leftDoor, rightDoor, trunk;
        public bool IsPreview => Application.platform != RuntimePlatform.Android && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "--xr") < 0 && !Huijing.HuijingMRBridge.Requested;
        float brightness = 1.2f, temperature = 6500, yaw = 145, pitch = 22, distance = 6.4f;
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
            // Preview reflections for metallic paint; real AR probes come from the device provider.
            var sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_Exposure", .9f); RenderSettings.skybox = sky;

        }
        IEnumerator Start()
        {
            if (!IsPreview)
            {
                if (Application.platform != RuntimePlatform.Android)
                {
                    var manager = XRGeneralSettings.Instance.Manager;
                    yield return manager.InitializeLoader();
                    if (manager.activeLoader == null) { Debug.LogError("[CarAR] OpenXR initialization failed. Check the connected headset and runtime."); yield break; }
                    manager.StartSubsystems();
                    Debug.Log("[CarAR] PC OpenXR started: " + UnityEngine.XR.OpenXR.OpenXRRuntime.name);
                    var vendor = GetComponent<Huijing.HuijingMRBridge>();
                    if (vendor != null) yield return vendor.InitializeAfterOpenXR();
                    yield return new WaitForSeconds(3);
                    Debug.Log("[CarAR] Hand state: " + gestures.Status);
                }
                yield break;
            }
            placement.PlaceAt(Vector3.zero, true);
            lighting.SetPreviewLight(brightness, temperature);
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "--carar-smoke") >= 0)
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
            GUILayout.Space(10); GUILayout.Label("911 / AR SHOWROOM", title);
            GUILayout.Label("DESKTOP PREVIEW", text); GUILayout.Space(12);
            GUILayout.Label("Drag right mouse to orbit. Scroll to zoom.", text);
            GUILayout.Space(14);
            GUI.enabled = car.gameObject.activeInHierarchy && !explode.IsBusy;
            if (GUILayout.Button("[1]  " + (leftDoor.IsOpen ? "Close left door" : "Open left door"), button)) leftDoor.Toggle();
            if (GUILayout.Button("[2]  " + (rightDoor.IsOpen ? "Close right door" : "Open right door"), button)) rightDoor.Toggle();
            if (GUILayout.Button("[3]  " + (trunk.IsOpen ? "Close front trunk" : "Open front trunk"), button)) trunk.Toggle();
            GUI.enabled = car.gameObject.activeInHierarchy;
            if (GUILayout.Button("[Space]  " + (explode.IsExploded ? "Assemble vehicle" : "Explode vehicle"), button)) explode.Toggle();
            GUI.enabled = true; GUILayout.Space(16);
            GUILayout.Label("LIGHT SIMULATION", text);
            GUILayout.Label($"Intensity  {brightness:F2}", text);
            float b = GUILayout.HorizontalSlider(brightness, .03f, 3f);
            GUILayout.Label($"Color temperature  {temperature:F0} K", text);
            float t = GUILayout.HorizontalSlider(temperature, 2500, 10000);
            if (b != brightness || t != temperature) { brightness = b; temperature = t; lighting.SetPreviewLight(b, t); }
            GUILayout.Space(18); GUILayout.Label("Floor: " + placement.Status, text);
            GUILayout.Label("Light: " + lighting.Status, text);
            GUILayout.Label("Hands: " + gestures.Status, text);
            GUILayout.Space(16);
            GUILayout.Label("911 mesh panels use an approximate spatial split. Front trunk = luggage compartment; rear lid = engine cover.", text);
            GUILayout.Space(8);
            GUILayout.Label("Real AR requires the headset's plane / camera / light providers. Desktop lighting is simulated.", text);
            GUILayout.EndArea();
        }
        IEnumerator Smoke()
        {
            Application.logMessageReceived += (condition, stack, type) => { if (type == LogType.Exception) Application.Quit(1); };
            yield return new WaitForSeconds(2);
            yield return MRTKInteractionSmoke();
            smokeControlsCamera = true;
            yield return ControllerInputSmoke.Run(this);
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
            var positions = new Vector3[parts.Length];
            for (int i = 0; i < parts.Length; i++) positions[i] = parts[i].transform.localPosition;
            leftDoor.Toggle(); rightDoor.Toggle(); trunk.Toggle();
            yield return new WaitForSeconds(1);
            if (!leftDoor.IsOpen || !rightDoor.IsOpen || !trunk.IsOpen) { Debug.LogError("[CarAR Smoke] Panel open failed"); Application.Quit(1); yield break; }
            ScreenCapture.CaptureScreenshot(Path.Combine(Application.dataPath, "../panels.png"));
            yield return new WaitForSeconds(.2f);
            explode.SetTarget(1);
            yield return new WaitForSeconds(2.8f);
            if (explode.Progress < .999f || !leftDoor.IsClosed || !rightDoor.IsClosed || !trunk.IsClosed) { Debug.LogError("[CarAR Smoke] Explode failed"); Application.Quit(1); yield break; }
            ScreenCapture.CaptureScreenshot(Path.Combine(Application.dataPath, "../exploded.png"));
            yield return new WaitForSeconds(.2f);
            explode.SetTarget(0);
            yield return new WaitForSeconds(2);
            for (int i = 0; i < parts.Length; i++)
                if (Vector3.Distance(positions[i], parts[i].transform.localPosition) > .0001f) { Debug.LogError("[CarAR Smoke] Assembly did not restore transforms"); Application.Quit(1); yield break; }
            ScreenCapture.CaptureScreenshot(Path.Combine(Application.dataPath, "../assembled.png"));
            yield return new WaitForSeconds(.2f);
            lighting.SetPreviewLight(.08f, 2800);
            yield return new WaitForSeconds(2);
            if (RenderSettings.sun.intensity > .12f || RenderSettings.sun.color.b > .6f) { Debug.LogError("[CarAR Smoke] Light response failed"); Application.Quit(1); yield break; }
            ScreenCapture.CaptureScreenshot(Path.Combine(Application.dataPath, "../warm-dim.png"));
            Debug.Log($"[CarAR Smoke] PASS: parked root stable, locked transforms rejected, explicit scaling preserves ground pivot, {parts.Length} renderers, 3 panel animations, explode/assemble restores original positions, light intensity and color response.");
            yield return new WaitForSeconds(.5f); Application.Quit(0);
        }

        IEnumerator MRTKInteractionSmoke()
        {
            var ui = GetComponent<MRTKShowroomUI>();
            var manipulator = gestures.manipulator;
            if (ui == null || ui.buttons.Length != 27 || manipulator == null || manipulator.enabled)
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
            second.StartManualInteraction((IXRSelectInteractable)manipulator);
            yield return null;
            float scale = car.localScale.x;
            a.transform.position += Vector3.left * .15f; b.transform.position += Vector3.right * .15f;
            yield return new WaitForSeconds(.6f);
            if (manipulator.interactorsSelecting.Count != 2 || car.localScale.x <= scale * 1.1f)
                throw new System.InvalidOperationException("MRTK two-hand scaling failed");
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
            Debug.Log("[CarAR MRTK Smoke] PASS: standard UX bindings, XRI selection, ObjectManipulator one-hand translation and two-hand scaling, release and park.");
        }

    }
}
