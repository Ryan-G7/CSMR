using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using UnityEngine.XR.OpenXR.Features;

namespace CarAR.Huijing
{
#if UNITY_EDITOR
    [UnityEditor.XR.OpenXR.Features.OpenXRFeature(
        UiName = "HJXR marker input coordinates",
        BuildTargetGroups = new[] { UnityEditor.BuildTargetGroup.Standalone },
        Company = "CarAR", Version = "1.0.0",
        Desc = "Matches tracked input to the manufacturer's marker-aligned rendering.",
        FeatureId = "com.carar.openxr.hjxr-marker-input")]
#endif
    public sealed class HjxrMarkerTrackingFeature : OpenXRFeature
    {
        // In vendor mode trackLocateSpace=0, xrLocateViews supplies aligned render
        // poses while xrLocateSpace supplies raw input. Mode 1 reverses that split.
        // Keep the vendor's rendering path and adapt only application input spaces.
        [StructLayout(LayoutKind.Sequential)]
        public struct SpacePose { public Quaternion rotation; public Vector3 position; }
        [StructLayout(LayoutKind.Sequential)]
        public struct SpaceLocation
        {
            public int type;
            public IntPtr next;
            public ulong flags;
            public SpacePose pose;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct SpaceVelocity
        {
            public int type;
            public IntPtr next;
            public ulong flags;
            public Vector3 linear, angular;
        }
        public sealed class Alignment
        {
            readonly Matrix4x4 matrix;
            readonly Quaternion rotation;
            public Alignment(Matrix4x4 nativeMatrix) { matrix = nativeMatrix; rotation = nativeMatrix.rotation; }
            public void Transform(ref SpaceLocation location)
            {
                if ((location.flags & 1) != 0) location.pose.rotation = rotation * location.pose.rotation;
                if ((location.flags & 2) != 0) location.pose.position = matrix.MultiplyPoint3x4(location.pose.position);
            }
            public void Transform(ref SpaceVelocity velocity)
            {
                if ((velocity.flags & 1) != 0) velocity.linear = rotation * velocity.linear;
                if ((velocity.flags & 2) != 0) velocity.angular = rotation * velocity.angular;
            }
        }
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate int GetProc(ulong instance, IntPtr name, out IntPtr function);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate int CreateSpace(ulong session, IntPtr info, out ulong space);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate int DestroySpace(ulong space);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate int LocateSpace(ulong space, ulong baseSpace, long time, IntPtr location);
        static GetProc nextGetProc;
        static CreateSpace nextReference, nextAction;
        static DestroySpace nextDestroy;
        static LocateSpace nextLocate;
        // Root all delegates for as long as native OpenXR can hold their pointers.
        static readonly GetProc getProc = InterceptGetProc;
        static readonly CreateSpace createReference = InterceptReference;
        static readonly CreateSpace createAction = InterceptAction;
        static readonly DestroySpace destroySpace = InterceptDestroy;
        static readonly LocateSpace locateSpace = InterceptLocate;
        static readonly object spacesLock = new object();
        static readonly HashSet<ulong> inputSpaces = new HashSet<ulong>();
        static Alignment alignment;
        static long appSpace, correctedCount;
        public static bool HookInstalled => nextLocate != null;
        public static long CorrectedLocationCount => Interlocked.Read(ref correctedCount);
        public static int InputSpaceCount { get { lock (spacesLock) return inputSpaces.Count; } }
        public static void SetAlignment(Matrix4x4 nativeMatrix)
        {
            if (!HjxrMarkerFeedback.IsRigid(nativeMatrix)) throw new ArgumentException("Marker alignment must be a finite rigid transform", nameof(nativeMatrix));
            // Construct on the Unity thread; native callbacks read an immutable object.
            Volatile.Write(ref alignment, new Alignment(nativeMatrix));
        }
        protected override IntPtr HookGetInstanceProcAddr(IntPtr function)
        {
            if (!HuijingMRBridge.Requested) return function;
            nextGetProc = Marshal.GetDelegateForFunctionPointer<GetProc>(function);
            return Marshal.GetFunctionPointerForDelegate(getProc);
        }
        protected override void OnAppSpaceChange(ulong space) => Interlocked.Exchange(ref appSpace, unchecked((long)space));
        protected override void OnSessionDestroy(ulong session)
        {
            Volatile.Write(ref alignment, null);
            Interlocked.Exchange(ref appSpace, 0);
            Interlocked.Exchange(ref correctedCount, 0);
            lock (spacesLock) inputSpaces.Clear();
        }
        [AOT.MonoPInvokeCallback(typeof(GetProc))]
        static int InterceptGetProc(ulong instance, IntPtr name, out IntPtr function)
        {
            int result = nextGetProc(instance, name, out function);
            if (result < 0 || function == IntPtr.Zero) return result;
            switch (Marshal.PtrToStringAnsi(name))
            {
                case "xrCreateReferenceSpace":
                    nextReference = Marshal.GetDelegateForFunctionPointer<CreateSpace>(function);
                    function = Marshal.GetFunctionPointerForDelegate(createReference); break;
                case "xrCreateActionSpace":
                    nextAction = Marshal.GetDelegateForFunctionPointer<CreateSpace>(function);
                    function = Marshal.GetFunctionPointerForDelegate(createAction); break;
                case "xrDestroySpace":
                    nextDestroy = Marshal.GetDelegateForFunctionPointer<DestroySpace>(function);
                    function = Marshal.GetFunctionPointerForDelegate(destroySpace); break;
                case "xrLocateSpace":
                    nextLocate = Marshal.GetDelegateForFunctionPointer<LocateSpace>(function);
                    function = Marshal.GetFunctionPointerForDelegate(locateSpace); break;
            }
            return result;
        }
        [AOT.MonoPInvokeCallback(typeof(CreateSpace))]
        static int InterceptReference(ulong session, IntPtr info, out ulong space)
        {
            int result = nextReference(session, info, out space);
            // XrReferenceSpaceCreateInfo.referenceSpaceType: offset 16 on Win64.
            if (result >= 0 && space != 0 && info != IntPtr.Zero && Marshal.ReadInt32(info, 16) == 1)
                lock (spacesLock) inputSpaces.Add(space); // VIEW only; LOCAL/STAGE are base spaces.
            return result;
        }
        [AOT.MonoPInvokeCallback(typeof(CreateSpace))]
        static int InterceptAction(ulong session, IntPtr info, out ulong space)
        {
            int result = nextAction(session, info, out space);
            if (result >= 0 && space != 0) lock (spacesLock) inputSpaces.Add(space);
            return result;
        }
        [AOT.MonoPInvokeCallback(typeof(DestroySpace))]
        static int InterceptDestroy(ulong space)
        {
            int result = nextDestroy(space);
            if (result >= 0) lock (spacesLock) inputSpaces.Remove(space);
            return result;
        }
        [AOT.MonoPInvokeCallback(typeof(LocateSpace))]
        static int InterceptLocate(ulong space, ulong baseSpace, long time, IntPtr pointer)
        {
            int result = nextLocate(space, baseSpace, time, pointer);
            var correction = Volatile.Read(ref alignment);
            if (result < 0 || pointer == IntPtr.Zero || correction == null ||
                baseSpace != unchecked((ulong)Interlocked.Read(ref appSpace))) return result;
            lock (spacesLock) if (!inputSpaces.Contains(space)) return result;
            var location = Marshal.PtrToStructure<SpaceLocation>(pointer);
            correction.Transform(ref location);
            Marshal.StructureToPtr(location, pointer, false);
            // Rotate optional velocities into the same basis; preserve every validity flag.
            IntPtr next = location.next;
            for (int i = 0; next != IntPtr.Zero && i < 16; i++)
            {
                int type = Marshal.ReadInt32(next);
                IntPtr following = Marshal.ReadIntPtr(next, 8);
                if (type == 43)
                {
                    var velocity = Marshal.PtrToStructure<SpaceVelocity>(next);
                    correction.Transform(ref velocity);
                    Marshal.StructureToPtr(velocity, next, false);
                }
                next = following;
            }
            Interlocked.Increment(ref correctedCount);
            return result;
        }
    }
}
