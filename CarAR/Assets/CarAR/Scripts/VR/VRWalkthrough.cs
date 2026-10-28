using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace CarAR
{
    // A visitor walking the existing showroom floor, stopping to look and
    // interact. No camera cuts, orbit camera, zoom, or viewpoint teleport.
    [DefaultExecutionOrder(100)]
    public sealed class VRWalkthrough : MonoBehaviour
    {
        VRShowroom app;
        Camera view;
        Vector3 savedRigPosition,savedCameraPosition;
        Quaternion savedRigRotation,savedCameraRotation;
        float savedFov,eyeHeight=1.65f,targetEye=1.65f,headYaw,headPitch,yawVelocity,pitchVelocity;
        float walkSpeed,walkDistance,startedAt;
        int savedFrameRate;
        bool savedCursor,playing,paused,prepared;
        CursorLockMode savedLock;
        Vector3 lookPoint,travelDirection;
        Matrix4x4 carPose,roomPose;
        Coroutine routine;
        DateTime startedUtc;
        float nextCapture;
        int stageIndex;
        bool detailReviewed;
        VRWalkthroughRecorder recorder;

        [Serializable] sealed class Take
        {
            public string startedUtc,completedUtc;
            public float durationSeconds,walkedMetres;
            public bool continuousFirstPerson,fixedWorld,assembled,detailReviewed;
        }

        void Start()
        {
            app=GetComponent<VRShowroom>();view=app.origin.Camera;
            Prepare();
            if(XRLaunch.HasArgument("--vr-presentation-autoplay"))routine=StartCoroutine(AutoPlay());
        }

        void Prepare()
        {
            if(prepared)return;
            savedRigPosition=app.origin.transform.position;savedRigRotation=app.origin.transform.rotation;
            savedCameraPosition=view.transform.localPosition;savedCameraRotation=view.transform.localRotation;
            savedFov=view.fieldOfView;savedFrameRate=Application.targetFrameRate;
            savedCursor=Cursor.visible;savedLock=Cursor.lockState;
            carPose=app.car.localToWorldMatrix;roomPose=app.environment.localToWorldMatrix;
            if(app.MenuVisible)app.ToggleMenu();
            app.PresentationActive=true;view.fieldOfView=60;Application.targetFrameRate=60;
            Cursor.visible=false;Cursor.lockState=CursorLockMode.Confined;
            headYaw=app.origin.transform.eulerAngles.y;headPitch=view.transform.localEulerAngles.x;
            if(headPitch>180)headPitch-=360;
            lookPoint=app.entryLook;prepared=true;
            Debug.Log($"[CarAR Walkthrough Quality] output={Screen.width}x{Screen.height}; renderScale={app.pipeline.renderScale}; MSAA={app.pipeline.msaaSampleCount}; AA={view.GetUniversalAdditionalCameraData().antialiasing}; backdrop={view.clearFlags}; directExport={XRLaunch.HasArgument("--vr-walkthrough-export")}");
        }

        IEnumerator AutoPlay()
        {
            yield return new WaitForSecondsRealtime(XRLaunch.HasArgument("--vr-walkthrough-check")?2:15);
            yield return RunWalkthrough();
        }

        void Update()
        {
            var keyboard=Application.isFocused?Keyboard.current:null;
            if(keyboard?.escapeKey.wasPressedThisFrame==true){Restore();return;}
            if(keyboard?.f7Key.wasPressedThisFrame==true)paused=!paused;
            if(keyboard?.f6Key.wasPressedThisFrame==true && !playing)
            {
                Prepare();
                if(routine!=null)StopCoroutine(routine);
                routine=StartCoroutine(RunWalkthrough());
            }
        }

        IEnumerator RunWalkthrough()
        {
            // Unity does not propagate exceptions from nested coroutines to
            // their caller. Drive the route here so every failed stage restores
            // the controls and closes the encoder, including startup failures.
            var stages=new Stack<IEnumerator>();stages.Push(Walkthrough());
            Exception failure=null;
            while(stages.Count>0)
            {
                object next=null;bool advanced=false;
                try
                {
                    advanced=stages.Peek().MoveNext();
                    if(advanced)next=stages.Peek().Current;
                }
                catch(Exception error){failure=error;break;}
                if(!advanced){stages.Pop();continue;}
                if(next is IEnumerator nested)stages.Push(nested);
                else yield return next;
            }
            if(failure==null)yield break;
            Debug.LogException(failure);
            routine=null;Restore();
            if(XRLaunch.HasArgument("--vr-walkthrough-check"))Application.Quit(1);
        }

        Vector3 World(float x,float z)
        {
            var p=app.car.TransformPoint(new Vector3(x,0,z));p.y=app.entry.y;return p;
        }
        Vector3 Look(float x,float y,float z)=>app.car.TransformPoint(new Vector3(x,y,z));

        IEnumerator Walkthrough()
        {
            string takePath=Path.Combine(Application.dataPath,"../walkthrough-take.json");
            // A failed new take must not leave a previous success as its result.
            if(File.Exists(takePath))File.Delete(takePath);
            app.explode.SetTarget(0);
            while(app.explode.IsBusy)yield return null;
            // Reset before the take begins, so replay starts at the entrance
            // and the recorded route itself remains one continuous walk.
            var body=app.origin.GetComponent<CharacterController>();body.enabled=false;
            app.origin.transform.SetPositionAndRotation(savedRigPosition,savedRigRotation);
            view.transform.SetLocalPositionAndRotation(Vector3.up*1.65f,savedCameraRotation);
            body.enabled=true;eyeHeight=targetEye=1.65f;
            headYaw=app.origin.transform.eulerAngles.y;headPitch=view.transform.localEulerAngles.x;
            if(headPitch>180)headPitch-=360;
            yawVelocity=pitchVelocity=walkSpeed=0;travelDirection=Vector3.zero;
            lookPoint=app.entryLook;
            view.GetUniversalAdditionalCameraData().resetHistory=true;
            playing=true;paused=false;walkDistance=0;stageIndex=0;detailReviewed=false;
            if(XRLaunch.HasArgument("--vr-walkthrough-export"))
            {
                recorder=GetComponent<VRWalkthroughRecorder>() ?? gameObject.AddComponent<VRWalkthroughRecorder>();
                recorder.Begin(view);
            }
            startedAt=Time.time;startedUtc=DateTime.UtcNow;
            Debug.Log($"[CarAR Walkthrough] START {startedUtc:O}; actual floor locomotion; eye=1.65m; fov=60");
            yield return PauseLook("入口环视",Look(0,.9f,0),2.5f);
            yield return Go("走近车头",-1.6f,3.55f,Look(-.3f,.9f,1.2f),3);
            yield return Go("前脸观察",0,3.4f,Look(0,.8f,1.8f),3);
            yield return Go("走向左前角",-1.8f,3.05f,Look(-.5f,.8f,1.6f),1);
            yield return Go("左前轮与侧面",-2.5f,1.35f,Look(-.85f,.65f,1.2f),3);
            yield return Go("走到模型侧面",-2.5f,-.45f,Look(-.7f,.95f,-.35f),1.5f);
            yield return PauseLook("侧面细节评审",Look(-.6f,.95f,-.35f),4);
            yield return Go("靠近模型",-1.4f,-.55f,Look(-.15f,.95f,-.15f),1);
            targetEye=1.35f;
            yield return PauseLook("近景几何与材质评审",Look(-.15f,.95f,-.15f),4);
            detailReviewed=true;
            targetEye=1.65f;
            yield return Go("退回模型侧面",-2.6f,-.55f,Look(-.6f,.95f,-.4f),1.5f);
            yield return Go("沿车侧走向车尾",-2.6f,-2.2f,Look(-.65f,.9f,-1.1f),1.5f);
            yield return Go("绕过左后角",-1.8f,-3.35f,Look(-.3f,.85f,-1.7f),2);
            yield return Go("车尾观察",0,-3.6f,Look(0,.8f,-1.8f),4);
            yield return Go("绕过右后角",1.8f,-3.25f,Look(.4f,.85f,-1.6f),1);
            yield return Go("右后侧观察",2.6f,-1.9f,Look(.7f,.9f,-1),2);
            yield return Go("沿右侧向前走",2.6f,.7f,Look(.65f,.9f,.2f),2);
            yield return Go("回到右前角",2,3.05f,Look(.35f,.85f,1.4f),2);
            yield return Go("走回车前",.2f,4.15f,Look(0,.9f,.5f),2);
            yield return Go("退后看整车",-1.1f,5.25f,Look(0,1.05f,0),3);
            app.explode.SetTarget(1);
            yield return PauseLook("原地观察爆炸展示",Look(0,1.15f,0),7);
            app.explode.SetTarget(0);
            yield return PauseLook("组装还原",Look(0,.85f,0),5);
            playing=false;walkSpeed=0;travelDirection=Vector3.zero;
            bool fixedWorld=app.car.localToWorldMatrix==carPose && app.environment.localToWorldMatrix==roomPose;
            bool assembled=app.explode.Progress<.001f;
            var take=new Take{startedUtc=startedUtc.ToString("O"),completedUtc=DateTime.UtcNow.ToString("O"),
                durationSeconds=Time.time-startedAt,walkedMetres=walkDistance,
                continuousFirstPerson=true,fixedWorld=fixedWorld,assembled=assembled,detailReviewed=detailReviewed};
            if(!fixedWorld || !assembled || !detailReviewed)throw new InvalidOperationException("Walkthrough final scene validation failed");
            if(recorder!=null)recorder.Finish();
            File.WriteAllText(takePath,JsonUtility.ToJson(take,true));
            Debug.Log($"[CarAR Walkthrough] COMPLETE {take.completedUtc}; seconds={take.durationSeconds:F2}; walked={walkDistance:F2}m; fixedWorld={fixedWorld}; assembled={assembled}; detailReviewed={detailReviewed}");
            if(XRLaunch.HasArgument("--vr-walkthrough-check"))Application.Quit();
        }

        void Stage(string name)
        {
            Debug.Log($"[CarAR Walkthrough] Stage {stageIndex++}: {name}; t={Time.time-startedAt:F2}; eye={eyeHeight:F2}");
            if(XRLaunch.HasArgument("--vr-walkthrough-check"))nextCapture=Time.unscaledTime+1.1f;
        }

        IEnumerator Go(string name,float x,float z,Vector3 look,float hold,float maxSpeed=.78f,float arrivalTolerance=.10f)
        {
            Stage(name);lookPoint=look;
            var desired=World(x,z);
            if(!NavMesh.SamplePosition(desired,out var destination,.75f,NavMesh.AllAreas))
                throw new InvalidOperationException("Walkthrough destination is outside the floor: "+name);
            var path=new NavMeshPath();
            if(!NavMesh.CalculatePath(app.Feet,destination.position,NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete)
                throw new InvalidOperationException("Walkthrough has no connected floor route: "+name);
            foreach(var corner in path.corners)
            {
                float deadline=Time.time+30;
                while(Vector3.ProjectOnPlane(corner-app.Feet,Vector3.up).magnitude>arrivalTolerance)
                {
                    if(paused){Tick(Vector3.zero,0);yield return null;deadline+=Time.deltaTime;continue;}
                    var delta=Vector3.ProjectOnPlane(corner-app.Feet,Vector3.up);
                    float speed=Mathf.Min(maxSpeed,Mathf.Sqrt(delta.magnitude*1.1f));
                    Tick(delta.normalized,speed);
                    if(Time.time>deadline)throw new InvalidOperationException("Walkthrough movement stalled: "+name+" at "+app.Feet);
                    yield return null;
                }
            }
            yield return Hold(hold);
        }

        IEnumerator PauseLook(string name,Vector3 look,float seconds)
        {
            Stage(name);lookPoint=look;yield return Hold(seconds);
        }
        IEnumerator Hold(float seconds)
        {
            float remaining=seconds;
            while(remaining>0)
            {
                Tick(Vector3.zero,0);
                if(!paused)remaining-=Time.deltaTime;
                yield return null;
            }
        }

        void Tick(Vector3 direction,float speed)
        {
            if(recorder!=null && recorder.Failed)throw new IOException("Walkthrough video capture failed");
            if(paused)return;
            walkSpeed=Mathf.MoveTowards(walkSpeed,speed,Time.deltaTime*1.6f);
            if(direction.sqrMagnitude>.01f)travelDirection=direction;
            eyeHeight=Mathf.MoveTowards(eyeHeight,targetEye,Time.deltaTime*.28f);
            var before=app.Feet;
            var looking=lookPoint-view.transform.position;
            var horizontal=Vector3.ProjectOnPlane(looking,Vector3.up);
            // Look slightly towards the walking direction and settle onto the
            // detail when stopping, as a visitor does while circling a vehicle.
            if(direction.sqrMagnitude>.01f)horizontal=Vector3.Slerp(horizontal.normalized,direction,.28f);
            float desiredYaw=Mathf.Atan2(horizontal.x,horizontal.z)*Mathf.Rad2Deg;
            float desiredPitch=-Mathf.Atan2(looking.y,Mathf.Max(.1f,Vector3.ProjectOnPlane(looking,Vector3.up).magnitude))*Mathf.Rad2Deg;
            headYaw=Mathf.SmoothDampAngle(headYaw,desiredYaw,ref yawVelocity,.45f,38,Time.deltaTime);
            headPitch=Mathf.SmoothDampAngle(headPitch,desiredPitch,ref pitchVelocity,.5f,22,Time.deltaTime);
            app.origin.transform.rotation=Quaternion.Euler(0,headYaw,0);
            view.transform.localRotation=Quaternion.Euler(headPitch,0,0);
            view.transform.localPosition=Vector3.up*(eyeHeight+Mathf.Sin(walkDistance*9)*.007f*Mathf.Clamp01(walkSpeed/.7f));
            app.WalkForPresentation(travelDirection,walkSpeed);
            walkDistance+=Vector3.ProjectOnPlane(app.Feet-before,Vector3.up).magnitude;
            if(nextCapture>0 && Time.unscaledTime>=nextCapture)
            {
                nextCapture=0;
                ScreenCapture.CaptureScreenshot(Path.Combine(Application.dataPath,"../walk-check-"+(stageIndex-1).ToString("00")+".png"));
            }
        }

        void Restore()
        {
            if(!prepared)return;
            if(routine!=null)StopCoroutine(routine);
            try {if(recorder!=null)recorder.Finish();}
            catch(Exception error){Debug.LogException(error);}
            playing=false;paused=false;
            app.explode.SetTarget(0);
            var body=app.origin.GetComponent<CharacterController>();body.enabled=false;
            app.origin.transform.SetPositionAndRotation(savedRigPosition,savedRigRotation);
            view.transform.SetLocalPositionAndRotation(savedCameraPosition,savedCameraRotation);view.fieldOfView=savedFov;
            body.enabled=true;app.PresentationActive=false;prepared=false;
            Application.targetFrameRate=savedFrameRate;Cursor.visible=savedCursor;Cursor.lockState=savedLock;
        }
        void OnDisable()
        {
            if(!prepared)return;
            try {if(recorder!=null)recorder.Finish();}
            catch(Exception error){Debug.LogException(error);}
            Cursor.visible=savedCursor;Cursor.lockState=savedLock;Application.targetFrameRate=savedFrameRate;
        }
    }
}
