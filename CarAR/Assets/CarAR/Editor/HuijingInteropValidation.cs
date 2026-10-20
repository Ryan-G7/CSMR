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
            // Replay the observed native failure, including a log line split between reads.
            using (var feedback = new HjxrMarkerFeedback())
            {
                feedback.Observe("JxrWorldManager::Sync:715 o\n[HJXR] Marker alignment requested\n");
                if (feedback.State != HjxrMarkerFeedback.Outcome.Waiting)
                    throw new Exception("Accepting WorldSync must not confirm physical alignment");
                const string failure = "JXR_SDK::JxrWorldManager::fillFrameData:533 err 0 yuv 000002D021ED0290 to cvmat failed\n";
                feedback.Observe(failure + failure + failure.Substring(0, 40));
                if (feedback.State != HjxrMarkerFeedback.Outcome.Waiting)
                    throw new Exception("Incomplete native log line was consumed");
                feedback.Observe(failure.Substring(40));
                if (feedback.State != HjxrMarkerFeedback.Outcome.CameraUnavailable)
                    throw new Exception("Repeated native image failure was not surfaced");
            }
            using (var feedback = new HjxrMarkerFeedback())
            {
                feedback.Observe("JXR_SDK::JxrWorldManager::detectMakerInMat:573 id 1 no data\n");
                if (feedback.State != HjxrMarkerFeedback.Outcome.Waiting)
                    throw new Exception("Initial buffer starvation was mistaken for a terminal error");
                feedback.Observe("T_slamw2objw mat:\n");
                if (feedback.State != HjxrMarkerFeedback.Outcome.Waiting)
                    throw new Exception("An incomplete alignment matrix was accepted");
                // Captured on-device ID-0 calibration: raw head -> aligned head.
                feedback.Observe("0.97759 -0.03706 -0.20723 0.56154\n0.06434 0.98987 0.12653 1.46375\n");
                if (feedback.State != HjxrMarkerFeedback.Outcome.Waiting)
                    throw new Exception("Partial matrix was accepted");
                feedback.Observe("0.20044 -0.13703 0.97008 8.87977\n0 0 0 1\n");
                if (feedback.State != HjxrMarkerFeedback.Outcome.Waiting)
                    throw new Exception("Matrix publication was mistaken for detector completion");
                feedback.Observe("JXR_SDK::JxrWorldManager::frameProcessLoop:409 detectMakerInMat return 0\n");
                if (feedback.State != HjxrMarkerFeedback.Outcome.AlignmentCalculated)
                    throw new Exception("Native alignment calculation output was not observed");
                if (Marshal.SizeOf<HjxrMarkerTrackingFeature.SpacePose>() != 28 || Marshal.SizeOf<HjxrMarkerTrackingFeature.SpaceLocation>() != 56 ||
                    Marshal.SizeOf<HjxrMarkerTrackingFeature.SpaceVelocity>() != 48 || Marshal.OffsetOf<HjxrMarkerTrackingFeature.SpaceLocation>("pose").ToInt32() != 24)
                    throw new Exception("OpenXR input-space ABI mismatch");
                var location = new HjxrMarkerTrackingFeature.SpaceLocation { type = 42, flags = 15,
                    pose = new HjxrMarkerTrackingFeature.SpacePose { position = new Vector3(-2.3454f, -.1432f, -8.3658f), rotation = Quaternion.identity } };
                var correction = new HjxrMarkerTrackingFeature.Alignment(feedback.NativeAlignment);
                correction.Transform(ref location);
                if (Vector3.Distance(location.pose.position, new Vector3(.0076f,.1129f,.3140f)) > .002f || location.flags != 15)
                    throw new Exception("Captured native marker transform did not reproduce the measured aligned head");
                var invalidPosition = location.pose.position; location.flags = 1;
                correction.Transform(ref location);
                if (location.pose.position != invalidPosition) throw new Exception("Invalid position was transformed");
                var velocity = new HjxrMarkerTrackingFeature.SpaceVelocity { flags = 3, linear = Vector3.right, angular = Vector3.up };
                correction.Transform(ref velocity);
                if (Mathf.Abs(velocity.linear.magnitude - 1) > .001f || Mathf.Abs(Vector3.Dot(velocity.linear, velocity.angular)) > .001f)
                    throw new Exception("Native marker input velocity basis was not preserved");
            }
            using (var invalid = new HjxrMarkerFeedback())
            {
                invalid.Observe("T_slamw2objw mat:\nNaN 0 0 0\n0 1 0 0\n0 0 1 0\n0 0 0 1\n");
                if (invalid.State != HjxrMarkerFeedback.Outcome.Waiting) throw new Exception("Non-finite native matrix was accepted");
                invalid.Observe("T_slamw2objw mat:\n2 0 0 0\n0 2 0 0\n0 0 2 0\n0 0 0 1\n");
                if (invalid.State != HjxrMarkerFeedback.Outcome.Waiting) throw new Exception("Non-rigid native matrix was accepted");
            }
            // The 20:09 device trace loses all matrix rows to interleaved native /
            // Unity output. The one-line completed pose remains intact. Compare
            // this alternate evidence against the independently captured 19:26 matrix.
            using (var completed = new HjxrMarkerFeedback())
            {
                completed.Observe("MarkerManager::poseCalculationPipe_check:1237 xxx INFO=localize success by LocalizeCheck\n");
                completed.Observe("MarkerManager::poseCalculationPipe_check:1237 xxx INFO=localize success by LocalizeCheck||confidence= 0.500000 ||tq=-2.422988,-0.211361,-8.682897,0.066409\n");
                if (completed.State != HjxrMarkerFeedback.Outcome.Waiting) throw new Exception("Incomplete marker pose was accepted");
                completed.Observe("MarkerManager::poseCalculationPipe_check:1237 xxx INFO=localize success by LocalizeCheck||confidence= 0.500000 ||tq=-2.422988,-0.211361,-8.682897,0.066409,0.102721,-0.025551,0.992162\n");
                if (completed.State != HjxrMarkerFeedback.Outcome.Waiting) throw new Exception("Preliminary localization ended the detector cycle early");
                completed.Observe("JXR_SDK::JxrWorldManager::frameProcessLoop:409 detectMakerInMat return 0\n");
                if (completed.State != HjxrMarkerFeedback.Outcome.AlignmentCalculated) throw new Exception("Completed native marker pose was not read");
                var expectedRows = new[] { new Vector4(.97759f,-.03706f,-.20723f,.56154f), new Vector4(.06434f,.98987f,.12653f,1.46375f),
                    new Vector4(.20044f,-.13703f,.97008f,8.87977f), new Vector4(0,0,0,1) };
                for (int i = 0; i < 4; i++)
                    if (Vector4.Distance(completed.NativeAlignment.GetRow(i), expectedRows[i]) > .0001f)
                        throw new Exception("Native xyzw pose does not reproduce captured SDK matrix");
            }
            // Device replay: at 20:18:47, stdout still appended the previous
            // 20:18:40 calibration after the new reader had sought to EOF. The
            // old parser accepted it before the new detector even ran.
            using (var correlated = new HjxrMarkerFeedback(requestedAt: new DateTime(2026,10,19,20,18,47,669)))
            {
                correlated.Observe("20:18:40.291 34040\t45840\tMarkerManager::poseCalculationPipe_check:1237 xxx INFO=localize success by LocalizeCheck||confidence= 0.500000 ||tq=-0.926658,-1.605905,1.363527,0.104247,0.194565,-0.032977,0.974777\n" +
                    "20:18:40.293 34040\t45840\tJXR_SDK::JxrWorldManager::detectMakerInMat:653 T_slamw2objw mat:\n" +
                    "1 0 0 10\n0 1 0 20\n0 0 1 30\n0 0 0 1\n" +
                    "20:18:40.293 34040\t45840\tJXR_SDK::JxrWorldManager::frameProcessLoop:409 detectMakerInMat return 0\n");
                if (correlated.State != HjxrMarkerFeedback.Outcome.Waiting || correlated.StaleRecordsIgnored != 3)
                    throw new Exception("Buffered prior calibration was accepted by a new request");
                correlated.Observe("20:18:47.670 34040\t43796\tJXR_SDK::JxrWorldManager::Sync:689 i\n" +
                    "20:18:47.895 34040\t45840\tMarkerManager::poseCalculationPipe_check:1237 xxx INFO=localize success by LocalizeCheck||confidence= 0.500000 ||tq=-0.928165,-1.603711,1.365314,0.090606,0.198338,-0.031193,0.975438\n" +
                    "20:18:47.897 34040\t45840\tJXR_SDK::JxrWorldManager::detectMakerInMat:646 marker_t2w: 0.919378 -0.024912 -0.392585 0.000000 , 0.096794 0.981635 0.164387 0.000000 , 0.381280 -0.189134 0.904905 0.000000 , -0.928165 -1.603711 1.365314 1.000000\n");
                if (correlated.State != HjxrMarkerFeedback.Outcome.Waiting || correlated.Evidence != "native final marker_t2w")
                    throw new Exception("Final GLM pose was lost or ended the detector cycle early");
                var expectedRows = new[] { new Vector4(.91938f,-.02491f,-.39259f,1.34939f), new Vector4(.09679f,.98164f,.16439f,1.43966f),
                    new Vector4(.38128f,-.18913f,.90491f,-1.18490f), new Vector4(0,0,0,1) };
                for (int i = 0; i < 4; i++)
                    if (Vector4.Distance(correlated.NativeAlignment.GetRow(i), expectedRows[i]) > .0001f)
                        throw new Exception("Final marker_t2w did not reproduce the captured SDK matrix");
                correlated.Observe("20:18:47.897 34040\t45840\tJXR_SDK::JxrWorldManager::frameProcessLoop:409 detectMakerInMat return 0\n");
                if (correlated.State != HjxrMarkerFeedback.Outcome.AlignmentCalculated || correlated.CompletedAt.Second != 47)
                    throw new Exception("Current detector completion was not correlated to its request");
            }
            using (var midnight = new HjxrMarkerFeedback(requestedAt: new DateTime(2026,10,19,23,59,59,900)))
            {
                midnight.Observe("23:59:58.000 1\t2\tJXR_SDK::JxrWorldManager::detectMakerInMat:653 T_slamw2objw mat:\n1 0 0 9\n0 1 0 9\n0 0 1 9\n0 0 0 1\n" +
                    "00:00:00.100 1\t2\tJXR_SDK::JxrWorldManager::detectMakerInMat:653 T_slamw2objw mat:\n1 0 0 0\n0 1 0 0\n0 0 1 0\n0 0 0 1\n" +
                    "00:00:00.101 1\t2\tJXR_SDK::JxrWorldManager::frameProcessLoop:409 detectMakerInMat return 0\n");
                if (midnight.State != HjxrMarkerFeedback.Outcome.AlignmentCalculated || midnight.CompletedAt.Day != 20 || midnight.NativeAlignment != Matrix4x4.identity)
                    throw new Exception("Calibration correlation failed across midnight");
            }
            var invalidTranslation = Matrix4x4.identity;
            invalidTranslation.m03 = float.NaN;
            bool rejected = false;
            try { HjxrMarkerTrackingFeature.SetAlignment(invalidTranslation); }
            catch (ArgumentException) { rejected = true; }
            if (!rejected) throw new Exception("Non-finite alignment translation reached the OpenXR input hook");
            string feedbackPath = System.IO.Path.GetTempFileName();
            try
            {
                var requested = new DateTime(2026, 10, 20, 9, 0, 0);
                using (var reader = new HjxrMarkerFeedback(feedbackPath, requestedAt: requested))
                {
                    if (!reader.CanRead) throw new Exception("Readable calibration log was rejected");
                    System.IO.File.AppendAllText(feedbackPath,
                        "09:00:00.001 1 2 JXR_SDK::JxrWorldManager::detectMakerInMat:653 T_slamw2objw mat:\n" +
                        "1 0 0 0\n0 1 0 0\n0 0 1 0\n0 0 0 1\n" +
                        "09:00:00.002 1 2 JXR_SDK::JxrWorldManager::frameProcessLoop:409 detectMakerInMat return 0\n");
                    reader.Poll();
                    if (reader.State != HjxrMarkerFeedback.Outcome.AlignmentCalculated)
                        throw new Exception("Calibration log polling did not consume appended completion evidence");
                }
                using (var locked = new System.IO.FileStream(feedbackPath, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
                using (var reader = new HjxrMarkerFeedback(feedbackPath))
                    if (reader.CanRead) throw new Exception("Locked calibration log was reported as readable");
            }
            finally { System.IO.File.Delete(feedbackPath); }
            Debug.Log("[HJXR Validation] PASS: native ABI and marker coordinate conversion");
            Debug.Log("[HJXR Validation] PASS: asynchronous marker feedback and observed camera failure trace");
            Debug.Log("[HJXR Validation] PASS: captured marker matrix, OpenXR input ABI, pose validity and velocity basis");
            Debug.Log("[HJXR Validation] PASS: buffered prior-result rejection, final marker pose and detector completion, midnight timestamps");
            Debug.Log("[HJXR Validation] PASS: non-finite translation rejection, actual file polling and unreadable-log detection");
        }
    }
}
