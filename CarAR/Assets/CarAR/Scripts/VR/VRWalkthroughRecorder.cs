using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace CarAR
{
    // Capture only rendered scene pixels, independently of desktop occlusion.
    public sealed class VRWalkthroughRecorder : MonoBehaviour
    {
        const int Width=3840,Height=2160,FrameRate=60;
        Process encoder;
        RenderTexture target,sceneTarget,previousCameraTarget;
        Texture2D readback;
        byte[] pixels;
        readonly StringBuilder encoderLog=new();
        Camera renderingCamera;
        Coroutine capture;
        float previousCaptureDelta;
        int previousFrameRate,frames;
        bool recording;
        string output;
        public bool Failed { get; private set; }

        public void Begin(Camera camera)
        {
            if(recording)throw new InvalidOperationException("A video is already recording");
            Failed=false;frames=0;encoderLog.Clear();
            previousCaptureDelta=Time.captureDeltaTime;previousFrameRate=Application.targetFrameRate;
            renderingCamera=camera;previousCameraTarget=camera.targetTexture;
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../Recordings"));
            Directory.CreateDirectory(directory);
            output=Path.Combine(directory,$"AMG-GT-review-4K60-{DateTime.Now:yyyyMMdd-HHmmss-fff}.mp4");
            string ffmpeg=Environment.GetEnvironmentVariable("CARAR_FFMPEG");
            if(string.IsNullOrWhiteSpace(ffmpeg))ffmpeg="ffmpeg";
            try
            {
                encoder=new Process {StartInfo=new ProcessStartInfo {
                    FileName=ffmpeg,
                    Arguments=$"-hide_banner -loglevel warning -f rawvideo -pixel_format rgb24 -video_size {Width}x{Height} -framerate {FrameRate} -i pipe:0 -an -vf vflip -c:v libx264 -preset veryfast -crf 12 -pix_fmt yuv420p -movflags +faststart -n \"{output}\"",
                    UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardError=true
                }};
                encoder.ErrorDataReceived+=(_,args)=>{if(args.Data!=null)lock(encoderLog)encoderLog.AppendLine(args.Data);};
                encoder.Start();encoder.BeginErrorReadLine();
                sceneTarget=new RenderTexture(Width,Height,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear)
                    {antiAliasing=8,bindTextureMS=false,useDynamicScale=false};
                target=new RenderTexture(Width,Height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB)
                    {antiAliasing=1,useDynamicScale=false};
                if(!sceneTarget.Create() || !target.Create())throw new InvalidOperationException("Cannot allocate 4K recording targets");
                readback=new Texture2D(Width,Height,TextureFormat.RGB24,false,false);
                pixels=new byte[Width*Height*3];camera.targetTexture=sceneTarget;
                Time.captureDeltaTime=1f/FrameRate;Application.targetFrameRate=FrameRate;
                recording=true;capture=StartCoroutine(Capture());
                UnityEngine.Debug.Log($"[CarAR Walkthrough Video] START {Width}x{Height}, {FrameRate}fps: {output}");
            }
            catch {Failed=true;Release();throw;}
        }

        IEnumerator Capture()
        {
            var endOfFrame=new WaitForEndOfFrame();
            while(recording)
            {
                yield return endOfFrame;
                if(!recording)yield break;
                var previous=RenderTexture.active;bool srgbWrite=GL.sRGBWrite;
                try
                {
                    GL.sRGBWrite=true;Graphics.Blit(sceneTarget,target);
                    RenderTexture.active=target;
                    readback.ReadPixels(new Rect(0,0,Width,Height),0,0,false);
                    readback.GetRawTextureData<byte>().CopyTo(pixels);
                    encoder.StandardInput.BaseStream.Write(pixels,0,pixels.Length);frames++;
                }
                catch(Exception error)
                {
                    Failed=true;recording=false;UnityEngine.Debug.LogException(error);
                }
                finally {RenderTexture.active=previous;GL.sRGBWrite=srgbWrite;}
                if(Failed){Release();yield break;}
            }
        }

        public void Finish()
        {
            if(!recording)return;
            recording=false;
            if(capture!=null){StopCoroutine(capture);capture=null;}
            try
            {
                encoder.StandardInput.Close();
                if(!encoder.WaitForExit(30000))throw new TimeoutException("Video encoder did not finish");
                encoder.WaitForExit(); // Drain the asynchronous stderr callback.
                string log;lock(encoderLog)log=encoderLog.ToString();
                File.WriteAllText(Path.ChangeExtension(output,".encoder.log"),log);
                if(encoder.ExitCode!=0)throw new IOException("Video encoder failed: "+log);
                string duration=(frames/(double)FrameRate).ToString("F6",CultureInfo.InvariantCulture);
                File.WriteAllText(Path.ChangeExtension(output,".frames.json"),$"{{\"width\":{Width},\"height\":{Height},\"frames\":{frames},\"fps\":{FrameRate},\"duration\":{duration}}}");
                UnityEngine.Debug.Log($"[CarAR Walkthrough Video] SAVED: frames={frames}; duration={duration}s; {output}");
            }
            catch {Failed=true;throw;}
            finally {Release();}
        }

        void Release()
        {
            recording=false;Time.captureDeltaTime=previousCaptureDelta;Application.targetFrameRate=previousFrameRate;
            if(renderingCamera!=null)renderingCamera.targetTexture=previousCameraTarget;
            if(encoder!=null)
            {
                try {if(!encoder.HasExited){encoder.StandardInput.Close();if(!encoder.WaitForExit(2000))encoder.Kill();}}
                catch(Exception error){UnityEngine.Debug.LogWarning("Encoder cleanup: "+error.Message);}
                finally {encoder.Dispose();encoder=null;}
            }
            if(readback!=null)Destroy(readback);
            if(target!=null){target.Release();Destroy(target);}
            if(sceneTarget!=null){sceneTarget.Release();Destroy(sceneTarget);}
            readback=null;target=null;sceneTarget=null;pixels=null;
        }
        void OnGUI()
        {
            if(recording && Event.current.type==EventType.Repaint)
                GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),target,ScaleMode.ScaleToFit,false);
        }
        void OnDestroy()
        {
            try {Finish();}catch(Exception error){UnityEngine.Debug.LogException(error);}
        }
    }
}
