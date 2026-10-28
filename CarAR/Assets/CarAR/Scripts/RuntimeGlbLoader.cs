using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GLTFast;
using GLTFast.Logging;
using UnityEngine;

namespace CarAR
{
    public sealed class RuntimeGlbLoader : MonoBehaviour
    {
        public Transform modelRoot;
        public int modelLayer = MRTKControllerInput.VehicleLayer;
        public MRTKCarInteraction interaction;
        public ExplodeController explode;
        public CarPlacementManager placement;
        public string CurrentPath { get; private set; }
        public string Status { get; private set; } = "默认模型";
        public bool IsLoading { get; private set; }
        public int RendererCount => currentContent == null ? 0 : currentContent.GetComponentsInChildren<Renderer>(true).Length;
        public event Action ModelChanged;
        GltfImport currentImport;
        GameObject currentContent;
        CancellationTokenSource cancellation;
        int revision;

        void Start()
        {
            var children = new Transform[modelRoot.childCount];
            for (int i = 0; i < children.Length; i++) children[i] = modelRoot.GetChild(i);
            if (children.Length == 1) currentContent = children[0].gameObject;
            else if (children.Length > 1)
            {
                currentContent = new GameObject("Default model");
                currentContent.transform.SetParent(modelRoot, false);
                foreach (var child in children) child.SetParent(currentContent.transform, false);
            }
            CurrentPath = Path.GetFullPath(Path.Combine(Application.dataPath, "Models/AMG-interactive.glb"));
            if (!string.IsNullOrWhiteSpace(XRLaunch.ModelPath)) _ = Load(XRLaunch.ModelPath);
        }

        public void PickFile()
        {
            if (IsLoading) return;
            var path = GlbFileDialog.Select();
            if (!string.IsNullOrWhiteSpace(path)) _ = Load(path);
        }

        public async Task<bool> Load(string path)
        {
            if (IsLoading || modelRoot == null) return false;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                Status = "模型文件不存在";
                Debug.LogError("[CarAR Model] File not found: " + path);
                return false;
            }
            IsLoading = true;
            Status = "正在加载 " + Path.GetFileName(path);
            int ownRevision = ++revision;
            cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            var gltf = new GltfImport(logger: new ConsoleLogger());
            GameObject container = null;
            try
            {
                bool loaded = await gltf.LoadFile(Path.GetFullPath(path), new Uri(Path.GetFullPath(path)), importSettings: null, cancellationToken: token);
                if (!loaded) throw new InvalidDataException("glTFast could not read this GLB");
                container = new GameObject(Path.GetFileNameWithoutExtension(path));
                container.transform.SetParent(modelRoot, false);
                container.SetActive(false);
                bool instantiated = await gltf.InstantiateMainSceneAsync(container.transform, token);
                if (!instantiated) throw new InvalidDataException("The GLB has no instantiable scene");
                if (ownRevision != revision || token.IsCancellationRequested) throw new OperationCanceledException();
                container.SetActive(true);
                foreach (var child in container.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = modelLayer;
                foreach (var filter in container.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null || filter.GetComponent<MeshCollider>() != null) continue;
                    var collider = filter.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh;
                }
                var bounds = ModelBounds.Local(container.transform);
                container.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                explode?.ResetModel();
                if (currentContent != null)
                {
                    currentContent.SetActive(false);
                    currentContent.transform.SetParent(null, false);
                    Destroy(currentContent);
                }
                currentImport?.Dispose();
                currentImport = gltf;
                currentContent = container;
                CurrentPath = Path.GetFullPath(path);
                Physics.SyncTransforms();
                interaction?.RefreshModel();
                placement?.RefreshModel();
                ModelChanged?.Invoke();
                Status = Path.GetFileName(path) + " · 已加载";
                Debug.Log($"[CarAR Model] Loaded {CurrentPath}; size={interaction?.ModelSize}; renderers={container.GetComponentsInChildren<Renderer>(true).Length}");
                return true;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                gltf.Dispose();
                if (container != null) Destroy(container);
                Status = "加载失败：" + exception.Message;
                Debug.LogError("[CarAR Model] " + path + "\n" + exception);
                return false;
            }
            catch (OperationCanceledException)
            {
                gltf.Dispose();
                if (container != null) Destroy(container);
                Status = "已取消加载";
                return false;
            }
            finally
            {
                if (ownRevision == revision)
                {
                    cancellation?.Dispose();
                    cancellation = null;
                    IsLoading = false;
                }
            }
        }

        void OnDestroy()
        {
            revision++;
            cancellation?.Cancel();
            cancellation?.Dispose();
            currentImport?.Dispose();
        }
    }
}
