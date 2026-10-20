using System;
using System.Runtime.InteropServices;
using UnityEngine;
using CarAR.Huijing;
namespace CarAR.EditorTools
{
    internal static class HuijingInteropValidation
    {
        public static void Validate()
        {
            if (Marshal.SizeOf<HjxrLayer>() != 8 || Marshal.SizeOf<HjxrLayerOrder>() != 8 || Marshal.SizeOf<HjxrLayerPoseTimeOffset>() != 16)
                throw new Exception("HJXR compositor ABI layout mismatch");
            Debug.Log("[HJXR Validation] PASS: native compositor ABI; no marker calibration or input-space hooks");
        }
    }
}
