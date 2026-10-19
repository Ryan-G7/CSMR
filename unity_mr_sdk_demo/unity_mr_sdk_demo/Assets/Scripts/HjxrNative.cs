using System;
using System.Runtime.InteropServices;

public enum HjxrResult
{
    Success = 0,
    Failed = -1,
    DllFailed = -2,
    DllWrong = -3,
    InterfaceNotFound = -4,
    InvalidParam = -5,
    RpcFailed = -6,
    ShareMemFailed = -7,
    UnsupportRenderName = -8,
    NoDisplay = -9,
    NoRenderDevice = -10,
    AppNotVisible = -11,
    SrvNotReady = -12,
    DllSrvMismatch = -13,
    AppAdapterMismatch = -14,
    NotSupport = -15,
}

public enum HjxrCmdType
{
    SetLayerZOrder = 0,
    GetLayerZOrder = 1,
    SetLayerEnable = 2,
    GetLayerEnableState = 3,
    SetVstMeshWithDepthEnable = 4,
    GetVstMeshWithDepthEnableState = 5,
    SetChromaKeyEnable = 6,
    GetChromaKeyEnableState = 7,
    SetChromaKeyColor = 8,
    GetChromaKeyColor = 9,
    SetVstZoneBy3dObjEnable = 10,
    GetVstZoneBy3dObjEnableState = 11,
    SetVstZone3dObjFilePath = 12,
    GetVstZone3dObjFilePath = 13,
    SetCameraProp = 14,
    GetCameraProp = 15,
    WorldSync = 16,
    WorldSetMarkerPoseOffset = 17,
    WorldGetMarkerPoseOffset = 18,
    SetVstModelPositionOffset = 19,
    GetVstModelPositionOffset = 20,
    SetLayerPoseTimeOffset = 21,
    GetLayerPoseTimeOffset = 22,
    SaveConfigToFile = 23,
    GetMarkerAdjMat = 24,
    GetMarkerAdjMatAddr = 25,
    SetVstVsyncOffsetNs = 26,
    GetVstVsyncOffsetNs = 27,
    SetVstNightMode = 28,
    GetVstNightMode = 29,
    SetMaskModelPoseOffset = 30,
    GetMaskModelPoseOffset = 31,
    SetChromaKeyThreshold = 32,
    GetChromaKeyThreshold = 33,
    SetVideoModelPoseOffset = 34,
    GetVideoModelPoseOffset = 35,
    SetMixrealityTrans = 36,
    GetMixrealityTrans = 37,
    SetDfrCropTanxy = 38,
    SetCameraFrameBufferEnable = 39,
    GetCameraFrameBufferEnable = 40,
    SetTrackLocateSpaceEnable = 41,
    GetTrackLocateSpaceEnable = 42,
    SetLayerPoseDefer = 43,
    GetLayerPoseDefer = 44,
    SetRsCameraCorrectionEnable = 45,
    GetRsCameraCorrectionEnable = 46,
    GetEyeTrackingData = 47,
    SetAlignMethod = 48,
    SetVstDfrEnable = 49,
    SetVrWidthHeightPect = 50,
    SetCloseServer = 51,
    GetSvrGlobalIniInfo = 52,
    SetCameraBicubicEnable = 53,
    SetCameraBrightFactor = 54,
    SetCameraGammaEnable = 55,
    SetCameraGammaFactor = 56,
    TrackerDetectMarker = 57,
    TrackerPoseRecord = 58,
    SetMarkerViveOffset = 59,
    SetHeadLock = 60,
    SetUseCubeMarker = 61,
    GetSvrCameraCalibrationInfo = 62,
    WorldSetMarkerPose = 63,
    WorldGetMarkerPose = 64,
    Unknown = 65,
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct HjxrVector3
{
    public float x;
    public float y;
    public float z;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct HjxrQuaternion
{
    public float x;
    public float y;
    public float z;
    public float w;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct HjxrPoseInfo
{
    public HjxrVector3 position;
    public HjxrQuaternion rotation;
}

public static class HjxrNative
{
    private const string HJXR_SDK_DLL_NAME = "jxrsdk.dll";
    private const string HJXR_GET_INTERFACE_FUNC_NAME = "jxr_hjxrGetInterface";
    private const uint HJXR_VERSION_MAJOR = 5;
    private const uint HJXR_VERSION_MINOR = 0;

    private static IntPtr _hSdkDll = IntPtr.Zero;
    private static IntPtr _pfnTable = IntPtr.Zero;
    private static IntPtr _hSession = IntPtr.Zero;

    private delegate IntPtr PfnHjxrGetInterface(uint major, uint minor);
    private delegate IntPtr PfnHjxrInitSession();
    private delegate void PfnHjxrDeinitSession(IntPtr hnd);
    private delegate int PfnHjxrIoctl(IntPtr hnd, int cmd, IntPtr pParam1, IntPtr pParam2);

    private static PfnHjxrInitSession _initSession;
    private static PfnHjxrDeinitSession _deinitSession;
    private static PfnHjxrIoctl _ioctl;

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr LoadLibraryA(string lpLibFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hLibModule);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    public static HjxrResult Init()
    {
        if (_pfnTable != IntPtr.Zero)
        {
            return HjxrResult.Success;
        }

        _hSdkDll = LoadLibraryA(HJXR_SDK_DLL_NAME);
        if (_hSdkDll == IntPtr.Zero)
        {
            return HjxrResult.DllFailed;
        }

        IntPtr pGetInterface = GetProcAddress(_hSdkDll, HJXR_GET_INTERFACE_FUNC_NAME);
        if (pGetInterface == IntPtr.Zero)
        {
            FreeLibrary(_hSdkDll);
            _hSdkDll = IntPtr.Zero;
            return HjxrResult.DllWrong;
        }

        PfnHjxrGetInterface getInterface = (PfnHjxrGetInterface)Marshal.GetDelegateForFunctionPointer(pGetInterface, typeof(PfnHjxrGetInterface));
        _pfnTable = getInterface(HJXR_VERSION_MAJOR, HJXR_VERSION_MINOR);
        if (_pfnTable == IntPtr.Zero)
        {
            FreeLibrary(_hSdkDll);
            _hSdkDll = IntPtr.Zero;
            return HjxrResult.InterfaceNotFound;
        }

        IntPtr pInitSession = Marshal.ReadIntPtr(_pfnTable, 0);
        IntPtr pDeinitSession = Marshal.ReadIntPtr(_pfnTable, IntPtr.Size);
        IntPtr pIoctl = Marshal.ReadIntPtr(_pfnTable, IntPtr.Size * 2);

        _initSession = (PfnHjxrInitSession)Marshal.GetDelegateForFunctionPointer(pInitSession, typeof(PfnHjxrInitSession));
        _deinitSession = (PfnHjxrDeinitSession)Marshal.GetDelegateForFunctionPointer(pDeinitSession, typeof(PfnHjxrDeinitSession));
        _ioctl = (PfnHjxrIoctl)Marshal.GetDelegateForFunctionPointer(pIoctl, typeof(PfnHjxrIoctl));

        _hSession = _initSession();
        if (_hSession == IntPtr.Zero)
        {
            _pfnTable = IntPtr.Zero;
            FreeLibrary(_hSdkDll);
            _hSdkDll = IntPtr.Zero;
            return HjxrResult.Failed;
        }

        return HjxrResult.Success;
    }

    public static void Deinit()
    {
        if (_hSession != IntPtr.Zero && _deinitSession != null)
        {
            _deinitSession(_hSession);
            _hSession = IntPtr.Zero;
        }

        if (_hSdkDll != IntPtr.Zero)
        {
            FreeLibrary(_hSdkDll);
            _hSdkDll = IntPtr.Zero;
        }

        _pfnTable = IntPtr.Zero;
        _initSession = null;
        _deinitSession = null;
        _ioctl = null;
    }

    public static HjxrResult Ioctl(HjxrCmdType cmd, IntPtr pParam1 = default, IntPtr pParam2 = default)
    {
        if (_ioctl == null || _hSession == IntPtr.Zero)
        {
            return HjxrResult.InvalidParam;
        }

        int result = _ioctl(_hSession, (int)cmd, pParam1, pParam2);
        return (HjxrResult)result;
    }

    public static HjxrResult Ioctl(HjxrCmdType cmd, ref HjxrPoseInfo param)
        {
            if (_ioctl == null || _hSession == IntPtr.Zero)
            {
                return HjxrResult.InvalidParam;
            }

            IntPtr pParam = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(HjxrPoseInfo)));
            try
            {
                Marshal.StructureToPtr(param, pParam, false);
                int result = _ioctl(_hSession, (int)cmd, pParam, IntPtr.Zero);
                param = (HjxrPoseInfo)Marshal.PtrToStructure(pParam, typeof(HjxrPoseInfo));
                return (HjxrResult)result;
            }
            finally
            {
                Marshal.FreeHGlobal(pParam);
            }
        }

    public static bool IsInitialized()
    {
        return _hSession != IntPtr.Zero && _ioctl != null;
    }
}