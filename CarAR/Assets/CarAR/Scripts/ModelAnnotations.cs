using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using MixedReality.Toolkit.UX.Experimental;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;

namespace CarAR
{
    public sealed class ModelAnnotations : MonoBehaviour
    {
        [Serializable] public class Entry { public string text; public Vector3 point, normal; }
        [Serializable] class Document { public List<Entry> entries = new(); }
        public delegate bool RayProvider(out RaycastHit hit);
        public Transform modelRoot;
        public RuntimeGlbLoader loader;
        public Camera viewCamera;
        public TMP_FontAsset font;
        public RayProvider AimAtModel;
        public bool IsDesktop;
        public bool AwaitingPlacement { get; private set; }
        public bool Editing { get; private set; }
        public int Count => entries.Count;
        public IReadOnlyList<Entry> Entries => entries;
        readonly List<Entry> entries = new();
        readonly List<GameObject> visuals = new();
        Entry pending;
        ReviewSnapshot snapshotTarget;
        string draft = "";
        string storagePath;
        float armedAt;
        InputAction leftTrigger, rightTrigger;
        NonNativeKeyboard keyboard;
        Transform visualRoot;

        void Start()
        {
            viewCamera ??= Camera.main;
            visualRoot = new GameObject("Model annotations").transform;
            SyncVisualRoot();
            leftTrigger = new InputAction("Left annotation trigger", InputActionType.Button, "<XRController>{LeftHand}/triggerPressed");
            rightTrigger = new InputAction("Right annotation trigger", InputActionType.Button, "<XRController>{RightHand}/triggerPressed");
            leftTrigger.AddBinding("<XRController>{LeftHand}/select");
            rightTrigger.AddBinding("<XRController>{RightHand}/select");
            leftTrigger.Enable(); rightTrigger.Enable();
            if (loader != null)
            {
                loader.ModelChanged += Reload;
                if (!string.IsNullOrEmpty(loader.CurrentPath)) Reload();
            }
        }
        public void BeginPlacement()
        {
            Cancel();
            AwaitingPlacement = true;
            armedAt = Time.unscaledTime + .25f;
        }
        public void BeginSnapshotAnnotation(ReviewSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrEmpty(snapshot.LastPath)) return;
            Cancel();
            snapshotTarget = snapshot;
            Editing = true;
            draft = "";
            OpenKeyboard();
        }
        public void Cancel()
        {
            AwaitingPlacement = false;
            Editing = false;
            snapshotTarget = null;
            if (keyboard != null)
            {
                keyboard.OnTextSubmit.RemoveListener(SubmitKeyboard);
                keyboard.Close();
                keyboard = null;
            }
        }
        void Update()
        {
            if (!AwaitingPlacement || Editing || Time.unscaledTime < armedAt) return;
            if (IsDesktop)
            {
                var mouse = Mouse.current;
                if (mouse?.leftButton.wasPressedThisFrame == true && viewCamera != null &&
                    Physics.Raycast(viewCamera.ScreenPointToRay(mouse.position.ReadValue()), out var mouseHit, 100, Physics.AllLayers) &&
                    mouseHit.transform.IsChildOf(modelRoot)) Prepare(mouseHit);
                return;
            }
            bool pressed = leftTrigger.WasPressedThisFrame() || rightTrigger.WasPressedThisFrame();
            if (pressed && AimAtModel != null && AimAtModel(out var hit) && hit.transform.IsChildOf(modelRoot)) Prepare(hit);
        }
        void Prepare(RaycastHit hit)
        {
            pending = new Entry
            {
                point = modelRoot.InverseTransformPoint(hit.point),
                normal = modelRoot.InverseTransformDirection(hit.normal).normalized
            };
            AwaitingPlacement = false;
            Editing = true;
            draft = "";
            OpenKeyboard();
        }
        void OpenKeyboard()
        {
            if (IsDesktop) return;
            keyboard = NonNativeKeyboard.Instance;
            if (keyboard != null)
            {
                keyboard.SubmitOnEnter = true;
                keyboard.OnTextSubmit.AddListener(SubmitKeyboard);
                keyboard.Open("");
                return;
            }
            if (snapshotTarget != null)
            {
                snapshotTarget.AddNote("快照注释");
                snapshotTarget = null;
            }
            else
            {
                pending.text = "标注 " + (entries.Count + 1);
                Add(pending);
            }
            Editing = false;
        }
        void SubmitKeyboard(string text)
        {
            if (keyboard != null) keyboard.OnTextSubmit.RemoveListener(SubmitKeyboard);
            keyboard = null;
            Submit(text);
        }
        public void Submit(string text)
        {
            if (!Editing) return;
            if (snapshotTarget != null)
            {
                snapshotTarget.AddNote(text);
                snapshotTarget = null;
            }
            else
            {
                pending.text = string.IsNullOrWhiteSpace(text) ? "标注 " + (entries.Count + 1) : text.Trim();
                Add(pending);
            }
            Editing = false;
            draft = "";
        }
        void Add(Entry entry)
        {
            entries.Add(entry);
            CreateVisual(entry);
            Save();
        }
        public void RemoveLast()
        {
            if (entries.Count == 0) return;
            int index = entries.Count - 1;
            Destroy(visuals[index]);
            visuals.RemoveAt(index);
            entries.RemoveAt(index);
            Save();
        }
        void CreateVisual(Entry entry)
        {
            SyncVisualRoot();
            var root = new GameObject("Annotation: " + entry.text);
            root.transform.SetParent(visualRoot, false);
            root.transform.localPosition = entry.point + entry.normal * .025f;
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Anchor";
            marker.transform.SetParent(root.transform, false);
            marker.transform.localScale = Vector3.one * .035f;
            Destroy(marker.GetComponent<Collider>());
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.color = new Color(.1f, .8f, 1f);
            marker.GetComponent<Renderer>().sharedMaterial = material;
            var label = new GameObject("Text", typeof(TextMeshPro));
            label.transform.SetParent(root.transform, false);
            label.transform.localPosition = entry.normal * .07f;
            var text = label.GetComponent<TextMeshPro>();
            text.text = entry.text;
            if (font != null) text.font = font;
            text.fontSize = 2;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.outlineWidth = .15f;
            text.outlineColor = Color.black;
            text.GetComponent<RectTransform>().sizeDelta = new Vector2(1.8f, .4f);
            label.transform.localScale = new Vector3(-1, 1, 1);
            if (viewCamera != null) label.transform.rotation = viewCamera.transform.rotation * Quaternion.Euler(0, 180, 0);
            visuals.Add(root);
        }
        void LateUpdate()
        {
            SyncVisualRoot();
            if (viewCamera == null) return;
            foreach (var visual in visuals)
                if (visual != null) visual.transform.GetChild(1).rotation =
                    viewCamera.transform.rotation * Quaternion.Euler(0, 180, 0);
        }
        void SyncVisualRoot()
        {
            if (visualRoot == null || modelRoot == null) return;
            visualRoot.SetPositionAndRotation(modelRoot.position, modelRoot.rotation);
            visualRoot.localScale = modelRoot.lossyScale;
            visualRoot.gameObject.SetActive(modelRoot.gameObject.activeInHierarchy);
        }
        void Reload()
        {
            foreach (var visual in visuals) Destroy(visual);
            visuals.Clear(); entries.Clear(); Cancel();
            storagePath = Path.Combine(Application.persistentDataPath, "Annotations", Hash(loader.CurrentPath) + ".json");
            if (!File.Exists(storagePath)) return;
            try
            {
                var document = JsonUtility.FromJson<Document>(File.ReadAllText(storagePath));
                if (document?.entries == null) return;
                foreach (var entry in document.entries) { entries.Add(entry); CreateVisual(entry); }
            }
            catch (Exception exception) { Debug.LogWarning("[CarAR Annotations] Could not load " + storagePath + ": " + exception.Message); }
        }
        void Save()
        {
            if (string.IsNullOrEmpty(storagePath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(storagePath));
            File.WriteAllText(storagePath, JsonUtility.ToJson(new Document { entries = entries }, true));
        }
        static string Hash(string value)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? "default"))).Replace("-", "").Substring(0, 24);
        }
        void OnGUI()
        {
            if (!IsDesktop || !Editing) return;
            var rect = new Rect((Screen.width - 460) / 2, (Screen.height - 150) / 2, 460, 150);
            GUI.Box(rect, snapshotTarget == null ? "添加文字标注" : "注释当前快照");
            GUI.SetNextControlName("AnnotationText");
            draft = GUI.TextField(new Rect(rect.x + 20, rect.y + 45, 420, 35), draft);
            if (Event.current.type == EventType.Repaint) GUI.FocusControl("AnnotationText");
            if (GUI.Button(new Rect(rect.x + 235, rect.y + 95, 95, 32), "取消")) Cancel();
            if (GUI.Button(new Rect(rect.x + 345, rect.y + 95, 95, 32), "保存") || Event.current.keyCode == KeyCode.Return) Submit(draft);
        }
        void OnDestroy()
        {
            if (loader != null) loader.ModelChanged -= Reload;
            leftTrigger?.Dispose(); rightTrigger?.Dispose();
            if (visualRoot != null) Destroy(visualRoot.gameObject);
        }
    }
}
