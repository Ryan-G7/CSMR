# 911 大厅 MR 整车评审

Unity 工程：本目录 `CarAR`；Unity 6000.6.3f1。厂家链路为 Windows / Pimax OpenXR / 荟镜 HJXR 透视，不是 Android APK。

## 运行

```powershell
& D:/Code/CSMR/CarAR/Launch/Start-HuijingMR.ps1
```

默认调用 `D:/Soft/AIXR Agent` 的运行时、API Layer、SDK，并启动或复用厂家后台。环境变量只传给本次应用进程；不修改 OpenXR 注册表，不替换已安装 DLL，不调用 SaveConfigToFile。Unity 保留透明 Alpha，VST 在背景，车辆在前景；显式请求关闭 VR head lock。

请先完成头显定位、手柄配对和运行时地面标定。PC 版本等待头显佩戴和位置跟踪恢复后，才开始首次地面搜索，避免用未佩戴时缓存的旧位姿摆放车辆。首次找到足够大的水平面后自动放置；设备没有平面数据时，8 秒后放到视线前方约 4.5 米。有效的 OpenXR Floor 优先；地面参考异常时按 1.5 米眼高估计，并明确显示为估计。首次放置后不再随平面更新或头部运动调整车辆。

默认车辆缩放为 1，当前模型包围盒约长 4.382 × 宽 1.981 × 高 1.259 米。1:1 指 GLB 原始模型单位；未经原厂尺寸校准。

## 头显操作

大厅首次摆放建议按以下顺序进行：

1. 佩戴头显，等待位置跟踪恢复和首次放置（无平面数据时约 8 秒）。
2. 菜单 / Y / B 呼出面板，选择“选择停放位置”，把整车占地边框指向大厅地面；摇杆上下调整预览高度。
3. 扳机确认。车辆停放并锁定；需要微调时解除锁定，在“精细调整”内按 10cm / 5° / 地面 ±1cm 修正，完成后再次停车锁定。
4. 收起面板绕车评审。门盖与爆炸 / 组装在停车状态下也能操作；要移动或缩放整车时再解锁。

- 菜单键、左手 Y 或右手 B：在当前视野前方显示 / 收起面板。面板只在呼出时定位，此后固定在世界中。注视停留和头部准星已移除。
- 扳机：用手柄射线点选按钮、车门或前行李厢。停车锁定时仍可开关门盖与播放爆炸 / 组装。
- 解除锁定后，握键按住车身可拖动；两只手柄同时握住车身并拉开 / 靠近可缩放。MRTK ObjectManipulator 处理变换；约束为水平平移、绕竖轴旋转、0.1–2 倍缩放。
- 解除锁定后，左摇杆平移，右摇杆左右旋转、上下缩放。握键正在抓取车辆时，摇杆不同时修改整车变换。
- “选择停放位置”：收起面板，射线指向地面显示整车占地轮廓；摇杆上下调整预览地面高度；扳机确认位置与高度并锁定车辆；菜单 / Y / B 取消。确认前不会移动已停放的车辆。
- “精细调整”：每次平移 10cm、旋转 5°、缩放 10%、地面 ±1cm，以及光强 / 色温调整和自动光照请求。
- “1:1 尺寸”和“1:4 展示”需要先解锁。摆放好后选择“停车锁定”，避免误操作。
- “切换流畅画质”：原生分辨率、4096 阴影和 SSAO，与 80% 分辨率、2048 阴影、关闭 SSAO 两档切换；均保持 4x MSAA 与 MR 透明背景。

当前采用标准 OpenXR Touch / Index / Vive / Simple / Microsoft 控制器配置；按键名称随手柄外形而异。Pimax 运行时在头显未佩戴、OpenXR 会话未进入 Focused 时会暂停手柄同步；已配对手柄也可能暂时不出现在应用中。Windows 桌面窗口失去焦点时仍接收 XR 输入；键盘快捷键仅在应用窗口有焦点时响应。

## 渲染与环境数据

车身使用 Unity URP Complex Lit 不透明金属清漆；车窗和灯罩保留透明。使用原生反射探针卷积生成大厅预设反射、URP 原生 SSAO、4096 软阴影、透明地面接收阴影及柔和接触阴影。纹理采用 16 倍各向异性过滤，取消负 mip 偏移以减少走动时纹理闪烁。

大厅反射与默认灯光是预设，不是现场采集。ARFoundation 已请求环境光强、颜色、主光方向、球谐与环境反射探针；只有设备实际提供数据时才会自动更新。当前 HJXR demo / 文档没有可直接供 Unity 读取的实时测光或环境贴图接口，运行中没有收到此类数据时会显示“大厅光照预设”，允许手动调光强、色温与方向。HDR 中间颜色缓冲关闭，以保留厂家合成需要的 Alpha。

## 空间稳定性

车辆是独立世界根对象，未挂在相机或 XR Origin 下；默认禁用操控，释放抓取后不保留速度。没有任何停车状态下跟随头部或持续追地的逻辑。

`huijing-mr.log` 中 `[CarAR Stability]` 每 5 秒记录车辆位置 / 角度变化、头部变化、跟踪标志、佩戴状态（若运行时提供）、Origin、帧率和手柄状态。车辆根坐标稳定不等于透视合成已经对齐；走动时的视觉晃动还可能来自跟踪、参考空间、视频与位姿时延。厂家手册第 6 页说明了 VST 位姿时间和 VSYNC 偏移校准，应用不猜测这些数值，也不自动修改校准。

厂家实体标记可见且虚拟标记正确配置时，K 请求异步 WorldSync；该返回值不能证明已经识别成功。普通大厅手柄摆放不需要按 K。R 重新放到当前视线前方，H 呼出 / 收起面板，M 切换锁定。

## 桌面预览与验证

直接启动 `Build/Windows/CarAR.exe` 是桌面预览，不占用头显。鼠标右键环绕、滚轮缩放视角；1 / 2 / 3 开关门盖；Space 爆炸 / 组装；左侧滑块明确标为模拟光照。

```powershell
Start-Process -FilePath 'C:/Program Files/Unity/Hub/Editor/6000.6.3f1/Editor/Unity.exe' -ArgumentList '-batchmode -quit -projectPath D:/Code/CSMR/CarAR -executeMethod CarAR.EditorTools.CarARSetupWizard.BuildPreview -logFile D:/Code/CSMR/CarAR/controller-review-build.log' -Wait
Start-Process -FilePath './Build/Windows/CarAR.exe' -ArgumentList '--carar-smoke -force-d3d11 -screen-fullscreen 0 -screen-width 1440 -screen-height 900 -logFile D:/Code/CSMR/CarAR/controller-review-smoke.log' -Wait
```

自动验证通过 Input System 注入测试控制器，驱动真实 XRI 射线、扳机与握键，不以直接调用按钮事件代替控制器测试；也覆盖原生 MRTK 单 / 双手操作、摇杆、放置高度、锁定、门盖、爆炸组装和模拟测光响应。它不替代实际手柄、佩戴走动和厂家最终透视合成验证。

2026-10-19 的最终编译及三组自动验证通过（退出码 0）。真机记录 `controller-review-device-worn.log` 已观察到左右 Touch 配置手柄被跟踪、真实扳机选中“解除锁定”和缩放按钮，佩戴期间约 72 FPS；佩戴者也确认有射线且能打开面板。停车时头部位置变化而车辆根坐标保持不变。此记录证明输入已到达应用，不能单独证明用户眼中的视频与虚拟车已完全对齐。

重启后的 `controller-review-device-final.log` 记录了未佩戴时等待、佩戴后首次放置，以及实际使用“选择停放位置”后的新停车坐标。相邻采样间头部位移约 2.1 米时，锁定车辆的位置和角度变化均为零；手动重新停放后也继续保持固定。

UI 检查图为 `Build/Windows/controller-panel.png` 和 `controller-adjustments.png`。`xr-scene.png` 与 `xr-car-quality.png` 是 Unity 另行渲染的单眼诊断图，不包含实际相机视频。不要将其视作头显透视画面截图。

## 模型边界

原始 `Assets/Models/911.glb` 保留，没有自带动画。`Tools/prepare_911.py` 生成 `911-interactive.glb`，按空间区域近似拆分左右门和前行李厢并添加铰链。爆炸 / 组装为展示动画，不是机械装配顺序。正式接缝、内饰、开门机构及尺寸评审需要准确分件与标定的原厂模型。911 的行李厢在车头，后盖为发动机舱盖。

MRTK 依赖及 Unity 6 兼容改动见 `MRTK3-MIGRATION.md`。
