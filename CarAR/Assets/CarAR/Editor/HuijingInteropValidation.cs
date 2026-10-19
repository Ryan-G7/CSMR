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
            // These layouts cross an unmanaged boundary and must match the vendor C headers.
            if (Marshal.SizeOf<HjxrPose>() != 28 || Marshal.SizeOf<HjxrLayer>() != 8 || Marshal.SizeOf<HjxrLayerOrder>() != 8 || Marshal.SizeOf<HjxrLayerPoseTimeOffset>() != 16)
                throw new Exception("HJXR ABI layout mismatch");
            var pose = HjxrPose.FromUnity(new Vector3(1, 2, 3), Quaternion.identity);
            if (pose.px != 1 || pose.py != 2 || pose.pz != -3 || pose.qw != -1)
                throw new Exception("HJXR marker pose convention mismatch");
            // A converted rotation must act on mirrored vectors just like the original Unity rotation.
            foreach (var angles in new[] { Vector3.zero, new Vector3(30, 70, -20), new Vector3(180, 0, 90) })
            {
                var rotation = Quaternion.Euler(angles);
                var converted = HjxrPose.FromUnity(Vector3.zero, rotation);
                var vendorRotation = new Quaternion(converted.qx, converted.qy, converted.qz, converted.qw);
                var v = new Vector3(.3f, -.7f, 1.2f);
                var expected = rotation * v; expected.z = -expected.z;
                var actual = vendorRotation * new Vector3(v.x, v.y, -v.z);
                if (Vector3.Distance(expected, actual) > .00001f) throw new Exception("HJXR rotation handedness mismatch");
            }
            Debug.Log("[HJXR Validation] PASS: native ABI and marker coordinate conversion");
        }
    }
}
