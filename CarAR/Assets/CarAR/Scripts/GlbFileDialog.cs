using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CarAR
{
    internal static class GlbFileDialog
    {
        public static string Select()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var buffer = new string('\0', 32768);
            var dialog = new OpenFileName
            {
                structSize = Marshal.SizeOf<OpenFileName>(),
                filter = "glTF binary model (*.glb)\0*.glb\0All files (*.*)\0*.*\0",
                file = buffer,
                maxFile = buffer.Length,
                title = "选择 GLB 模型",
                flags = 0x00080000 | 0x00001000 | 0x00000008
            };
            return GetOpenFileName(ref dialog) ? buffer.TrimEnd('\0') : null;
#else
            Debug.LogWarning("[CarAR Model] This platform supports --model <path.glb>; the native picker is available on Windows.");
            return null;
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct OpenFileName
        {
            public int structSize;
            public IntPtr owner;
            public IntPtr instance;
            public string filter;
            public string customFilter;
            public int maxCustomFilter;
            public int filterIndex;
            public string file;
            public int maxFile;
            public string fileTitle;
            public int maxFileTitle;
            public string initialDirectory;
            public string title;
            public int flags;
            public short fileOffset;
            public short fileExtension;
            public string defaultExtension;
            public IntPtr customData;
            public IntPtr hook;
            public string templateName;
            public IntPtr reserved;
            public int reservedInt;
            public int flagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetOpenFileName(ref OpenFileName dialog);
#endif
    }
}
