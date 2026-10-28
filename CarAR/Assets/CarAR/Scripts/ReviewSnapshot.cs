using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CarAR
{
    public sealed class ReviewSnapshot : MonoBehaviour
    {
        [Serializable] class Document
        {
            public string modelPath, imagePath;
            public List<ModelAnnotations.Entry> modelAnnotations;
            public List<string> notes;
        }
        public Camera sourceCamera;
        public RuntimeGlbLoader loader;
        public ModelAnnotations annotations;
        public string LastPath { get; private set; }
        public IReadOnlyList<string> Notes => notes;
        readonly List<string> notes = new();

        public string Capture()
        {
            if (sourceCamera == null) sourceCamera = Camera.main;
            if (sourceCamera == null) throw new InvalidOperationException("No review camera is available");
            var directory = Path.Combine(Application.persistentDataPath, "Snapshots");
            Directory.CreateDirectory(directory);
            LastPath = Path.Combine(directory, DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + ".png");
            notes.Clear();
            var go = new GameObject("Review snapshot camera");
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.CopyFrom(sourceCamera);
                camera.enabled = false;
                camera.transform.SetPositionAndRotation(sourceCamera.transform.position, sourceCamera.transform.rotation);
                go.AddComponent<UniversalAdditionalCameraData>();
                camera.GetComponent<UniversalAdditionalCameraData>().allowXRRendering = false;
                const int width = 1920, height = 1080;
                var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, 4);
                var previous = RenderTexture.active;
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                var image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(LastPath, image.EncodeToPNG());
                camera.targetTexture = null;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Destroy(image);
                SaveInfo();
                Debug.Log("[CarAR Snapshot] " + LastPath);
                return LastPath;
            }
            finally
            {
                Destroy(go);
            }
        }
        public void AddNote(string text)
        {
            if (string.IsNullOrEmpty(LastPath) || !File.Exists(LastPath)) return;
            notes.Add(string.IsNullOrWhiteSpace(text) ? "快照注释 " + (notes.Count + 1) : text.Trim());
            SaveInfo();
        }
        void SaveInfo()
        {
            var document = new Document
            {
                modelPath = loader != null ? loader.CurrentPath : "",
                imagePath = LastPath,
                modelAnnotations = annotations != null ? new List<ModelAnnotations.Entry>(annotations.Entries) : new List<ModelAnnotations.Entry>(),
                notes = notes
            };
            File.WriteAllText(LastPath + ".json", JsonUtility.ToJson(document, true));
        }
    }
}
