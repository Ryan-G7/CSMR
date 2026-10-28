using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MixedReality.Toolkit;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Management;

namespace CarAR
{
    [DefaultExecutionOrder(-100)]
    public sealed class VRShowroom : MonoBehaviour
    {
        public XROrigin origin;
        public Transform car, environment;
        public ExplodeController explode;
        public RuntimeGlbLoader modelLoader;
        public ModelReview review;
        public ReviewSnapshot snapshot;
        public ModelAnnotations annotations;
        public ModelMeasurement measurement;
        public NavMeshData navigation;
        public Transform menu;
        public StatefulInteractable[] buttons;
        public TMP_Text status;
        public Material rayMaterial, comfortMaterial;
        public UniversalRenderPipelineAsset pipeline;
        public Vector3 entry, entryLook;
        public Vector3[] viewpoints;
        public float[] ambientCoefficients;
        public Texture2D[] sourceLightmaps;
        public Font desktopFont;
        public bool useHeadsetInEditor;
        public bool Desktop { get; private set; }
        public bool Ready { get; private set; }
        public bool Continuous { get; private set; } = true;
        public bool HighQuality { get; private set; } = true;
        public bool RecordingMode { get; private set; }
        internal bool PresentationActive;
        const float RecordingScale = .65f;
        const float StandingEyeHeight = 1.65f;
        public bool TeleportValid { get; private set; }
        public Vector3 TeleportPoint { get; private set; }
        public const int EnvironmentLayer = 8, VehicleLayer = 9;
        const int CollisionMask = (1 << EnvironmentLayer) | (1 << VehicleLayer);
        const int WalkingMask = 1 << EnvironmentLayer;
        const float Radius = .22f;
        CharacterController body;
        NavMeshDataInstance navInstance;
        UniversalRenderPipelineAsset previousPipeline;
        Material comfort;
        Transform destination;
        LineRenderer arc;
        Hand left, right;
        bool teleporting, turnReady, locomotionReady, tracking, desktopTeleport, initializedXR;
        float yaw, pitch, speed = 1.4f, gravity, vignette, nextStats;
        float trackingInvalidTime,trackingValidTime;
        float nextHeadDiagnostics;
        bool menuOpen = true;
        internal bool ReplayControls;
        internal Transform VehicleShadow { get; private set; }
        string error;
        Vector3 beforeTeleport;
        int teleportCount;
        TMP_Text[] buttonLabels;
        readonly List<InputAction> actions = new();
        sealed class Hand
        {
            public ActionBasedController controller;
            public XRRayInteractor ray;
            public XRInteractorLineVisual line;
            public InputAction stick, tracked, state, menu;
            public InputActionProperty gripPosition,gripRotation,aimPosition,aimRotation;
            public bool wasTracked,neutralReady,triggerReleased;
        }

        void Awake()
        {
            Desktop = XRLaunch.DesktopVR || Application.isEditor&&!useHeadsetInEditor || XRLaunch.HasArgument("--vr-smoke");
            Application.runInBackground = true;
            if(XRLaunch.HasArgument("--vr-smoke"))Application.logMessageReceived+=FailSmokeOnException;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Application.targetFrameRate = Desktop ? 90 : -1;
            QualitySettings.vSyncCount = 0;
            previousPipeline = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
            pipeline = Instantiate(pipeline); QualitySettings.renderPipeline = pipeline;
            // None of the showroom shaders samples the opaque scene color.
            // Avoid copying both high-resolution eyes into an unused texture.
            pipeline.supportsCameraOpaqueTexture=false;
            RecordingMode=XRLaunch.HasArgument("--vr-recording");
            ApplyQuality(true);
            // Retain the authored HDR sky, source GI, and local reflections.
            origin.Camera.clearFlags=CameraClearFlags.Skybox;
            RenderSettings.ambientMode = AmbientMode.Custom;
            var sh = new SphericalHarmonicsL2();
            if (ambientCoefficients?.Length == 27)
                for (int c=0;c<3;c++) for (int n=0;n<9;n++) sh[c,n]=ambientCoefficients[c*9+n];
            RenderSettings.ambientProbe=sh;
            if(Desktop && XRLaunch.HasArgument("--vr-walkthrough"))
            {
                // Calibrated against the supplied white-paint / warm-room reference.
                Shader.SetGlobalFloat("_CarARPresentationPaintGain",2.2f);
                Shader.SetGlobalVector("_CarARPresentationRoomGain",new Vector4(.65f,.6175f,.572f,1));
            }
            ApplySourceLightmaps();
            modelLoader=gameObject.AddComponent<RuntimeGlbLoader>();modelLoader.modelRoot=car;modelLoader.modelLayer=VehicleLayer;modelLoader.explode=explode;
            review=gameObject.AddComponent<ModelReview>();review.modelRoot=car;review.loader=modelLoader;
            annotations=gameObject.AddComponent<ModelAnnotations>();annotations.modelRoot=car;annotations.loader=modelLoader;
            annotations.viewCamera=origin.Camera;annotations.IsDesktop=Desktop;annotations.font=status?.font;annotations.AimAtModel=AimAtModel;
            snapshot=gameObject.AddComponent<ReviewSnapshot>();snapshot.sourceCamera=origin.Camera;snapshot.loader=modelLoader;snapshot.annotations=annotations;
            measurement=gameObject.AddComponent<ModelMeasurement>();measurement.modelRoot=car;measurement.loader=modelLoader;
            measurement.viewCamera=origin.Camera;measurement.IsDesktop=Desktop;measurement.AimAtModel=AimAtModel;
            modelLoader.ModelChanged+=RefreshModel;review.MeshVisibilityChanged+=RefreshModel;
            CreateVehicleShadow();
            if (navigation != null) navInstance = NavMesh.AddNavMeshData(navigation);
            body = origin.gameObject.AddComponent<CharacterController>();
            body.radius=Radius; body.height=1.7f; body.center=Vector3.up*.9f;
            body.skinWidth=.025f; body.stepOffset=.18f; body.slopeLimit=45;
            body.minMoveDistance=0;
            // Model meshes remain ray targets, but must not push the tracked viewer.
            body.excludeLayers=1 << VehicleLayer;
            origin.Camera.nearClipPlane=.02f;
            // Move only the rig; neither the environment nor the vehicle follows it.
            var overlay=GameObject.CreatePrimitive(PrimitiveType.Quad);
            overlay.name="Comfort fade and motion vignette";
            Destroy(overlay.GetComponent<Collider>());
            overlay.transform.SetParent(origin.Camera.transform,false);
            overlay.transform.localPosition=Vector3.forward*.11f;
            overlay.transform.localScale=Vector3.one*4;
            comfort=new Material(comfortMaterial);
            overlay.GetComponent<Renderer>().sharedMaterial=comfort;
            overlay.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
            CreateTeleportVisuals();
            BindButtons();
            if (Desktop) ConfigureDesktop();
            else
            {
                left=CreateHand("LeftHand"); right=CreateHand("RightHand");
                foreach (var action in actions) action.Enable();
                comfort.SetFloat("_Fade",1);
            }
        }
        public void ApplySourceLightmaps()
        {
            if(sourceLightmaps==null || sourceLightmaps.Length==0)return;
            var maps=new LightmapData[sourceLightmaps.Length];
            for(int i=0;i<maps.Length;i++)maps[i]=new LightmapData{lightmapColor=sourceLightmaps[i]};
            LightmapSettings.lightmapsMode=LightmapsMode.NonDirectional;LightmapSettings.lightmaps=maps;
            var properties=new MaterialPropertyBlock();
            foreach(var renderer in environment.GetComponentsInChildren<Renderer>())
            {
                int index=renderer.lightmapIndex;
                if(index<0 || index>=sourceLightmaps.Length)continue;
                renderer.GetPropertyBlock(properties);properties.SetTexture("_SourceLighting",sourceLightmaps[index]);
                renderer.SetPropertyBlock(properties);properties.Clear();
            }
        }

        void RefreshModel()
        {
            if(VehicleShadow!=null)Destroy(VehicleShadow.gameObject);
            CreateVehicleShadow();
        }
        bool AimAtModel(out RaycastHit hit)
        {
            int mask=1<<VehicleLayer;
            if(HandTracked(right) && Physics.Raycast(right.ray.transform.position,right.ray.transform.forward,out hit,right.ray.maxRaycastDistance,mask))return true;
            if(HandTracked(left) && Physics.Raycast(left.ray.transform.position,left.ray.transform.forward,out hit,left.ray.maxRaycastDistance,mask))return true;
            hit=default;return false;
        }
        void ToggleHandClipping()
        {
            var hand=HandTracked(right)||!HandTracked(left)?right:left;
            if(hand!=null)review.SetHandClipping(!review.HandClipping,hand.ray.transform);
        }
        void CreateVehicleShadow()
        {
            // Freeze a model copy outside the exploded hierarchy.
            // Reuse meshes and materials; these renderers only enter shadow maps.
            VehicleShadow=new GameObject("Model ground shadow").transform;
            foreach(var part in car.GetComponentsInChildren<Transform>(true))part.gameObject.layer=VehicleLayer;
            foreach(var source in car.GetComponentsInChildren<MeshRenderer>())
            {
                if(!source.enabled || !source.gameObject.activeInHierarchy)continue;
                source.shadowCastingMode=ShadowCastingMode.Off;source.receiveShadows=false;
                var filter=source.GetComponent<MeshFilter>();
                if(filter==null || filter.sharedMesh==null)continue;
                bool opaque=false;
                foreach(var material in source.sharedMaterials)
                {
                    if(material==null)continue;
                    opaque|=material.renderQueue<(int)RenderQueue.Transparent;
                    if(material.HasProperty("_ReceiveShadows"))
                    {
                        material.SetFloat("_ReceiveShadows",0);
                        material.EnableKeyword("_RECEIVE_SHADOWS_OFF");
                    }
                }
                if(!opaque)continue;
                var copy=new GameObject(source.name,typeof(MeshFilter),typeof(MeshRenderer));
                copy.layer=VehicleLayer;copy.transform.SetParent(VehicleShadow,false);
                copy.transform.SetPositionAndRotation(source.transform.position,source.transform.rotation);
                copy.transform.localScale=source.transform.lossyScale;
                copy.GetComponent<MeshFilter>().sharedMesh=filter.sharedMesh;
                var renderer=copy.GetComponent<MeshRenderer>();renderer.sharedMaterials=source.sharedMaterials;
                renderer.shadowCastingMode=ShadowCastingMode.ShadowsOnly;renderer.receiveShadows=false;
            }
        }
        IEnumerator Start()
        {
            if (!Desktop)
            {
                var manager=XRGeneralSettings.Instance?.Manager;
                if (manager == null) { StartupFailed("未配置 XR Loader。请使用桌面漫游入口。"); yield break; }
                VarjoSupport.VarjoMRBridge.PrepareLoader(manager);
                yield return manager.InitializeLoader();
                if (manager.activeLoader==null) { StartupFailed("头显启动失败。请检查 XR 运行时与设备连接；可按 F2 进入桌面漫游。"); yield break; }
                if (!VarjoSupport.VarjoMRBridge.ValidateLoader(manager.activeLoader)) { StartupFailed("Varjo 启动失败。请检查 Varjo Base 与设备连接；可按 F2 进入桌面漫游。"); yield break; }
                manager.StartSubsystems(); initializedXR=true;
                // XROrigin.Start can run before the manually started XR loader
                // exists. Apply the origin again now that input is available.
                Debug.Log($"[CarAR VR Origin] Before configuration: mode={origin.CurrentTrackingOriginMode}; height={origin.CameraInOriginSpaceHeight:F3}");
                origin.RequestedTrackingOriginMode=XROrigin.TrackingOriginMode.Floor;
                float deadline=Time.realtimeSinceStartup+20;
                yield return WaitForHeadPose(deadline);
                if (!HeadTracked()) { StartupFailed("等待头显有效定位。请佩戴头显，或按 F2 进入桌面漫游。"); yield break; }
                if (!ValidStartupEyeHeight(origin.CameraInOriginSpaceHeight) || origin.CurrentTrackingOriginMode==TrackingOriginModeFlags.Unknown)
                {
                    Debug.LogWarning($"[CarAR VR Origin] Invalid floor height={origin.CameraInOriginSpaceHeight:F3}; mode={origin.CurrentTrackingOriginMode}; falling back to device origin");
                    origin.RequestedTrackingOriginMode=XROrigin.TrackingOriginMode.Device;
                    yield return WaitForHeadPose(deadline);
                    if (!HeadTracked() || origin.CurrentTrackingOriginMode!=TrackingOriginModeFlags.Device || !CalibrateDeviceHeight())
                    { StartupFailed("头显地面定位异常。请在头显软件中重新定位；可按 F2 进入桌面漫游。"); yield break; }
                }
                Debug.Log($"[CarAR VR Origin] Configured: mode={origin.CurrentTrackingOriginMode}; raw={InputSystem.GetDevice<XRHMD>().centerEyePosition.ReadValue():F3}; height={origin.CameraInOriginSpaceHeight:F3}; offset={origin.CameraFloorOffsetObject.transform.localPosition:F3}");
                if(XRLaunch.HasArgument("--vr-stop-recenter"))
                {
                    origin.StopAllCoroutines();
                    Debug.Log("[CarAR VR Origin] Diagnostic: stopped automatic recenter retries");
                }
            }
            Ready=true; tracking=true;
            MoveRig(entry, Quaternion.LookRotation(Vector3.ProjectOnPlane(entryLook-entry,Vector3.up)).eulerAngles.y);
            menuOpen=!Desktop;PositionMenu(); menu.gameObject.SetActive(menuOpen);
            comfort.SetFloat("_Fade",0);
            Debug.Log($"[CarAR VR] Ready; desktop={Desktop}; world is fixed; navmesh={navInstance.valid}; native stereo; HDR/AgX; static GI and local reflections.");
            if(Desktop && !XRLaunch.HasArgument("--vr-smoke"))
            {
                if(XRLaunch.HasArgument("--vr-walkthrough"))gameObject.AddComponent<VRWalkthrough>();
            }
            if (XRLaunch.HasArgument("--vr-smoke")) yield return VRSmoke();
        }
        IEnumerator WaitForHeadPose(float deadline)
        {
            float stable=0;
            // Let the pose driver consume the new reference space before
            // placing the viewer. The first tracked sample can still be old.
            do
            {
                yield return null;
                stable=HeadTracked()?stable+Time.unscaledDeltaTime:0;
            } while(stable<.2f && Time.realtimeSinceStartup<deadline);
        }
        internal static bool ValidStartupEyeHeight(float height) => float.IsFinite(height) && height>=.25f && height<=2.7f;
        internal bool CalibrateDeviceHeight()
        {
            float height=origin.CameraInOriginSpaceHeight;
            if(!float.IsFinite(height))return false;
            // Correct the shared tracking parent, keeping hands aligned and
            // preserving subsequent physical crouching and head movement.
            var offset=origin.CameraFloorOffsetObject.transform;
            var calibrated=offset.localPosition+Vector3.up*(StandingEyeHeight-height);
            origin.CameraYOffset=calibrated.y;
            offset.localPosition=calibrated;
            return ValidStartupEyeHeight(origin.CameraInOriginSpaceHeight);
        }
        void StartupFailed(string message)
        {
            error=message; comfort.SetFloat("_Fade",0);
            Debug.LogError("[CarAR VR] "+message);
            // A visible desktop fallback button also works without any XR input.
        }
        void ConfigureDesktop()
        {
            Application.targetFrameRate=90;
            var driver=origin.Camera.GetComponent<TrackedPoseDriver>();
            if (driver!=null) driver.enabled=false;
            origin.enabled=false;
            origin.CameraFloorOffsetObject.transform.localPosition=Vector3.zero;
            origin.Camera.transform.SetLocalPositionAndRotation(Vector3.up*StandingEyeHeight,Quaternion.identity);
            origin.Camera.fieldOfView=55;
            origin.Camera.nearClipPlane=.06f;origin.Camera.farClipPlane=40;
        }
        Hand CreateHand(string hand)
        {
            string path="<XRController>{"+hand+"}/";
            var go=new GameObject("VR "+hand);
            go.transform.SetParent(origin.CameraFloorOffsetObject.transform,false);
            var h=new Hand {controller=go.AddComponent<ActionBasedController>()};
            h.controller.positionAction=Property(hand+" position",path+"devicePosition","Vector3");
            h.controller.rotationAction=Property(hand+" rotation",path+"deviceRotation","Quaternion");
            h.gripPosition=h.controller.positionAction;h.gripRotation=h.controller.rotationAction;
            h.aimPosition=Property(hand+" aim position",path+"pointerPosition","Vector3");
            h.aimRotation=Property(hand+" aim rotation",path+"pointerRotation","Quaternion");
            h.controller.isTrackedAction=Property(hand+" tracked",path+"isTracked","Button");
            h.controller.trackingStateAction=Property(hand+" state",path+"trackingState","Integer");
            h.tracked=h.controller.isTrackedAction.action; h.state=h.controller.trackingStateAction.action;
            h.controller.selectAction=Property(hand+" select",path+"triggerPressed","Button");
            h.controller.selectAction.action.AddBinding(path+"select");
            h.ray=go.AddComponent<XRRayInteractor>();
            h.ray.interactionLayers=1; h.ray.maxRaycastDistance=12;h.ray.enableUIInteraction=false;
            h.ray.allowAnchorControl=false; h.ray.selectActionTrigger=XRBaseControllerInteractor.InputTriggerType.StateChange;
            h.line=go.AddComponent<XRInteractorLineVisual>();
            go.GetComponent<LineRenderer>().sharedMaterial=rayMaterial;
            h.line.lineWidth=.004f;h.line.lineLength=8;h.line.smoothMovement=false;
            h.stick=Own(new InputAction(hand+" locomotion",InputActionType.Value,path+"primary2DAxis",expectedControlType:"Vector2"));
            h.menu=Own(new InputAction(hand+" menu",InputActionType.Button,path+"secondaryButton"));
            h.menu.AddBinding(path+"menuButton");
            h.menu.performed+=_=>{if (HandTracked(h) && Ready && tracking && !teleporting) ToggleMenu();};
            return h;
        }
        InputAction Own(InputAction action) { actions.Add(action); return action; }
        InputActionProperty Property(string name,string binding,string type) => new(Own(new InputAction(name,
            type=="Button"?InputActionType.Button:InputActionType.Value,binding,expectedControlType:type)));
        bool HeadTracked()
        {
            var h=InputSystem.GetDevice<XRHMD>();
            if(h==null || !h.isTracked.isPressed || (h.trackingState.ReadValue()&3)!=3 || !Finite(h.centerEyePosition.ReadValue()))return false;
            float norm=Quaternion.Dot(h.centerEyeRotation.ReadValue(),h.centerEyeRotation.ReadValue());
            return float.IsFinite(norm)&&norm>.8f&&norm<1.2f;
        }
        static bool HandTracked(Hand hand) => hand!=null && hand.tracked.IsPressed() && (hand.state.ReadValue<int>()&3)==3 && Finite(hand.controller.transform.position);
        static bool Finite(Vector3 p) => float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z);

        void Update()
        {
            if(!Desktop && Time.unscaledTime>=nextHeadDiagnostics)
            {
                nextHeadDiagnostics=Time.unscaledTime+5;
                LogHeadDiagnostics();
            }
            var keyboard=Application.isFocused?Keyboard.current:null;
            if (error!=null && keyboard?.f2Key.wasPressedThisFrame==true) EnterDesktopFallback();
            if (!Ready) return;
            if (PresentationActive) return;
            if(!TickTracking(Desktop || HeadTracked(),Time.unscaledDeltaTime))return;
            if (keyboard?.escapeKey.wasPressedThisFrame==true || keyboard?.tabKey.wasPressedThisFrame==true) ToggleMenu();
            if (keyboard?.fKey.wasPressedThisFrame==true) GoTo(0);
            if (keyboard?.f8Key.wasPressedThisFrame==true) ToggleRecordingMode();
            if (keyboard?.spaceKey.wasPressedThisFrame==true) explode.Toggle();
            if (teleporting) return;
            UpdateBody();
            if (Desktop && !ReplayControls) UpdateDesktop(keyboard); else UpdateHeadset();
            if (!teleporting)
            {
                gravity=body.isGrounded?-1:Mathf.Max(-8,gravity-9.81f*Time.deltaTime);
                body.Move(Vector3.up*(gravity*Time.deltaTime));
            }
            comfort.SetFloat("_Vignette",Mathf.MoveTowards(comfort.GetFloat("_Vignette"),vignette,Time.unscaledDeltaTime*4));
            if (Time.unscaledTime>nextStats)
            {
                nextStats=Time.unscaledTime+10;
                Debug.Log($"[CarAR VR] fps={1/Mathf.Max(.0001f,Time.unscaledDeltaTime):F0}; head={origin.Camera.transform.position:F2}; feet={Feet:F2}; origin={origin.CurrentTrackingOriginMode}; teleport={teleportCount}; mode={(Continuous?"smooth":"teleport")}; quality={HighQuality}; recording={RecordingMode}; renderScale={pipeline.renderScale:F2}; eye={XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight}");
            }
        }
        void LogHeadDiagnostics()
        {
            var head=InputSystem.GetDevice<XRHMD>();
            var driver=origin.Camera.GetComponent<TrackedPoseDriver>();
            var native=InputDevices.GetDeviceAtXRNode(XRNode.Head);
            native.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition,out var nativePosition);
            native.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation,out var nativeRotation);
            Debug.Log($"[CarAR VR Pose] device={head?.name}; tracked={head?.isTracked.isPressed}; state={head?.trackingState.ReadValue()}; input={head?.centerEyePosition.ReadValue():F3}/{head?.centerEyeRotation.ReadValue().eulerAngles:F2}; native={nativePosition:F3}/{nativeRotation.eulerAngles:F2}; camera={origin.Camera.transform.localPosition:F3}/{origin.Camera.transform.localEulerAngles:F2}; driver={driver?.isActiveAndEnabled}; actions={driver?.positionInput.action.enabled}/{driver?.rotationInput.action.enabled}/{driver?.trackingStateInput.action.enabled}; actionPose={driver?.positionInput.action.ReadValue<Vector3>():F3}/{driver?.rotationInput.action.ReadValue<Quaternion>().eulerAngles:F2}; origin={origin.CurrentTrackingOriginMode}; offset={origin.CameraFloorOffsetObject.transform.localPosition:F3}");
        }
        // Stop controls immediately, but do not flash black for a single bad
        // tracking sample. Sustained loss still fades out; recovery is re-armed.
        internal bool TickTracking(bool valid,float deltaTime)
        {
            if(!valid)
            {
                if(tracking)Debug.Log("[CarAR VR Tracking] Lost pose; locomotion suspended");
                tracking=false;trackingValidTime=0;trackingInvalidTime+=deltaTime;
                locomotionReady=turnReady=false;CancelAim();vignette=0;
                SuspendHand(left);SuspendHand(right);
                if(trackingInvalidTime>=.15f)
                    comfort.SetFloat("_Fade",Mathf.MoveTowards(comfort.GetFloat("_Fade"),1,deltaTime*10));
                return false;
            }
            if(!tracking)
            {
                trackingValidTime+=deltaTime;
                if(trackingValidTime<.12f)return false;
                Debug.Log($"[CarAR VR Tracking] Stable pose recovered; loss={trackingInvalidTime:F3}s; sticks must return to neutral");
                tracking=true;
            }
            trackingInvalidTime=trackingValidTime=0;
            if(!teleporting)comfort.SetFloat("_Fade",Mathf.MoveTowards(comfort.GetFloat("_Fade"),0,deltaTime*10));
            return true;
        }
        internal float ComfortFade => comfort.GetFloat("_Fade");
        void UpdateBody()
        {
            var local=origin.transform.InverseTransformPoint(origin.Camera.transform.position);
            body.height=Mathf.Clamp(local.y,.65f,2.3f);
            body.center=new Vector3(local.x,body.height*.5f+.04f,local.z);
        }
        void UpdateDesktop(Keyboard keyboard)
        {
            var mouse=Application.isFocused?Mouse.current:null;
            if(mouse?.rightButton.isPressed==true)
            {
                yaw+=mouse.delta.ReadValue().x*.15f;pitch=Mathf.Clamp(pitch-mouse.delta.ReadValue().y*.15f,-80,80);
                origin.transform.rotation=Quaternion.Euler(0,yaw,0);
                origin.Camera.transform.localRotation=Quaternion.Euler(pitch,0,0);
            }
            Vector2 move=Vector2.zero;
            if(keyboard!=null)
            {
                move=new Vector2((keyboard.dKey.isPressed?1:0)-(keyboard.aKey.isPressed?1:0),(keyboard.wKey.isPressed?1:0)-(keyboard.sKey.isPressed?1:0));
                if(keyboard.tKey.wasPressedThisFrame) desktopTeleport=!desktopTeleport;
            }
            MoveAlongFloor(move,keyboard?.leftShiftKey.isPressed==true?2.2f:speed);
            vignette=0;
            if(desktopTeleport && mouse!=null)
            {
                var ray=origin.Camera.ScreenPointToRay(mouse.position.ReadValue());Aim(ray.origin,ray.direction);
                if(mouse.leftButton.wasPressedThisFrame && TeleportValid) {StartCoroutine(Teleport(TeleportPoint,null)); desktopTeleport=false;}
            }
            else CancelAim();
        }
        void UpdateHeadset()
        {
            bool l=UpdateHand(left),r=UpdateHand(right);
            var a=l?left.stick.ReadValue<Vector2>():Vector2.zero;
            var b=r?right.stick.ReadValue<Vector2>():Vector2.zero;
            left.ray.enabled=l&&left.triggerReleased&&!teleporting;right.ray.enabled=r&&right.triggerReleased&&!teleporting;
            left.line.enabled=l;right.line.enabled=r&&!arc.gameObject.activeSelf;
            if(!l && !r) {locomotionReady=turnReady=false;CancelAim();vignette=0;return;}
            if(a.magnitude<.15f && b.magnitude<.15f) locomotionReady=turnReady=true;
            bool canAim=r && right.neutralReady && locomotionReady;
            // A real stick crosses intermediate values on release. Keep the
            // gesture alive until neutral instead of cancelling below 0.6.
            if(!canAim) CancelAim();
            else if(arc.gameObject.activeSelf)
            {
                if(b.magnitude<.25f)
                {
                    if(TeleportValid) StartCoroutine(Teleport(TeleportPoint,null));
                    CancelAim();
                }
                else if(b.y<-.3f || Mathf.Abs(b.x)>.7f) CancelAim();
                else Aim(right.controller.transform.position,right.controller.transform.forward);
            }
            else if(b.y>.6f && Mathf.Abs(b.x)<.55f)
            {
                // The startup panel must not make the teleport control appear
                // unresponsive. An intentional forward push starts locomotion.
                if(menuOpen){menuOpen=false;menu.gameObject.SetActive(false);}
                Aim(right.controller.transform.position,right.controller.transform.forward);
            }
            if(teleporting) return;
            if(r && right.neutralReady && turnReady && Mathf.Abs(b.x)>.7f && Mathf.Abs(b.y)<.5f)
            {
                turnReady=false;StartCoroutine(SnapTurn(Mathf.Sign(b.x)*30));
            }
            if(b.magnitude<.2f) turnReady=true;
            if(Continuous && l && left.neutralReady && locomotionReady && !arc.gameObject.activeSelf && a.magnitude>.2f)
            {
                if(menuOpen){menuOpen=false;menu.gameObject.SetActive(false);}
                MoveAlongFloor(a.normalized*((Mathf.Min(1,a.magnitude)-.2f)/.8f),speed);
            }
            else vignette=0;
        }
        bool UpdateHand(Hand hand)
        {
            bool valid=HandTracked(hand);
            if(!valid){hand.wasTracked=hand.neutralReady=hand.triggerReleased=false;return false;}
            if(!hand.wasTracked){hand.neutralReady=hand.triggerReleased=false;hand.wasTracked=true;}
            if(hand.stick.ReadValue<Vector2>().magnitude<.15f)hand.neutralReady=true;
            if(!hand.controller.selectAction.action.IsPressed())hand.triggerReleased=true;
            bool aim=hand.aimPosition.action.controls.Count>0 && hand.aimRotation.action.controls.Count>0 &&
                Finite(hand.aimPosition.action.ReadValue<Vector3>()) && Quaternion.Dot(hand.aimRotation.action.ReadValue<Quaternion>(),hand.aimRotation.action.ReadValue<Quaternion>())>.8f;
            var position=aim?hand.aimPosition:hand.gripPosition;
            var rotation=aim?hand.aimRotation:hand.gripRotation;
            if(hand.controller.positionAction.action!=position.action)hand.controller.positionAction=position;
            if(hand.controller.rotationAction.action!=rotation.action)hand.controller.rotationAction=rotation;
            hand.aimPosition.action.Enable();hand.aimRotation.action.Enable();hand.gripPosition.action.Enable();hand.gripRotation.action.Enable();
            return true;
        }
        static void SuspendHand(Hand hand)
        {
            if(hand==null)return;
            hand.wasTracked=hand.neutralReady=hand.triggerReleased=false;
            hand.ray.enabled=false;hand.line.enabled=false;
        }
        internal void BeginControllerReplay()
        {
            left=CreateHand("LeftHand");right=CreateHand("RightHand");
            foreach(var action in actions)action.Enable();ReplayControls=true;menuOpen=false;menu.gameObject.SetActive(false);
        }
        internal void EndControllerReplay()
        {
            ReplayControls=false;CancelAim();
            left.controller.gameObject.SetActive(false);right.controller.gameObject.SetActive(false);
            foreach(var action in actions)action.Disable();
        }
        internal bool PointerVisible => arc.gameObject.activeSelf;
        internal bool MenuVisible => menuOpen;
        internal int Teleports => teleportCount;
        internal void MoveAlongFloor(Vector2 axis,float metresPerSecond)
        {
            if(axis.sqrMagnitude<.001f || teleporting) {vignette=0;return;}
            var forward=Vector3.ProjectOnPlane(origin.Camera.transform.forward,Vector3.up).normalized;
            if(forward.sqrMagnitude<.1f)forward=origin.transform.forward;
            var delta=(forward*axis.y+Vector3.Cross(Vector3.up,forward)*axis.x)*metresPerSecond*Time.deltaTime;
            if(delta.magnitude>metresPerSecond*Time.deltaTime)delta=delta.normalized*metresPerSecond*Time.deltaTime;
            var feet=Feet;
            if(NavMesh.SamplePosition(feet,out var start,.45f,NavMesh.AllAreas))
            {
                var target=start.position+delta;
                if(NavMesh.Raycast(start.position,target,out var boundary,NavMesh.AllAreas))delta=Vector3.ProjectOnPlane(boundary.position-start.position,Vector3.up)*.85f;
                body.Move(delta);
            }
            vignette=Desktop?0:.82f;
        }
        // Use the same floor navigation and capsule movement as desktop WASD.
        internal void WalkForPresentation(Vector3 direction,float metresPerSecond)
        {
            UpdateBody();
            var forward=Vector3.ProjectOnPlane(origin.Camera.transform.forward,Vector3.up).normalized;
            var right=Vector3.Cross(Vector3.up,forward);
            MoveAlongFloor(new Vector2(Vector3.Dot(direction,right),Vector3.Dot(direction,forward)),metresPerSecond);
            gravity=body.isGrounded?-1:Mathf.Max(-8,gravity-9.81f*Time.deltaTime);
            body.Move(Vector3.up*(gravity*Time.deltaTime));
        }
        internal Vector3 Feet => new(origin.Camera.transform.position.x,origin.transform.position.y,origin.Camera.transform.position.z);
        internal bool ValidateDestination(Vector3 point,out Vector3 result)
        {
            result=point;
            if(!Finite(point) || Vector3.Distance(Feet,point)>14 || !NavMesh.SamplePosition(point,out var hit,.3f,NavMesh.AllAreas))return false;
            var route=new NavMeshPath();
            if(!NavMesh.CalculatePath(Feet,hit.position,NavMesh.AllAreas,route) || route.status!=NavMeshPathStatus.PathComplete)return false;
            float height=Mathf.Max(1.15f,body.height);
            if(Physics.CheckCapsule(hit.position+Vector3.up*(Radius+.06f),hit.position+Vector3.up*(height-Radius),Radius,WalkingMask,QueryTriggerInteraction.Ignore))return false;
            result=hit.position;return true;
        }
        void CreateTeleportVisuals()
        {
            var go=new GameObject("Teleport parabolic pointer");arc=go.AddComponent<LineRenderer>();
            arc.sharedMaterial=rayMaterial;arc.widthMultiplier=.012f;arc.positionCount=33;
            arc.shadowCastingMode=ShadowCastingMode.Off;go.SetActive(false);
            var ring=GameObject.CreatePrimitive(PrimitiveType.Cylinder);ring.name="Teleport landing marker";
            Destroy(ring.GetComponent<Collider>());ring.transform.localScale=new Vector3(.44f,.004f,.44f);
            ring.GetComponent<Renderer>().sharedMaterial=new Material(rayMaterial);destination=ring.transform;ring.SetActive(false);
        }
        internal void Aim(Vector3 position,Vector3 direction)
        {
            TeleportValid=false;arc.gameObject.SetActive(true);destination.gameObject.SetActive(false);
            arc.positionCount=33;
            var previous=position;var velocity=direction.normalized*7+Vector3.up*1.2f;int count=33;
            arc.SetPosition(0,previous);
            for(int i=1;i<33;i++)
            {
                float t=i*.065f;var next=position+velocity*t+Vector3.down*(4.9f*t*t);
                if(Physics.Linecast(previous,next,out var hit,CollisionMask,QueryTriggerInteraction.Ignore))
                {
                    arc.SetPosition(i,hit.point);count=i+1;
                    TeleportValid=false;
                    if(Vector3.Dot(hit.normal,Vector3.up)>.75f && ValidateDestination(hit.point,out var landing))
                    {
                        TeleportValid=true;TeleportPoint=landing;
                        destination.position=landing+Vector3.up*.012f;destination.gameObject.SetActive(true);
                    }
                    break;
                }
                arc.SetPosition(i,next);previous=next;
            }
            arc.positionCount=count;
            var color=TeleportValid?new Color(.12f,.9f,.65f):new Color(.95f,.27f,.24f);
            arc.startColor=arc.endColor=color;
        }
        void CancelAim() {arc.gameObject.SetActive(false);destination.gameObject.SetActive(false);TeleportValid=false;}
        internal IEnumerator Teleport(Vector3 point,float? heading)
        {
            if(teleporting || !ValidateDestination(point,out var landing))yield break;
            teleporting=true;beforeTeleport=Feet;CancelAim();
            yield return Fade(1,.1f);
            if(Desktop || HeadTracked()) {MoveRig(landing,heading);teleportCount++;}
            yield return Fade(0,.13f);teleporting=false;locomotionReady=turnReady=false;
        }
        IEnumerator SnapTurn(float angle)
        {
            teleporting=true;yield return Fade(1,.075f);
            if(Desktop || HeadTracked())origin.RotateAroundCameraUsingOriginUp(angle);
            yield return Fade(0,.1f);teleporting=false;
        }
        IEnumerator Fade(float target,float seconds)
        {
            float start=comfort.GetFloat("_Fade"),elapsed=0;
            while(elapsed<seconds){elapsed+=Time.unscaledDeltaTime;comfort.SetFloat("_Fade",Mathf.Lerp(start,target,elapsed/seconds));yield return null;}
            comfort.SetFloat("_Fade",target==0&&trackingInvalidTime>=.15f?1:target);
        }
        void MoveRig(Vector3 floor,float? heading)
        {
            body.enabled=false;
            if(heading.HasValue)
            {
                float delta=Mathf.DeltaAngle(origin.Camera.transform.eulerAngles.y,heading.Value);
                origin.RotateAroundCameraUsingOriginUp(delta);yaw=origin.transform.eulerAngles.y;
                if(Desktop)
                {
                    float distance=Vector3.ProjectOnPlane(entryLook-floor,Vector3.up).magnitude;
                    pitch=Mathf.Atan2(floor.y+StandingEyeHeight-entryLook.y,Mathf.Max(.1f,distance))*Mathf.Rad2Deg;
                    origin.Camera.transform.localRotation=Quaternion.Euler(pitch,0,0);
                }
            }
            var offset=origin.Camera.transform.position-origin.transform.position;
            origin.transform.position=floor-new Vector3(offset.x,0,offset.z);
            gravity=0;body.enabled=true;UpdateBody();Physics.SyncTransforms();
        }
        void BindButtons()
        {
            buttonLabels=new TMP_Text[buttons.Length];
            for(int i=0;i<buttons.Length;i++)
                foreach(var text in buttons[i].GetComponentsInChildren<TMP_Text>(true))
                    if(text.name=="Text"){buttonLabels[i]=text;break;}
            buttons[0].OnClicked.AddListener(()=>{Continuous=!Continuous;locomotionReady=false;});
            buttons[1].OnClicked.AddListener(()=>speed=speed<1.3f?1.4f:.9f);
            for(int i=0;i<5;i++){int index=i;buttons[i+2].OnClicked.AddListener(()=>GoTo(index));}
            buttons[7].OnClicked.AddListener(modelLoader.PickFile);
            buttons[8].OnClicked.AddListener(()=>review.SetClipping(!review.Clipping));
            buttons[9].OnClicked.AddListener(()=>review.SelectMaterial(-1));
            buttons[10].OnClicked.AddListener(()=>review.SelectMaterial(1));
            buttons[11].OnClicked.AddListener(explode.Toggle);
            buttons[12].OnClicked.AddListener(ToggleQuality);
            buttons[13].OnClicked.AddListener(ToggleMenu);
            buttons[14].OnClicked.AddListener(()=>snapshot.Capture());
            buttons[15].OnClicked.AddListener(ToggleRecordingMode);
            buttons[16].OnClicked.AddListener(()=>{var color=ColorValue();review.SetColor(color);});
            buttons[17].OnClicked.AddListener(()=>review.SetMetallic(review.Metallic+.1f));
            buttons[18].OnClicked.AddListener(()=>review.SetMetallic(review.Metallic-.1f));
            buttons[19].OnClicked.AddListener(()=>review.SetRoughness(review.Roughness+.1f));
            buttons[20].OnClicked.AddListener(()=>review.SetRoughness(review.Roughness-.1f));
            buttons[21].OnClicked.AddListener(()=>review.SetOpacity(review.ColorValue.a+.1f));
            buttons[22].OnClicked.AddListener(()=>review.SetOpacity(review.ColorValue.a-.1f));
            buttons[23].OnClicked.AddListener(()=>{annotations.BeginPlacement();menuOpen=false;menu.gameObject.SetActive(false);});
            buttons[24].OnClicked.AddListener(annotations.RemoveLast);
            buttons[25].OnClicked.AddListener(()=>review.SetPlaneAxis(Vector3.right));
            buttons[26].OnClicked.AddListener(()=>review.MovePlane(-.1f));
            buttons[27].OnClicked.AddListener(()=>review.MovePlane(.1f));
            buttons[28].OnClicked.AddListener(()=>BeginMeasurement(ModelMeasurement.MeasurementMode.Distance));
            buttons[29].OnClicked.AddListener(()=>BeginMeasurement(ModelMeasurement.MeasurementMode.Curve));
            buttons[30].OnClicked.AddListener(()=>BeginMeasurement(ModelMeasurement.MeasurementMode.Surface));
            buttons[31].OnClicked.AddListener(measurement.Finish);
            buttons[32].OnClicked.AddListener(measurement.Clear);
            buttons[33].OnClicked.AddListener(()=>review.SelectMesh(-1));
            buttons[34].OnClicked.AddListener(()=>review.SelectMesh(1));
            buttons[35].OnClicked.AddListener(review.ToggleSelectedMesh);
            buttons[36].OnClicked.AddListener(review.ShowAllMeshes);
            buttons[37].OnClicked.AddListener(()=>{annotations.BeginSnapshotAnnotation(snapshot);menuOpen=false;menu.gameObject.SetActive(false);});
            buttons[38].OnClicked.AddListener(ToggleHandClipping);
        }
        void BeginMeasurement(ModelMeasurement.MeasurementMode mode)
        {
            measurement.Begin(mode);menuOpen=false;menu.gameObject.SetActive(false);
        }
        Color ColorValue()
        {
            Color[] colors={new(.8f,.04f,.03f),new(.9f,.9f,.9f),new(.03f,.25f,.8f),new(.08f,.55f,.25f)};
            var current=review.ColorValue;
            int closest=0;float distance=float.MaxValue;
            for(int i=0;i<colors.Length;i++){float d=Vector3.Distance(new Vector3(current.r,current.g,current.b),new Vector3(colors[i].r,colors[i].g,colors[i].b));if(d<distance){distance=d;closest=i;}}
            var next=colors[(closest+1)%colors.Length];next.a=current.a;return next;
        }
        public void GoTo(int index)
        {
            Vector3 p=index>0&&index<=viewpoints.Length?viewpoints[index-1]:entry;
            for(int ring=0;ring<=5;ring++)for(int sample=0;sample<(ring==0?1:16);sample++)
            {
                float angle=sample*Mathf.PI/8;
                var candidate=p+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*(ring*.3f);
                if(!NavMesh.SamplePosition(candidate,out var hit,.35f,NavMesh.AllAreas) || !ValidateDestination(hit.position,out var landing))continue;
                float heading=Quaternion.LookRotation(Vector3.ProjectOnPlane(entryLook-landing,Vector3.up)).eulerAngles.y;
                StartCoroutine(Teleport(landing,heading));return;
            }
            Debug.LogWarning($"[CarAR VR] Viewpoint {index} blocked; requested={p}; feet={Feet}");
        }
        public void ToggleQuality()
        {
            ApplyQuality(!HighQuality);
        }
        void ApplyQuality(bool high)
        {
            HighQuality=high;pipeline.renderScale=(HighQuality?1:.8f)*(RecordingMode?RecordingScale:1);
            // Keep the original presentation's shading/post-processing path.
            // Supersampling plus MSAA improves edges without changing its look.
            if(Desktop && XRLaunch.HasArgument("--vr-walkthrough"))
            {
                pipeline.renderScale=HighQuality?2f:1f;
                pipeline.msaaSampleCount=8;
                var cameraData=origin.Camera.GetUniversalAdditionalCameraData();
                cameraData.renderPostProcessing=false;
                cameraData.antialiasing=AntialiasingMode.None;
                origin.Camera.allowMSAA=true;
                origin.Camera.allowDynamicResolution=false;
                QualitySettings.anisotropicFiltering=AnisotropicFiltering.ForceEnable;
            }
            pipeline.mainLightShadowmapResolution=HighQuality?4096:2048;
        }
        public void ToggleRecordingMode()
        {
            RecordingMode=!RecordingMode;ApplyQuality(HighQuality);
            Debug.Log($"[CarAR VR Recording] enabled={RecordingMode}; renderScale={pipeline.renderScale:F2}; highMaterialsAndLighting={HighQuality}; headset cadence remains XR-controlled");
        }
        public void ToggleMenu()
        {
            menuOpen=!menuOpen;CancelAim();locomotionReady=false;
            menu.gameObject.SetActive(menuOpen&&!Desktop);if(menuOpen)PositionMenu();
        }
        void PositionMenu()
        {
            var forward=Vector3.ProjectOnPlane(origin.Camera.transform.forward,Vector3.up).normalized;
            if(forward.sqrMagnitude<.1f)forward=origin.transform.forward;
            menu.SetPositionAndRotation(origin.Camera.transform.position+forward*1.6f-Vector3.up*.08f,Quaternion.LookRotation(forward));
        }
        void LateUpdate()
        {
            string instructions=error??(Desktop?"GLB 模型 · 沉浸评审\nWASD 移动 · 右键环视 · T 指向地面后单击传送\nTab 面板 · F 返回入口 · 空格 爆炸 / 还原":
                "GLB 模型 · 沉浸评审\n左摇杆移动 · 右摇杆前推松开传送 / 左右转向 30°\n菜单 / Y / B：剖切、材质、快照与标注 · 扳机点选");
            if(annotations.AwaitingPlacement)instructions+="\n请用射线在模型上选点，再输入标注文字";
            if(measurement.AwaitingPoint)instructions+="\n测量中：请用射线在模型上选点";
            instructions+="\n"+measurement.Status;
            if(status.text!=instructions)status.text=instructions;
            SetLabel(0,Continuous?"移动：连续 + 传送":"移动：舒适传送");
            SetLabel(1,speed<1.3f?"步速：缓慢":"步速：正常");
            SetLabel(8,review.Clipping?"关闭剖切":"开启剖切");
            SetLabel(11,explode.IsExploded?"组装还原":"模型爆炸");
            SetLabel(12,HighQuality?"画质：精细":"画质：流畅");
            SetLabel(5,"右后视角");
            SetLabel(4,"右侧视角");
            SetLabel(15,RecordingMode?"录屏优化：开启":"录屏优化：关闭");
            SetLabel(35,review.SelectedMesh!=null&&review.SelectedMesh.enabled?"隐藏选中网格":"显示选中网格");
            SetLabel(38,review.HandClipping?"取消手柄剖面":"手柄跟随剖面");
        }
        void SetLabel(int index,string value)
        {
            var text=buttonLabels[index];if(text!=null && text.text!=value)text.text=value;
        }
        void OnGUI()
        {
            if(PresentationActive)return;
            if(XRLaunch.HasArgument("--vr-smoke"))return;
            if(!Desktop && error==null)return;
            if(desktopFont!=null)GUI.skin.font=desktopFont;
            var style=new GUIStyle(GUI.skin.button){fontSize=17};
            if(error!=null)
            {
                GUI.Box(new Rect(20,20,760,100),error);
                if(GUI.Button(new Rect(35,72,430,36),"[F2] 进入桌面漫游",style))EnterDesktopFallback();
                return;
            }
            GUI.Box(new Rect(18,18,610,70),"GLB 模型 · 场景评审\nWASD 移动 / 右键环视 / Tab 面板 / T 传送");
            if(!menuOpen)return;
            string[] labels={Continuous?"移动：连续 + 传送":"移动：舒适传送",speed<1.3f?"步速：缓慢":"步速：正常","返回入口 [F]","车头视角","右侧视角","右后视角","车尾视角","导入 GLB",review.Clipping?"关闭剖切":"开启剖切","上一材质","下一材质","爆炸 / 还原 [空格]",HighQuality?"画质：精细":"画质：流畅","收起面板 [Tab]","保存快照",RecordingMode?"录屏优化：开启 [F8]":"录屏优化：关闭 [F8]","切换颜色","金属度 +","金属度 -","粗糙度 +","粗糙度 -","透明度 +","透明度 -","添加标注","撤销标注","剖面 X","剖面 -10cm","剖面 +10cm","两点距离","曲线测量","曲面面积","完成曲线","清除测量","上一网格","下一网格","显示 / 隐藏网格","全部显示","注释当前快照","手柄跟随剖面"};
            for(int i=0;i<labels.Length;i++)if(GUI.Button(new Rect(18+(i%3)*205,100+(i/3)*43,198,38),labels[i],style))buttons[i].OnClicked.Invoke();
            GUI.Label(new Rect(18,675,610,90),$"{modelLoader.Status}\n{review.SelectedName} · {review.SelectedMeshName}\n{measurement.Status} · 标注 {annotations.Count} 个");
        }
        void EnterDesktopFallback()
        {
            StopXR();foreach(var a in actions)a.Disable();
            if(left!=null)left.controller.gameObject.SetActive(false);if(right!=null)right.controller.gameObject.SetActive(false);
            Desktop=true;annotations.IsDesktop=true;Ready=tracking=true;error=null;ConfigureDesktop();
            MoveRig(entry,Quaternion.LookRotation(Vector3.ProjectOnPlane(entryLook-entry,Vector3.up)).eulerAngles.y);
            menuOpen=false;menu.gameObject.SetActive(false);
            PositionMenu();comfort.SetFloat("_Fade",0);
        }
        void StopXR()
        {
            var manager=XRGeneralSettings.Instance?.Manager;
            if(manager!=null && manager.isInitializationComplete){if(initializedXR)manager.StopSubsystems();manager.DeinitializeLoader();}
            initializedXR=false;
        }
        void OnDestroy()
        {
            Application.logMessageReceived-=FailSmokeOnException;
            foreach(var a in actions)a.Dispose();StopXR();
            if(navInstance.valid)navInstance.Remove();
            QualitySettings.renderPipeline=previousPipeline;
            if(review!=null)review.MeshVisibilityChanged-=RefreshModel;
            if(comfort!=null)Destroy(comfort);
            if(pipeline!=null)Destroy(pipeline);
            if(VehicleShadow!=null)Destroy(VehicleShadow.gameObject);
            Shader.SetGlobalFloat("_CarARPresentationPaintGain",1);
            Shader.SetGlobalVector("_CarARPresentationRoomGain",Vector4.zero);
        }
        static void FailSmokeOnException(string message,string stack,LogType type)
        {if(type==LogType.Exception)Application.Quit(1);}

        IEnumerator VRSmoke()
        {
            yield return new WaitForSeconds(2);
            yield return new WaitForEndOfFrame();
            var frame=ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(Application.dataPath,"../vr-gameplay.png"),frame.EncodeToPNG());Destroy(frame);
            yield return null;
            try
            {
                if(!navInstance.valid || !Ready || !Desktop)throw new Exception("VR desktop/navmesh startup failed");
                if(environment.GetComponentsInChildren<Renderer>().Length<10 || car.GetComponentsInChildren<Renderer>().Length==0)throw new Exception("Missing source environment or model");
                if(RenderSettings.customReflectionTexture==null || LightmapSettings.lightmaps.Length==0)throw new Exception("Missing reflection / baked indirect lighting");
                foreach(var probe in FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None))
                    if(probe.texture==null)throw new Exception("Missing packaged local reflection: "+probe.name);
                if(!pipeline.supportsHDR || !origin.Camera.allowHDR || !pipeline.supportsSoftShadows)throw new Exception("VR HDR / soft shadows are disabled");
                float startupScale=XRLaunch.HasArgument("--vr-recording")?RecordingScale:1;
                if(!HighQuality || !Mathf.Approximately(pipeline.renderScale,startupScale) || pipeline.mainLightShadowmapResolution!=4096)throw new Exception("VR did not start at the requested high quality / recording preset");
                bool wasRecording=RecordingMode;
                if(buttons.Length!=39)throw new Exception("VR review buttons are missing");
                buttons[15].OnClicked.Invoke();
                if(RecordingMode==wasRecording || !HighQuality || !Mathf.Approximately(pipeline.renderScale,wasRecording?1:RecordingScale))throw new Exception("Recording button did not switch render resolution independently of high quality");
                ToggleQuality();
                if(HighQuality || !Mathf.Approximately(pipeline.renderScale,.8f*(RecordingMode?RecordingScale:1)))throw new Exception("Quality toggle ignored recording resolution");
                ToggleQuality();buttons[15].OnClicked.Invoke();
                if(!HighQuality || RecordingMode!=wasRecording || !Mathf.Approximately(pipeline.renderScale,startupScale) || pipeline.supportsCameraOpaqueTexture)throw new Exception("Recording preset did not restore the requested state or retained unused opaque copy");
                Debug.Log($"[CarAR VR Recording Smoke] PASS: startup recording={RecordingMode}; button and quality combination restore requested resolution; unused opaque copy disabled");
                VerifyVehicleShadows();
                if(ValidateDestination(new Vector3(float.NaN,0,0),out _) || ValidateDestination(Feet+Vector3.up*8,out _))throw new Exception("Unsafe teleport accepted");
                Capture("vr-entrance.png");
                if(XRLaunch.HasArgument("--vr-material-diagnostics"))CaptureMaterialDiagnostics();
                var start=Feet;
                if(!NavMesh.SamplePosition(start+Vector3.left*.7f,out var target,.4f,NavMesh.AllAreas)||!ValidateDestination(target.position,out _))throw new Exception("Reachable floor rejected");
                Debug.Log("[CarAR VR Smoke] Destination validation passed");
            }
            catch(Exception e){Debug.LogException(e);Application.Quit(1);yield break;}
            var scenePose=environment.localToWorldMatrix;var vehiclePose=car.localToWorldMatrix;
            NavMesh.SamplePosition(Feet+Vector3.left*.7f,out var landing,.4f,NavMesh.AllAreas);
            yield return Teleport(landing.position,null);
            if(teleportCount!=1 || Vector3.Distance(Feet,beforeTeleport)<.4f || environment.localToWorldMatrix!=scenePose || car.localToWorldMatrix!=vehiclePose)
            {Debug.LogError("[CarAR VR Smoke] Teleport moved the world or failed");Application.Quit(1);yield break;}
            review.SetClipping(true);yield return new WaitForSeconds(.2f);Capture("vr-clipped.png");review.SetClipping(false);
            explode.SetTarget(1);yield return new WaitForSeconds(2.5f);Capture("vr-exploded.png");
            explode.SetTarget(0);yield return new WaitForSeconds(2.2f);
            if(explode.Progress>.001f){Application.Quit(1);yield break;}
            for(int i=1;i<5;i++)
            {
                int previous=teleportCount;GoTo(i);yield return new WaitForSeconds(.7f);
                if(teleportCount!=previous+1)throw new Exception("VR viewpoint "+i+" has no reachable landing");
                Capture("vr-view-"+i+".png");
            }
            ToggleQuality();if(!Mathf.Approximately(pipeline.renderScale,.8f*(RecordingMode?RecordingScale:1))){Application.Quit(1);yield break;}ToggleQuality();
            if(!HighQuality || !Mathf.Approximately(pipeline.renderScale,RecordingMode?RecordingScale:1) || pipeline.mainLightShadowmapResolution!=4096)throw new Exception("High quality preset was not restored");
            GoTo(0);yield return new WaitForSeconds(.5f);
            yield return VRControllerSmoke.Run(this);
            Debug.Log("[CarAR VR Smoke] PASS: CarRoom-2, source color transform, HDR, baked GI, reflections, valid/invalid teleport, fixed world/model, clipping, generic explode/assemble and quality presets.");
            Application.Quit(0);
        }
        void CaptureMaterialDiagnostics()
        {
            var renderers=environment.GetComponentsInChildren<Renderer>();
            var materials=new HashSet<Material>();foreach(var r in renderers)foreach(var m in r.sharedMaterials)materials.Add(m);
            foreach(var m in materials){m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");m.SetFloat("_EnvironmentReflections",0);}
            Capture("vr-diffuse-only.png");
            foreach(var m in materials){m.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");m.SetFloat("_EnvironmentReflections",1);}
            var bumps=new Dictionary<Material,float>();foreach(var m in materials){bumps[m]=m.GetFloat("_BumpScale");m.SetFloat("_BumpScale",0);}
            Capture("vr-no-normal-maps.png");foreach(var item in bumps)item.Key.SetFloat("_BumpScale",item.Value);
            var indices=new int[renderers.Length];var usage=new LightProbeUsage[renderers.Length];
            for(int i=0;i<renderers.Length;i++){indices[i]=renderers[i].lightmapIndex;usage[i]=renderers[i].lightProbeUsage;renderers[i].lightmapIndex=-1;renderers[i].lightProbeUsage=LightProbeUsage.Off;}
            var ambient=RenderSettings.ambientProbe;RenderSettings.ambientProbe=new SphericalHarmonicsL2();
            Capture("vr-reflections-only.png");
            RenderSettings.ambientProbe=ambient;
            for(int i=0;i<renderers.Length;i++){renderers[i].lightmapIndex=indices[i];renderers[i].lightProbeUsage=usage[i];}
            foreach(var m in materials)Debug.Log($"[VR Material] {m.name}; shader={m.shader.name}; emission={m.GetColor("_EmissionColor")}; keywords={string.Join(",",m.shaderKeywords)}");
        }
        void VerifyVehicleShadows()
        {
            var light=RenderSettings.sun;
            if(light==null || light.intensity<=0 || light.shadows==LightShadows.None)throw new Exception("VR vehicle shadows have no active light");
            VerifyFixedVehicleShadow();
            // Stay inside the room and see the car and its floor receiver.
            var position=entry+Vector3.up*2.8f;
            var rotation=Quaternion.LookRotation(entryLook-position);
            var withShadows=Capture("vr-shadows-on.png",position,rotation);
            var shadows=light.shadows;Color32[] withoutShadows;
            try {light.shadows=LightShadows.None;withoutShadows=Capture("vr-shadows-off.png",position,rotation);}
            finally {light.shadows=shadows;}
            int darkened=0;
            for(int i=0;i<withShadows.Length;i++)
            {
                float before=withoutShadows[i].r+withoutShadows[i].g+withoutShadows[i].b;
                float after=withShadows[i].r+withShadows[i].g+withShadows[i].b;
                if(i/1280<400 && before>45 && after<before*.93f)darkened++;
            }
            if(darkened<150)throw new Exception("Vehicle shadows did not darken baked receivers: "+darkened+" pixels");
            // The ground shadow must not introduce dark patches on model surfaces.
            var inspectionEye=car.TransformPoint(ModelBounds.Local(car).center);
            var cabinRotation=car.rotation*Quaternion.Euler(8,0,0);
            var cabinWith=Capture("vr-cabin-shadow-on.png",inspectionEye,cabinRotation);
            Color32[] cabinWithout;
            try {VehicleShadow.gameObject.SetActive(false);cabinWithout=Capture("vr-cabin-shadow-off.png",inspectionEye,cabinRotation);}
            finally {VehicleShadow.gameObject.SetActive(true);}
            int cabinChanged=0;
            for(int y=150;y<400;y++)for(int x=360;x<920;x++)
            {
                int i=y*1280+x;
                if(Mathf.Abs(cabinWith[i].r-cabinWithout[i].r)+Mathf.Abs(cabinWith[i].g-cabinWithout[i].g)+Mathf.Abs(cabinWith[i].b-cabinWithout[i].b)>6)cabinChanged++;
            }
            if(cabinChanged>100)throw new Exception("Ground shadow still darkens cabin surfaces: "+cabinChanged+" pixels");
            Debug.Log($"[CarAR VR Shadow Smoke] PASS: fixed closed-car ground shadow; floor pixels={darkened}; cabin changed pixels={cabinChanged}; vehicle self shadows disabled");
        }
        void VerifyFixedVehicleShadow()
        {
            if(VehicleShadow==null || VehicleShadow.IsChildOf(car) || VehicleShadow.childCount==0)
                throw new Exception("Closed vehicle shadow is missing or follows the animated vehicle");
            foreach(var renderer in car.GetComponentsInChildren<Renderer>())
            {
                if(renderer.shadowCastingMode!=ShadowCastingMode.Off || renderer.receiveShadows)
                    throw new Exception("Vehicle part still casts or receives shadows: "+renderer.name);
                foreach(var material in renderer.sharedMaterials)
                    if(material.HasProperty("_ReceiveShadows") && (material.GetFloat("_ReceiveShadows")!=0 || !material.IsKeywordEnabled("_RECEIVE_SHADOWS_OFF")))
                        throw new Exception("Vehicle material still receives shadows: "+material.name);
            }
            foreach(var renderer in VehicleShadow.GetComponentsInChildren<Renderer>())
                if(renderer.shadowCastingMode!=ShadowCastingMode.ShadowsOnly || renderer.receiveShadows)
                    throw new Exception("Vehicle shadow copy renders visible geometry or receives shadows");
        }
        internal Color32[] Capture(string name,Vector3? position=null,Quaternion? rotation=null)
        {
            var go=new GameObject("VR validation camera");var cam=go.AddComponent<Camera>();
            cam.CopyFrom(origin.Camera);cam.enabled=false;
            cam.transform.SetPositionAndRotation(position ?? origin.Camera.transform.position,rotation ?? origin.Camera.transform.rotation);
            var data=go.AddComponent<UniversalAdditionalCameraData>();data.allowXRRendering=false;data.renderPostProcessing=false;
            var rt=RenderTexture.GetTemporary(1280,800,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear,4);
            cam.targetTexture=rt;var previous=RenderTexture.active;cam.Render();RenderTexture.active=rt;
            var texture=new Texture2D(1280,800,TextureFormat.RGB24,false,true);
            texture.ReadPixels(new Rect(0,0,1280,800),0,0);texture.Apply();
            // A linear offscreen target needs sRGB encoding for a PNG viewer.
            var colors=texture.GetPixels();for(int i=0;i<colors.Length;i++)colors[i]=colors[i].gamma;texture.SetPixels(colors);texture.Apply();
            File.WriteAllBytes(Path.Combine(Application.dataPath,"../"+name),texture.EncodeToPNG());
            var pixels=texture.GetPixels32();
            RenderTexture.active=previous;cam.targetTexture=null;RenderTexture.ReleaseTemporary(rt);Destroy(texture);Destroy(go);
            return pixels;
        }
    }
}
