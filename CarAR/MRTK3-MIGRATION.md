# MRTK3 interaction migration

The generated scene retains the MRTK XR Rig camera/origin and interaction manager, standard Action Buttons and ObjectManipulator. Physical XRI controllers replace the prefab hand/gaze interaction objects. Application adapters bind review commands and parking policy; MRTK/XRI own tracked-ray selection, button visuals and one/two-controller manipulation.

The car starts parked at GLB scale 1. Physical controllers replace head dwell and the prefab's hand/gaze interactors. Standard OpenXR controller profiles are enabled. The camera, XR Origin and MRTK UX remain; native XRI ActionBasedController and XRRayInteractor handle tracked controller poses, trigger selection and grip rays. MRTK ObjectManipulator handles one/two-controller transforms. Grip interaction has a separate interaction layer from buttons and hinged panels.

Menu / Y / B toggles a world-space panel at the current viewing position. Trigger operates native MRTK Action Buttons and panel interactables. Unlock enables grip dragging and two-controller scale / yaw; left stick translates and right stick rotates / scales when no grip selection is active. Constraints retain a floor pivot, yaw-only rotation and scale 0.1–2. Head pose never selects a button. Door and explosion operations remain application animation content.

The main panel and precision page reuse standard MRTK Action Button prefabs. Floor placement is an application command: a LineRenderer footprint shows the existing floor reference, stick adjusts the preview height, and trigger explicitly confirms and parks the car. It is not presented as plane detection. Instructions and controls are documented in README.md.

## Rendering and device boundaries

URP Complex Lit, MSAA, native reflection-probe convolution and native URP SSAO supply rendering. Reflection geometry is a neutral hall preset on a camera-excluded layer, captured once by a Unity probe; it is not live environmental sensing. A supplied AR probe can take priority. The VST alpha bridge is retained. Environment measurements, actual controller tracking and correct video/pose alignment still depend on the device runtime.

The MRTK profile retains UnityHandsSubsystem, SyntheticHandsSubsystem and MRTKHandsAggregatorSubsystem, but the prefab hand/gaze interaction objects are disabled for this controller workflow. Input System actions are created/enabled in Awake before OpenXR initializes. The controllers are parented under the same CameraFloorOffsetObject as the HMD, rather than a separate world root.

The prefab's CameraSettingsManager and UnboundedTrackingMode remain disabled: this VST compositor reports an opaque display, whose default camera settings would destroy the transparent background. Floor tracking is configured by the application.

## Unity 6000.6 compatibility

MRTK source packages are local sibling dependencies in `../MixedRealityToolkit-Unity`; keep that directory when moving the project. XRI 2.6.5 and Graphics Tools 0.8.0 are embedded under Packages to make the following compatibility changes persistent:

- XRI editor debugger uses generic TreeView APIs and maps tree IDs to Unity objects via EntityId. Its legacy VR camera test helper is excluded on newer Unity.
- Graphics Tools editor material lookup uses EntityId. Mesh-combiner diagnostic IDs use EntityId hash codes.
- Legacy Graphics Tools full-screen passes and experimental Acrylic/Magnifier integrations are excluded on Unity 6000.3 and newer, whose URP removed the required compatibility APIs. Standard MRTK UI materials remain available. No custom replacement blur renderer was introduced.

Build: run `CarAR.EditorTools.CarARSetupWizard.BuildPreview` in Unity batch mode. Run the resulting executable with `--carar-smoke` to verify real XRI selection and MRTK translation/two-hand scaling, application bindings, parked transforms, panel animations, assembly restoration, and simulated light response. Physical hand gestures and compositor stability require headset verification.

## Validation on 2026-10-19

`controller-review-build.log` and `controller-review-smoke.log` record the controller implementation. The smoke suite injects Input System controller state to exercise real trigger rays, menu invocation, grip selection, MRTK two-controller scaling, sticks, floor confirmation / height adjustment and head-independent parking. Separate native MRTK and animation checks remain. UI snapshots verify Chinese labels and both panel pages.

The final build and all three smoke suites passed with exit code 0. The worn-headset run (`controller-review-device-worn.log`) reported both physical controllers through the Touch interaction profile and real trigger selections of unlock and scale buttons. The wearer confirmed visible rays and menu invocation. The app sustained approximately 72 FPS during that sampled interval. Head position changed while the parked root stayed fixed. Pimax defers controller synchronization until the headset-mounted OpenXR session is Focused; desktop window focus is independent and no longer resets XR input. Initial placement now also waits for worn, position-tracked HMD data instead of using an unworn cached pose.

The device run reported all 62 renderers inside the frustum, transparent output and explicit head-lock-off acknowledgement. VST pose/VSYNC offset getters returned no usable readback; the application left calibration unchanged. Visual alignment of the car with the real floor while walking still requires wearer verification; stable scene coordinates alone do not certify compositor calibration.

The restarted run (`controller-review-device-final.log`) verified the unworn placement gate and subsequent first placement. A 2.1 m inter-sample head displacement left the parked root position and angle unchanged. A real controller floor-placement command moved the car to a new parked position, which then remained fixed.
