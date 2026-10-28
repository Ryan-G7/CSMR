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
        int page, colorIndex;
        readonly int[] main = { 5, 4, 0, 3, 1, 27, 16, 43, 36, 25, 24, 26 };
        readonly int[] settings = { 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26 };
        readonly int[] materials = { 2, 28, 29, 30, 31, 32, 33, 34, 35, 38, 39, 40, 41, 42, 1, 25, 26 };

        void Start()
        {
            Bind(0, () => showroom.modelLoader.PickFile());
            Bind(1, () => showroom.review.SetClipping(!showroom.review.Clipping));
            Bind(2, () => showroom.review.SelectMaterial(-1));
            Bind(3, () =>
            {
                if (!showroom.placement.IsPlaced && !showroom.explode.IsExploded) return;
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
            Bind(23, () =>
            {
                showroom.lighting.RequestAuto();
                Notice(showroom.lighting.HasCameraProvider ? "等待设备输出有效测光，期间使用 HDR" : "使用静态 HDR");
            });
            Bind(24, () => GetComponent<MRPresentation>().ToggleQuality());
            Bind(25, () => { page = page == 0 ? 1 : 0; SetPage(); });
            Bind(26, TogglePanel);
            Bind(27, () => { showroom.snapshot.Capture(); Notice("快照已保存\n" + showroom.snapshot.LastPath); });
            Bind(28, () => showroom.review.SelectMaterial(1));
            Bind(29, () =>
            {
                Color[] colors = { new(.8f, .04f, .03f), new(.9f, .9f, .9f), new(.03f, .25f, .8f), new(.08f, .55f, .25f) };
                colorIndex = (colorIndex + 1) % colors.Length;
                var color = colors[colorIndex]; color.a = showroom.review.ColorValue.a; showroom.review.SetColor(color);
            });
            Bind(30, () => showroom.review.SetMetallic(showroom.review.Metallic + .1f));
            Bind(31, () => showroom.review.SetMetallic(showroom.review.Metallic - .1f));
            Bind(32, () => showroom.review.SetRoughness(showroom.review.Roughness + .1f));
            Bind(33, () => showroom.review.SetRoughness(showroom.review.Roughness - .1f));
            Bind(34, () => showroom.review.SetOpacity(showroom.review.ColorValue.a + .1f));
            Bind(35, () => showroom.review.SetOpacity(showroom.review.ColorValue.a - .1f));
            Bind(36, () => { showroom.annotations.BeginPlacement(); controlPanel.gameObject.SetActive(false); Notice("用射线指向模型并按扳机选点"); });
            Bind(37, showroom.annotations.RemoveLast);
            Bind(38, () => showroom.review.SetPlaneAxis(Vector3.right));
            Bind(39, () => showroom.review.SetPlaneAxis(Vector3.up));
            Bind(40, () => showroom.review.SetPlaneAxis(Vector3.forward));
            Bind(41, () => showroom.review.MovePlane(-.1f));
            Bind(42, () => showroom.review.MovePlane(.1f));
            Bind(43, () => { page = 2; SetPage(); });
            SetPage(); controlPanel.gameObject.SetActive(false);
        }
        void Bind(int index, UnityEngine.Events.UnityAction action)
        {
            buttons[index].OnClicked.AddListener(action);
            buttons[index].OnClicked.AddListener(() => { ConfirmedCount++; Debug.Log("[CarAR UI] Controller selected " + buttons[index].name); });
        }
        void ToggleLock()
        {
            if (!showroom.placement.IsPlaced) { Notice("正在放置模型，请稍候"); return; }
            if (showroom.explode.IsBusy) { Notice("请先组装还原，再解锁模型"); return; }
            showroom.gestures.SetManipulation(!showroom.gestures.ManipulationEnabled);
        }
        void SetPage()
        {
            foreach (var button in buttons) button.gameObject.SetActive(false);
            var pageButtons = page == 1 ? settings : page == 2 ? materials : main;
            for (int i = 0; i < pageButtons.Length; i++)
            {
                var button = buttons[pageButtons[i]];
                button.gameObject.SetActive(true);
                button.GetComponent<RectTransform>().anchoredPosition = new Vector2((i % 3 - 1) * 220, 80 - i / 3 * 78);
            }
            controlPanel.GetComponent<RectTransform>().sizeDelta = new Vector2(700, page == 0 ? 610 : 840);
            SetLabel(buttons[25], page == 0 ? "精细调整" : "返回评审");
        }
        static Vector3 ViewForward()
        {
            var direction = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
            return direction.sqrMagnitude > .1f ? direction : Vector3.forward;
        }
        static Vector3 ViewRight() => Vector3.Cross(Vector3.up, ViewForward());
        void TransformCommand(System.Action command)
        {
            if (!showroom.placement.IsPlaced) { Notice("正在放置模型，请稍候"); return; }
            if (showroom.explode.IsBusy) { Notice("请先组装还原，再移动模型"); return; }
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
        }
        public void PositionPanel()
        {
            var cam = Camera.main.transform;
            var forward = ViewForward();
            controlPanel.SetPositionAndRotation(cam.position + forward * 1.45f - Vector3.up * .12f, Quaternion.LookRotation(forward));
        }
        void LateUpdate()
        {
            float scale = showroom.car.localScale.x;
            status.text = $"GLB 评审 · {scale:F2} 倍 · {showroom.modelLoader.Status}\n" +
                "菜单 / Y / B：面板 · 扳机：点选 · 标注：射线选点后输入文字\n" +
                (showroom.gestures.ManipulationEnabled ? "已解锁 · 左摇杆平移 / 右摇杆旋转缩放 / 双握键缩放" : "已停车锁定 / 可爆炸、剖切、编辑材质");
            if (page == 2) status.text += $"\n{showroom.review.SelectedName} · 金属 {showroom.review.Metallic:F2} · 粗糙 {showroom.review.Roughness:F2} · 透明 {showroom.review.ColorValue.a:F2}";
            if (page == 1) status.text += "\n" + showroom.lighting.DisplayStatus + " · " + showroom.placement.DisplayStatus;
            else if (!showroom.placement.IsPlaced) status.text += "\n" + showroom.placement.DisplayStatus;
            if (showroom.annotations.AwaitingPlacement) status.text += "\n请用控制器射线在模型上选择标注位置";
            if (Time.unscaledTime < noticeUntil) status.text += "\n" + notice;
            SetLabel(buttons[4], showroom.gestures.ManipulationEnabled ? "停车锁定" : "解除锁定");
            SetLabel(buttons[3], showroom.explode.IsExploded ? "组装还原" : "模型爆炸");
            SetLabel(buttons[1], showroom.review.Clipping ? "关闭剖切" : "开启剖切");
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
