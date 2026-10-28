using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using Varjo.XR;

namespace CarAR.VarjoSupport
{
    public sealed class VarjoMRBridge : MonoBehaviour
    {
        public static bool Requested => XRLaunch.HasArgument("--varjo") || XRLaunch.HasArgument("--varjo-mr");
        public static bool MixedRealityRequested => XRLaunch.HasArgument("--varjo-mr");
        bool rendering;

        public static void PrepareLoader(XRManagerSettings manager)
        {
            var loaders = new List<XRLoader>(manager.activeLoaders);
            Type preferred = Requested ? typeof(VarjoLoader) : typeof(OpenXRLoaderNoPreInit);
            var loader = loaders.Find(item => item != null && item.GetType() == preferred);
            if (loader == null) throw new InvalidOperationException("Missing XR loader: " + preferred.Name);
            loaders.Remove(loader);
            loaders.Insert(0, loader);
            if (!manager.TrySetLoaders(loaders)) throw new InvalidOperationException("Cannot select XR loader: " + preferred.Name);
        }

        public static bool ValidateLoader(XRLoader loader)
        {
            if (!Requested || loader is VarjoLoader) return true;
            Debug.LogError("[CarAR Varjo] Varjo was requested but another XR loader started. Check Varjo Base and the headset connection.");
            return false;
        }

        public bool StartMixedReality()
        {
            if (!MixedRealityRequested) return true;
            var camera = Camera.main;
            if (camera == null)
            {
                Debug.LogError("[CarAR Varjo] Main camera is missing.");
                return false;
            }
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            var background = camera.GetComponent<ARCameraBackground>();
            if (background != null) background.enabled = false;
            if (!VarjoMixedReality.IsMRAvailable() || !VarjoMixedReality.StartRender())
            {
                Debug.LogError("[CarAR Varjo] Video pass-through could not start. Check Varjo Base, MR permissions and the XR-4 cameras.");
                return false;
            }
            rendering = true;
            Debug.Log("[CarAR Varjo] MR video pass-through started with the shared review scene.");
            return true;
        }

        public void Shutdown()
        {
            if (!rendering) return;
            VarjoMixedReality.StopRender();
            rendering = false;
        }

        void OnDestroy() => Shutdown();
    }
}
