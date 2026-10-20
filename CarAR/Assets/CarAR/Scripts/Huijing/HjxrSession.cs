// ABI and coordinate conventions from the manufacturer's unity_mr_sdk_demo/3rd/hjxr.
using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
namespace CarAR.Huijing
{
    public enum HjxrResult { Success = 0, Failed = -1, DllFailed = -2, DllWrong = -3, InterfaceNotFound = -4, InvalidParam = -5, RpcFailed = -6, ShareMemFailed = -7, UnsupportedRender = -8, NoDisplay = -9, NoRenderDevice = -10, AppNotVisible = -11, ServerNotReady = -12, VersionMismatch = -13, AdapterMismatch = -14, NotSupported = -15 }
    public enum HjxrCommand { SetLayerZOrder = 0, SetLayerEnable = 2, GetLayerEnable = 3, SetLayerPoseTimeOffset = 21, GetLayerPoseTimeOffset = 22, GetVstVsyncOffset = 27, SetLayerPoseDefer = 43, SetHeadLock = 60 }
    [StructLayout(LayoutKind.Sequential)]
    public struct HjxrLayer { public int layer, enabled; } // 0 = VR, 1 = VST; hjxr_Bool is int32.
    [StructLayout(LayoutKind.Sequential)]
    public struct HjxrLayerOrder { public int layer, zorder; }
    [StructLayout(LayoutKind.Sequential)]
    public struct HjxrLayerPoseTimeOffset { public int layer; public long timeOffsetNs; }

    public sealed class HjxrSession : IDisposable
    {
        IntPtr module, session;
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr GetInterface(uint major, uint minor);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr InitSession();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void DeinitSession(IntPtr handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Ioctl(IntPtr handle, int command, IntPtr p1, IntPtr p2);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr GetVersion();
        DeinitSession deinit;
        Ioctl ioctl;
        public string Version { get; private set; } = "unavailable";
        public bool IsInitialized => session != IntPtr.Zero;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);
        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true)] static extern IntPtr GetProcAddress(IntPtr handle, string name);
        [DllImport("kernel32")] static extern bool FreeLibrary(IntPtr module);
#endif
        public HjxrResult Initialize(string dll)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (IsInitialized) return HjxrResult.Success;
            if (string.IsNullOrWhiteSpace(dll) || !Path.IsPathRooted(dll) || !File.Exists(dll)) return HjxrResult.DllFailed;
            module = LoadLibraryExW(Path.GetFullPath(dll), IntPtr.Zero, 0x100 | 0x1000);
            if (module == IntPtr.Zero) { Debug.LogError("[HJXR] DLL load error " + Marshal.GetLastWin32Error()); return HjxrResult.DllFailed; }
            var address = GetProcAddress(module, "jxr_hjxrGetInterface");
            if (address == IntPtr.Zero) { Dispose(); return HjxrResult.DllWrong; }
            var table = Marshal.GetDelegateForFunctionPointer<GetInterface>(address)(5, 0);
            if (table == IntPtr.Zero) { Dispose(); return HjxrResult.InterfaceNotFound; }
            var init = Marshal.GetDelegateForFunctionPointer<InitSession>(Marshal.ReadIntPtr(table));
            deinit = Marshal.GetDelegateForFunctionPointer<DeinitSession>(Marshal.ReadIntPtr(table, IntPtr.Size));
            ioctl = Marshal.GetDelegateForFunctionPointer<Ioctl>(Marshal.ReadIntPtr(table, IntPtr.Size * 2));
            var version = Marshal.GetDelegateForFunctionPointer<GetVersion>(Marshal.ReadIntPtr(table, IntPtr.Size * 3));
            Version = Marshal.PtrToStringAnsi(version()) ?? "unknown";
            session = init();
            if (session == IntPtr.Zero) { Dispose(); return HjxrResult.Failed; }
            return HjxrResult.Success;
#else
            return HjxrResult.NotSupported;
#endif
        }
        public HjxrResult Command<T>(HjxrCommand command, ref T value) where T : struct
        {
            if (!IsInitialized) return HjxrResult.InvalidParam;
            var data = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
            try
            {
                Marshal.StructureToPtr(value, data, false);
                var result = (HjxrResult)ioctl(session, (int)command, data, IntPtr.Zero);
                if (result == HjxrResult.Success) value = Marshal.PtrToStructure<T>(data);
                return result;
            }
            finally { Marshal.FreeHGlobal(data); }
        }
        public void Dispose()
        {
            if (session != IntPtr.Zero) { deinit?.Invoke(session); session = IntPtr.Zero; }
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (module != IntPtr.Zero) { FreeLibrary(module); module = IntPtr.Zero; }
#endif
            deinit = null; ioctl = null;
        }
    }
}
