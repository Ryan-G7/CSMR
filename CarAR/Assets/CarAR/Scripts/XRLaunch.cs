using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarAR
{
    // A small first scene selects the experience before either XR rig wakes up.
    public sealed class XRLaunch : MonoBehaviour
    {
        public static bool DesktopVR { get; private set; }
        public static string ModelPath { get; private set; }
        public static bool HasArgument(string value) => Array.IndexOf(Environment.GetCommandLineArgs(), value) >= 0;
        void Awake()
        {
            var arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length; i++)
            {
                if (arguments[i] == "--model" && i + 1 < arguments.Length) ModelPath = arguments[i + 1];
                else if (arguments[i].StartsWith("--model=", StringComparison.Ordinal)) ModelPath = arguments[i].Substring(8);
            }
            DesktopVR = HasArgument("--vr-desktop") || HasArgument("--vr-smoke");
            SceneManager.LoadScene(DesktopVR || HasArgument("--vr") || HasArgument("--varjo") ? "CarVR" : "CarAR");
        }
        public static void OpenVRPreview()
        {
            DesktopVR = true;
            SceneManager.LoadScene("CarVR");
        }
    }
}
