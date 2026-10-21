using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarAR
{
    // A small first scene selects the experience before either XR rig wakes up.
    public sealed class XRLaunch : MonoBehaviour
    {
        public static bool DesktopVR { get; private set; }
        public static bool HasArgument(string value) => Array.IndexOf(Environment.GetCommandLineArgs(), value) >= 0;
        void Awake()
        {
            DesktopVR = HasArgument("--vr-desktop") || HasArgument("--vr-smoke");
            SceneManager.LoadScene(DesktopVR || HasArgument("--vr") ? "CarVR" : "CarAR");
        }
        public static void OpenVRPreview()
        {
            DesktopVR = true;
            SceneManager.LoadScene("CarVR");
        }
    }
}
