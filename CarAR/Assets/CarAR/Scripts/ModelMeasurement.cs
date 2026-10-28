using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarAR
{
    public sealed class ModelMeasurement : MonoBehaviour
    {
        public enum MeasurementMode { Distance, Curve, Surface }
        public delegate bool RayProvider(out RaycastHit hit);
        public Transform modelRoot;
        public RuntimeGlbLoader loader;
        public Camera viewCamera;
        public RayProvider AimAtModel;
        public bool IsDesktop;
        public bool AwaitingPoint { get; private set; }
        public string Status { get; private set; } = "选择距离、曲线或曲面测量";
        public MeasurementMode Mode { get; private set; }
        public double LastLength { get; private set; }
        public double LastSurfaceArea { get; private set; }
        public double LastMeshArea { get; private set; }
        readonly List<Vector3> points = new();
        InputAction leftTrigger, rightTrigger;
        LineRenderer line;
        Matrix4x4 measuredMatrix;
        float armedAt;

        void Start()
        {
            viewCamera ??= Camera.main;
            leftTrigger = new InputAction("Left measurement trigger", InputActionType.Button, "<XRController>{LeftHand}/triggerPressed");
            rightTrigger = new InputAction("Right measurement trigger", InputActionType.Button, "<XRController>{RightHand}/triggerPressed");
            leftTrigger.AddBinding("<XRController>{LeftHand}/select");
            rightTrigger.AddBinding("<XRController>{RightHand}/select");
            leftTrigger.Enable(); rightTrigger.Enable();
            line = new GameObject("Model measurement").AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.widthMultiplier = .008f;
            line.startColor = line.endColor = new Color(.1f, .9f, 1f);
            line.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (loader != null) loader.ModelChanged += Clear;
        }
        public void Begin(MeasurementMode mode)
        {
            Clear();
            Mode = mode;
            AwaitingPoint = true;
            armedAt = Time.unscaledTime + .25f;
            Status = mode == MeasurementMode.Distance ? "距离：选择两个表面点" : mode == MeasurementMode.Curve ? "曲线：逐点选择，完成后确认" : "曲面：选择要测量的三角形";
        }
        public void Finish()
        {
            if (Mode != MeasurementMode.Curve || points.Count < 2) return;
            AwaitingPoint = false;
            UpdateLength();
        }
        public void Clear()
        {
            AwaitingPoint = false;
            points.Clear();
            LastLength = LastSurfaceArea = LastMeshArea = 0;
            if (line != null) line.positionCount = 0;
            Status = "选择距离、曲线或曲面测量";
        }
        void Update()
        {
            if (!AwaitingPoint || Time.unscaledTime < armedAt) return;
            RaycastHit hit;
            if (IsDesktop)
            {
                var mouse = Mouse.current;
                if (mouse?.leftButton.wasPressedThisFrame != true || viewCamera == null ||
                    !Physics.Raycast(viewCamera.ScreenPointToRay(mouse.position.ReadValue()), out hit, 100, Physics.AllLayers)) return;
            }
            else if ((!leftTrigger.WasPressedThisFrame() && !rightTrigger.WasPressedThisFrame()) || AimAtModel == null || !AimAtModel(out hit)) return;
            if (hit.transform.IsChildOf(modelRoot)) Select(hit);
        }
        void Select(RaycastHit hit)
        {
            if (Mode == MeasurementMode.Surface)
            {
                AwaitingPoint = false;
                Status = MeasureSurface(hit);
                return;
            }
            points.Add(modelRoot.InverseTransformPoint(hit.point));
            if (Mode == MeasurementMode.Distance && points.Count == 2) AwaitingPoint = false;
            UpdateLength();
        }
        string MeasureSurface(RaycastHit hit)
        {
            var filter = hit.transform.GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || !mesh.isReadable) return "曲面：网格不可读取，无法测量";
            var vertices = mesh.vertices;
            int remaining = hit.triangleIndex;
            LastSurfaceArea = LastMeshArea = 0;
            bool found = false;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                var triangles = mesh.GetTriangles(submesh);
                if (!found && remaining >= 0 && remaining < triangles.Length / 3)
                {
                    LastSurfaceArea = Area(filter.transform, vertices, triangles, remaining * 3);
                    found = true;
                }
                for (int i = 0; i < triangles.Length; i += 3) LastMeshArea += Area(filter.transform, vertices, triangles, i);
                remaining -= triangles.Length / 3;
            }
            return found ? $"{filter.name}：面 {LastSurfaceArea:F6} m² · 网格 {LastMeshArea:F6} m²" : "曲面：未命中有效三角形";
        }
        static double Area(Transform transform, Vector3[] vertices, int[] triangles, int index)
        {
            Vector3 a = transform.TransformPoint(vertices[triangles[index]]);
            Vector3 b = transform.TransformPoint(vertices[triangles[index + 1]]);
            Vector3 c = transform.TransformPoint(vertices[triangles[index + 2]]);
            double abx = b.x - a.x, aby = b.y - a.y, abz = b.z - a.z;
            double acx = c.x - a.x, acy = c.y - a.y, acz = c.z - a.z;
            double x = aby * acz - abz * acy, y = abz * acx - abx * acz, z = abx * acy - aby * acx;
            return Math.Sqrt(x * x + y * y + z * z) * .5;
        }
        void UpdateLength()
        {
            if (line == null) return;
            measuredMatrix = modelRoot.localToWorldMatrix;
            line.positionCount = points.Count;
            double length = 0;
            for (int i = 0; i < points.Count; i++)
            {
                var point = modelRoot.TransformPoint(points[i]);
                line.SetPosition(i, point);
                if (i > 0) length += Vector3.Distance(modelRoot.TransformPoint(points[i - 1]), point);
            }
            LastLength = length;
            Status = Mode == MeasurementMode.Distance ? points.Count == 1 ? "距离：再选择一个表面点" : $"距离：{length:F6} m" : $"曲线：{points.Count} 点 · {length:F6} m";
        }
        void LateUpdate()
        {
            if (Mode != MeasurementMode.Surface && points.Count > 0 && measuredMatrix != modelRoot.localToWorldMatrix) UpdateLength();
        }
        void OnDestroy()
        {
            if (loader != null) loader.ModelChanged -= Clear;
            leftTrigger?.Dispose(); rightTrigger?.Dispose();
            if (line != null)
            {
                Destroy(line.sharedMaterial);
                Destroy(line.gameObject);
            }
        }
    }
}
