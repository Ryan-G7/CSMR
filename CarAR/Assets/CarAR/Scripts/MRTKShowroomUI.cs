using MixedReality.Toolkit;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarAR
{
    public class MRTKShowroomUI : MonoBehaviour
    {
        public CarShowroom showroom;
        public MRTKControllerInput controllers;
        public Transform controlPanel;
        public StatefulInteractable[] buttons;
        public TMP_Text status;
        public int ConfirmedCount { get; private set; }
        public Transform ControlPanel => controlPanel;
        float noticeUntil;
        string notice;
        bool settingsPage;
        readonly int[] main = { 5, 4, 0, 1, 2, 27, 3, 16, 17, 25, 24, 26 };
        readonly int[] settings = { 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 18, 19, 20, 21, 22, 23, 24, 25, 26 };

        void Start()
        {
            Bind(0, showroom.leftDoor.Toggle); Bind(1, showroom.rightDoor.Toggle); Bind(2, showroom.trunk.Toggle);
            Bind(3, () =>
            {
                if (!showroom.placement.IsPlaced) return;
                showroom.gestures.SetManipulation(false); showroom.explode.Toggle();
            });
            Bind(4, ToggleLock); Bind(5, controllers.BeginPlacement);
            Bind(6, () => TransformCommand(() => showroom.gestures.MoveBy(-ViewRight() * .1f)));
            Bind(7, () => TransformCommand(() => showroom.gestures.MoveBy(ViewRight() * .1f)));
            Bind(8, () => TransformCommand(() => showroom.gestures.MoveBy(ViewForward() * .1f)));
            Bind(9, () => TransformCommand(() => showroom.gestures.MoveBy(-ViewForward() * .1f)));
            Bind(10, () => TransformCommand(() => showroom.gestures.ScaleBy(1 / 1.1f)));
            Bind(11, () => TransformCommand(() => showroom.gestures.ScaleBy(1.1f)));
            Bind(12, () => TransformCommand(() => showroom.gestures.RotateBy(-5)));
            Bind(13, () => TransformCommand(() => showroom.gestures.RotateBy(5)));
            Bind(14, () => TransformCommand(() => showroom.placement.AdjustFloor(.01f)));
            Bind(15, () => TransformCommand(() => showroom.placement.AdjustFloor(-.01f)));
            Bind(16, () => TransformCommand(() => showroom.gestures.ScaleBy(1 / showroom.car.localScale.x)));
            Bind(17, () => TransformCommand(() => showroom.gestures.ScaleBy(.25f / showroom.car.localScale.x)));
            Bind(18, () => showroom.lighting.AdjustManual(.1f, 0));
            Bind(19, () => showroom.lighting.AdjustManual(-.1f, 0));
            Bind(20, () => showroom.lighting.AdjustManual(0, -500));
            Bind(21, () => showroom.lighting.AdjustManual(0, 500));
            Bind(22, () => showroom.lighting.RotateManual(15));
            Bind(23, showroom.lighting.RequestAuto);
            Bind(24, () => GetComponent<MRPresentation>().ToggleQuality());
            Bind(25, () => { settingsPage = !settingsPage; SetPage(); });
            Bind(26, TogglePanel);
            Bind(27, showroom.hood.Toggle);
            SetPage(); controlPanel.gameObject.SetActive(false);
        }
        void Bind(int index, UnityEngine.Events.UnityAction action)
        {
            buttons[index].OnClicked.AddListener(action);
            buttons[index].OnClicked.AddListener(() => { ConfirmedCount++; Debug.Log("[CarAR UI] Controller selected " + buttons[index].name); });
        }
        void ToggleLock()
        {
            if (!showroom.placement.IsPlaced) { Notice("正在放置汽车，请稍候"); return; }
            if (showroom.explode.IsBusy) { Notice("请先组装还原，再解锁车辆"); return; }
            showroom.gestures.SetManipulation(!showroom.gestures.ManipulationEnabled);
        }
        void SetPage()
        {
            foreach (var button in buttons) button.gameObject.SetActive(false);
            var page = settingsPage ? settings : main;
            for (int i = 0; i < page.Length; i++)
            {
                var button = buttons[page[i]];
                button.gameObject.SetActive(true);
                button.GetComponent<RectTransform>().anchoredPosition = new Vector2((i % 3 - 1) * 220, 80 - i / 3 * 78);
            }
            controlPanel.GetComponent<RectTransform>().sizeDelta = new Vector2(700, settingsPage ? 840 : 610);
            SetLabel(buttons[25], settingsPage ? "返回评审" : "精细调整");
        }
        static Vector3 ViewForward()
        {
            var direction = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
            return direction.sqrMagnitude > .1f ? direction : Vector3.forward;
        }
        static Vector3 ViewRight() => Vector3.Cross(Vector3.up, ViewForward());
        void TransformCommand(System.Action command)
        {
            if (!showroom.placement.IsPlaced) { Notice("正在放置汽车，请稍候"); return; }
            if (showroom.explode.IsBusy) { Notice("请先组装还原，再移动汽车"); return; }
            if (!showroom.gestures.ManipulationEnabled) { Notice("请先按“解除锁定”"); return; }
            command();
        }
        public void Notice(string message) { notice = message; noticeUntil = Time.unscaledTime + 4; }
        public void TogglePanel()
        {
            if (controllers.IsPlacing) { controllers.CancelPlacement(); controlPanel.gameObject.SetActive(false); }
            bool visible = !controlPanel.gameObject.activeSelf;
            controlPanel.gameObject.SetActive(visible);
            if (visible) PositionPanel();
            Debug.Log("[CarAR UI] Controller panel " + (visible ? "opened" : "closed"));
        }
        public void PositionPanel()
        {
            var cam = Camera.main.transform;
            var forward = ViewForward();
            // Anchor on invocation only. Walking or turning the head never drags the panel.
            controlPanel.SetPositionAndRotation(cam.position + forward * 1.45f - Vector3.up * .12f,
                Quaternion.LookRotation(forward));
        }
        void LateUpdate()
        {
            float scale = showroom.car.localScale.x;
            status.text = $"AMG GT 整车评审 · {scale:F2} 倍 · 长 {showroom.gestures.ModelSize.z * scale:F2} 米\n" +
                "菜单 / Y / B：面板 · 扳机：点选 · 选择停放位置：对齐地面\n" +
                (showroom.gestures.ManipulationEnabled ? "已解锁 · 左摇杆平移 / 右摇杆旋转缩放 / 双握键缩放" : "已停车锁定 · 绕车观察 / 点选车门与行李厢");
            if (settingsPage) status.text += "\n" + showroom.lighting.DisplayStatus + " · " + showroom.placement.DisplayStatus;
            if (Time.unscaledTime < noticeUntil) status.text += "\n" + notice;
            SetLabel(buttons[4], showroom.gestures.ManipulationEnabled ? "停车锁定" : "解除锁定");
            SetLabel(buttons[3], showroom.explode.IsExploded ? "组装还原" : "整车爆炸");
            SetLabel(buttons[24], GetComponent<MRPresentation>().HighQuality ? "切换流畅画质" : "切换精细画质");
            if (Application.isFocused && Keyboard.current?.hKey.wasPressedThisFrame == true) TogglePanel();
            if (Application.isFocused && Keyboard.current?.mKey.wasPressedThisFrame == true) ToggleLock();
        }
        static void SetLabel(StatefulInteractable button, string text)
        {
            foreach (var label in button.GetComponentsInChildren<TMP_Text>(true))
                if (label.gameObject.name == "Text" && label.text != text) label.text = text;
        }
    }
}
