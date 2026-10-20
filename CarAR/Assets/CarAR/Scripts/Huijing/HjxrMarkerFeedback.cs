using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CarAR.Huijing
{
    // The installed SDK has no implemented completion getter. Read only this app's
    // native output from this request, including the detector's completion record.
    // File append time alone is insufficient: native stdout can flush an older cycle
    // after a new request. A calculated matrix still requires physical verification.
    public sealed class HjxrMarkerFeedback : IDisposable
    {
        public enum Outcome { Waiting, CameraUnavailable, DetectionStopped, AlignmentCalculated }
        public Outcome State { get; private set; }
        FileStream input;
        readonly byte[] buffer = new byte[16384];
        string fragment = "";
        int imageFailures;
        int matrixRow = -1;
        Matrix4x4 candidate;
        readonly Matrix4x4 targetMarker;
        readonly DateTime? requestStarted;
        static readonly Regex nativeRecord = new Regex(
            @"(?<time>\d{2}:\d{2}:\d{2}\.\d{3})[ \t]+\d+[ \t]+\d+[ \t]+(?<message>(?:JXR_SDK::JxrWorldManager|MarkerManager)::.*)",
            RegexOptions.CultureInvariant);
        int evidencePriority;
        DateTime matrixTime;
        public Matrix4x4 NativeAlignment { get; private set; }
        public string Evidence { get; private set; }
        public DateTime EvidenceAt { get; private set; }
        public DateTime CompletedAt { get; private set; }
        public int StaleRecordsIgnored { get; private set; }
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        // The installed SDK imports this public CRT function and uses that same
        // CRT's stdout. Flush during calibration so the end-of-cycle record is
        // delivered now, rather than tens of seconds later with another request.
        [DllImport("api-ms-win-crt-stdio-l1-1-0.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int fflush(IntPtr stream);
        static bool canFlush = true;
#endif

        static void FlushNativeOutput()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (!canFlush) return;
            try { fflush(IntPtr.Zero); }
            catch (DllNotFoundException) { canFlush = false; }
            catch (EntryPointNotFoundException) { canFlush = false; }
#endif
        }

        public HjxrMarkerFeedback(string logPath = null, Matrix4x4? targetMarkerPose = null, DateTime? requestedAt = null)
        {
            targetMarker = targetMarkerPose ?? Matrix4x4.identity;
            var started = requestedAt ?? (!string.IsNullOrEmpty(logPath) ? DateTime.Now : (DateTime?)null);
            // Native records have millisecond precision. Match that precision at
            // the request boundary, including a command logged in the same ms.
            requestStarted = started.HasValue ? new DateTime(started.Value.Ticks - started.Value.Ticks % TimeSpan.TicksPerMillisecond) : null;
            if (string.IsNullOrEmpty(logPath)) return;
            try
            {
                FlushNativeOutput();
                input = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                input.Seek(0, SeekOrigin.End);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
            { Dispose(); }
        }

        public void Poll()
        {
            if (input == null || State != Outcome.Waiting) return;
            try
            {
                FlushNativeOutput();
                // Bound disk work even when a broken native detector floods the log.
                for (int i = 0; i < 4 && State == Outcome.Waiting; i++)
                {
                    int count = input.Read(buffer, 0, buffer.Length);
                    if (count == 0) break;
                    Observe(Encoding.ASCII.GetString(buffer, 0, count));
                }
            }
            catch (IOException) { Dispose(); }
        }

        public void Observe(string chunk)
        {
            if (State != Outcome.Waiting || string.IsNullOrEmpty(chunk)) return;
            string text = fragment + chunk;
            int start = 0, end;
            while ((end = text.IndexOf('\n', start)) >= 0)
            {
                string line = text.Substring(start, end - start);
                start = end + 1;
                if (matrixRow >= 0)
                {
                    if (ReadMatrixRow(line))
                    {
                        if (++matrixRow == 4)
                        {
                            matrixRow = -1;
                            if (IsRigid(candidate)) KeepEvidence(candidate, "native final matrix", 3, matrixTime);
                        }
                        continue;
                    }
                    matrixRow = -1;
                }
                if (!TryCurrentRecord(line, out string message, out DateTime recordedAt)) continue;
                if (ReadFinalMarkerPose(message, out var finalMarker))
                {
                    KeepEvidence(finalMarker, "native final marker_t2w", 2, recordedAt);
                }
                else if (ReadCompletedMarkerPose(message, out var localizedMarker))
                    KeepEvidence(localizedMarker, "complete native marker pose (xyzw)", 1, recordedAt);
                else if (message.Contains("fillFrameData:") && message.Contains("to cvmat failed"))
                {
                    if (++imageFailures >= 3) State = Outcome.CameraUnavailable;
                }
                else if (message.Contains("T_slamw2objw mat:")) { matrixRow = 0; candidate = Matrix4x4.zero; matrixTime = recordedAt; }
                else if (message.Contains("detect stop") || message.Contains("timeout, mDetectNum")) State = Outcome.DetectionStopped;
                else if (evidencePriority != 0 && recordedAt >= EvidenceAt &&
                    (message.Contains("frameProcessLoop:") && message.EndsWith("detectMakerInMat return 0", StringComparison.Ordinal) ||
                     message.Contains("waitMakerSem:") && message.EndsWith(" i", StringComparison.Ordinal)))
                {
                    // LocalizeCheck may precede averaging and publishing the
                    // final matrix. Do not reopen the menu or accept another
                    // calibration until the current detector cycle has ended.
                    CompletedAt = recordedAt;
                    State = Outcome.AlignmentCalculated;
                }
                if (State != Outcome.Waiting) break;
            }
            fragment = start < text.Length ? text.Substring(start) : "";
            if (fragment.Length > 4096) fragment = fragment.Substring(fragment.Length - 4096);
        }

        bool TryCurrentRecord(string line, out string message, out DateTime recordedAt)
        {
            message = line.TrimEnd(); recordedAt = default;
            var match = nativeRecord.Match(line);
            if (!match.Success) return !requestStarted.HasValue; // Unstamped, isolated validation traces only.
            message = match.Groups["message"].Value.TrimEnd();
            if (!requestStarted.HasValue) return true;
            if (!TimeSpan.TryParseExact(match.Groups["time"].Value, @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture, out var time)) return false;
            recordedAt = requestStarted.Value.Date + time;
            if (recordedAt - requestStarted.Value > TimeSpan.FromHours(12)) recordedAt = recordedAt.AddDays(-1);
            else if (requestStarted.Value - recordedAt > TimeSpan.FromHours(12)) recordedAt = recordedAt.AddDays(1);
            if (recordedAt < requestStarted.Value) { StaleRecordsIgnored++; return false; }
            return recordedAt <= requestStarted.Value.AddMinutes(1);
        }

        void KeepEvidence(Matrix4x4 matrix, string evidence, int priority, DateTime recordedAt)
        {
            if (priority < evidencePriority || !IsRigid(matrix)) return;
            NativeAlignment = matrix; Evidence = evidence; EvidenceAt = recordedAt; evidencePriority = priority;
        }

        bool ReadFinalMarkerPose(string line, out Matrix4x4 alignment)
        {
            alignment = default;
            if (!line.Contains("JxrWorldManager::detectMakerInMat:")) return false;
            int at = line.IndexOf("marker_t2w:", StringComparison.Ordinal);
            if (at < 0) return false;
            var values = line.Substring(at + 11).Split(new[] { ' ', '\t', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (values.Length != 16) return false;
            var marker = Matrix4x4.zero;
            // The SDK's one-line GLM dump is column-major. This is the final
            // selected/averaged marker pose, unlike intermediate LocalizeCheck.
            for (int i = 0; i < values.Length; i++)
            {
                if (!float.TryParse(values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value)) return false;
                marker[i % 4, i / 4] = value;
            }
            if (!IsRigid(marker)) return false;
            alignment = targetMarker * marker.inverse;
            return IsRigid(alignment);
        }

        bool ReadCompletedMarkerPose(string line, out Matrix4x4 alignment)
        {
            alignment = default;
            // The native thread's multi-line matrix can be interrupted by Unity's
            // log writer. Its completed localization pose is one atomic log line.
            // Captured 2026-10-19: inverse(T_maintag2slamw) exactly reproduces the
            // SDK's T_slamw2objw for the configured identity marker target.
            if (!line.Contains("MarkerManager::poseCalculationPipe_check:") || !line.Contains("localize success by LocalizeCheck")) return false;
            int poseAt = line.IndexOf("||tq=", StringComparison.Ordinal);
            int confidenceAt = line.IndexOf("||confidence=", StringComparison.Ordinal);
            if (confidenceAt < 0 || poseAt <= confidenceAt) return false;
            string confidenceText = line.Substring(confidenceAt + 13, poseAt - confidenceAt - 13).Trim();
            if (!float.TryParse(confidenceText, NumberStyles.Float, CultureInfo.InvariantCulture, out float confidence) ||
                !float.IsFinite(confidence) || confidence <= 0 || confidence > 1) return false;
            var values = line.Substring(poseAt + 5).Trim().Split(',');
            if (values.Length != 7) return false;
            var numbers = new float[7];
            for (int i = 0; i < numbers.Length; i++)
                if (!float.TryParse(values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]) || !float.IsFinite(numbers[i])) return false;
            var rotation = new Quaternion(numbers[3], numbers[4], numbers[5], numbers[6]);
            if (Mathf.Abs(Quaternion.Dot(rotation, rotation) - 1) > .003f) return false;
            var markerInSlam = Matrix4x4.TRS(new Vector3(numbers[0], numbers[1], numbers[2]), rotation.normalized, Vector3.one);
            var calculated = targetMarker * markerInSlam.inverse;
            if (!IsRigid(calculated)) return false;
            alignment = calculated;
            return true;
        }

        bool ReadMatrixRow(string line)
        {
            var values = line.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (values.Length != 4) return false;
            for (int column = 0; column < 4; column++)
            {
                if (!float.TryParse(values[column], NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ||
                    float.IsNaN(value) || float.IsInfinity(value)) return false;
                candidate[matrixRow, column] = value;
            }
            return true;
        }
        static bool IsRigid(Matrix4x4 matrix)
        {
            if (Mathf.Abs(matrix.m30) + Mathf.Abs(matrix.m31) + Mathf.Abs(matrix.m32) > .001f || Mathf.Abs(matrix.m33 - 1) > .001f) return false;
            Vector3 x = matrix.GetColumn(0), y = matrix.GetColumn(1), z = matrix.GetColumn(2);
            return Mathf.Abs(x.sqrMagnitude - 1) < .003f && Mathf.Abs(y.sqrMagnitude - 1) < .003f && Mathf.Abs(z.sqrMagnitude - 1) < .003f &&
                Mathf.Abs(Vector3.Dot(x,y)) < .003f && Mathf.Abs(Vector3.Dot(x,z)) < .003f && Mathf.Abs(Vector3.Dot(y,z)) < .003f &&
                Mathf.Abs(Vector3.Dot(Vector3.Cross(x,y),z) - 1) < .003f;
        }

        public void Dispose() { input?.Dispose(); input = null; }
    }
}
