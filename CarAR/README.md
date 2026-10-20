# AMG GT 大厅 MR 整车评审

Unity 工程：本目录 `CarAR`；Unity 6000.6.3f1。厂家链路为 Windows / Pimax OpenXR / 荟镜 HJXR 透视，不是 Android APK。

2026-10-20 的系统审查、修复清单及尚缺少的正式评审能力见 [代码审查报告](CODE-REVIEW.md)。下文保留了设备排查的历史记录；当前定位板黑框实测边长为 **0.0678m**，旧记录中的 5cm 已被取代。

## 运行

日常运行可直接双击工作区根目录的 `D:\Code\CSMR\启动汽车MR.cmd`。它调用下面的同一启动脚本，自动启动或复用厂家相机后台和 MR 服务器；运行现有构建无需打开 Unity。已有 CarAR 正常运行时不会重复启动实例。新启动的相机后台会等待 USB 就绪回调，最多 30 秒；检测到既有后台的最后状态为断连时，自动重启相机、MR 服务器及应用。

```powershell
& D:/Code/CSMR/CarAR/Launch/Start-HuijingMR.ps1
```

如果头显只能看到车辆、没有真实画面，双击根目录的 `修复透视并重启.cmd`，或运行：

```powershell
& D:/Code/CSMR/CarAR/Launch/Start-HuijingMR.ps1 -RestartCameraBackend
```

该入口先关闭本工程的应用与厂家相机后台，再按“两路相机就绪 → MR 服务器 → CarAR”的顺序启动，并保存上次日志为 `before-mr-start-*.log`。2026-10-20 的透视丢失中，两路旧后台进程仍存在，但日志已断连，应用也没有实际视频帧统计；完整重启后，两路恢复就绪，`fps:vr ... vst ... read ...` 恢复，读取约 70 帧/秒。佩戴者随后确认真实环境与 AMG 汽车同时可见。修复入口已实际运行通过，退出码 0；会等待旧进程完全退出，并以共享方式读取正在写入的相机日志。SDK 接受开启透视的请求不能单独证明相机已有画面。

默认调用 `D:/Soft/AIXR Agent` 的运行时、API Layer、SDK，并启动或复用厂家后台。环境变量只传给本次应用进程；不修改 OpenXR 注册表，不替换已安装 DLL，不调用 SaveConfigToFile。Unity 保留透明 Alpha，VST 在背景，车辆在前景；显式请求关闭 VR head lock。

请先完成头显定位、手柄配对和运行时地面标定。PC 版本等待头显佩戴和位置跟踪恢复后，才开始首次地面搜索，避免用未佩戴时缓存的旧位姿摆放车辆。荟镜模式在 OpenXR 输入启动后请求厂家 demo 使用的 Device / LOCAL 参考空间，并将相机高度附加偏移设为 0；实际结果见 `[HJXR Reference]`，不能仅看请求值判断成功。首次找到足够大的水平面后自动放置；设备没有平面数据时，8 秒后放到视线前方约 4.5 米。没有有效地面参考时按 1.5 米眼高估计，并明确显示为估计。首次放置后不再随平面更新或头部运动调整车辆。

当前车辆为工作区原始 `../AMG.blend`，通过 `Tools/prepare_amg.py` 导出 `Assets/Models/AMG-interactive.glb`。默认缩放为 1，包围盒约长 4.676 × 宽 2.082（含后视镜）× 高 1.349 米。1:1 指源模型的米制单位；未经原厂尺寸校准。源文件和旧 911 资源均保留。

导出在后台 Blender 中执行，不保存或覆盖原 `.blend`：

```powershell
& D:/Soft/Blender/blender.exe --background --factory-startup D:/Code/CSMR/AMG.blend --python D:/Code/CSMR/CarAR/Tools/prepare_amg.py
```

导出应用 Mirror / Subdivision 修改器，仅沿原本不相连的网格岛分离左右车门，不按空间平面切割车身三角形。门玻璃、把手、后视镜与门板饰件随相应车门开合；后备厢尾门（含后窗）和发动机盖各有独立铰链。车轮按轮胎、轮辋、制动盘和卡钳组件整体爆炸 / 组装，所有分件向地面上方展开。约 131 万三角形，145 个网格，保留打包纹理；Blender 专用节点组转换为标准 PBR，车漆在 Unity 中使用 URP Complex Lit。源门盖外壳无内部厚度，采用原生双面渲染保证打开后背面可见；没有凭空补建发动机或内层钣金。

AMG 最终构建见 `amg-controller-final-build.log`，`amg-controller-smoke.log` 三组自动验证通过，退出码 0。新增验证包含射线实际生成顶点、aim 失效后回退与恢复、真实四处门盖几何移动、车门包含原门玻璃及后视镜、爆炸分件不下沉到地面、组装后位置与旋转完整还原。`Build/Windows/assembled.png`、`panels.png`、`exploded.png` 是原生 URP 离屏渲染，已视觉核对。

20:09 佩戴者已确认射线恢复并能点选；真机日志 `amg-ray-restored-marker-interrupted-device.log` 同时记录了校准按钮选中。该次原厂确实识别 ID 0，但矩阵多行日志被 Unity 输出打断。反馈读取因此增加了完整原厂定位位姿（xyz + xyzw）途径：按本次提交的标记目标重建同一个刚性变换，并用此前独立捕获的原厂矩阵验证至 0.1mm 内。纯成功字样、不完整位姿及非法四元数不作为对齐依据。该修正仍需再次在实体板上核对绿框，不能把自动验证当作现实空间稳定验收。

随后佩戴者确认射线可点选、校准有提示，但绿框位置偏在旁边；黑框实测为 **6.78 × 6.78cm**，取代此前估计的 5cm。参考框、`global.ini` 的 `Core.marker.size` 和当前设备 YAML 的 `markerLength` 现统一为 **0.0678m**；两份厂家配置在修改前均有相邻 `*.carar-before-measured-678mm-20261019-203049.bak` 备份，相机内外参未改。5cm 会把识别距离低估约 26%，不能继续用于该板。

`amg-marker-offset-device-20261019-202852.log` 还复现了另一个问题：本次请求之后才写入文件的缓冲日志可能属于上次检测（例如 20:18:47 请求读到 20:18:40 结果）。反馈读取现按原厂记录时间过滤旧结果，等待本次检测线程完成后再应用最终矩阵；多行矩阵被打断时优先读取最终单行 `marker_t2w`，完整定位位姿作为后备证据。仅在校准期间刷新原厂所用 CRT 的日志缓冲，避免最终结果滞留数十秒；不改变原厂 DLL 或跟踪算法。日志记录本次请求、证据和完成时间。`marker-request-correlation-build.log` 的旧结果回放、最终姿态及跨午夜校验通过，`marker-request-correlation-smoke.log` 的三组检查通过，退出码 0。实体板贴合与走动稳定仍需用实测尺寸版佩戴验证。

实测尺寸的最终构建 `marker-678mm-final-build.log` 通过，生成场景为 `markerLength: 0.0678`。20:32 重启厂家服务器与应用，`huijing-mr.log` 确认服务器配置 `size 0.067800`、定位模块 `markerLength_ = 0.0678`，原生输入适配已安装、VST / VR 启动正常。尺寸更新后的实体重合结果仍待佩戴者反馈。

## 头显操作

大厅首次摆放建议按以下顺序进行：

1. 佩戴头显，等待位置跟踪恢复和首次放置（无平面数据时约 8 秒）。
2. 厂家定位板已可用时，先将其竖直固定、图案朝向佩戴者，头显在约 0.5–1 米处看清整个码。长按左手 X 键约 1.2 秒，或菜单 / Y / B 呼出面板选择“定位板校准”。面板自动收起，保持板子和头部稳定；检查绿色参考框与实体黑色方框能否重合，并从不同角度确认。照片识别结果为 DICT_6X6_50、ID 0；SDK 与参考框边长均按用户实测 **6.78cm** 设置。
3. 校准后再次呼出面板，选择“选择停放位置”，把整车占地边框指向大厅地面；摇杆上下调整预览高度。
4. 扳机确认。车辆停放并锁定；需要微调时解除锁定，在“精细调整”内按 10cm / 5° / 地面 ±1cm 修正，完成后再次停车锁定。
5. 收起面板绕车评审。门盖与爆炸 / 组装在停车状态下也能操作；要移动或缩放整车时再解锁。

- 菜单键、左手 Y 或右手 B：在当前视野前方显示 / 收起面板。面板只在呼出时定位，此后固定在世界中。注视停留和头部准星已移除。
- 扳机：用手柄射线点选按钮、左右门、后备厢尾门或发动机盖。停车锁定时仍可开关门盖与播放爆炸 / 组装。
- 长按左手 X 键约 1.2 秒：直接请求定位板校准，无需先用射线点按钮。每次按住仅触发一次，需松开后才能再次请求。
- 指向使用原生 XRI 射线和线条视觉组件。检查 OpenXR aim 位姿实际有效性；仅 grip 有效时回退握持位姿，aim 恢复时自动切回。射线为 5mm 不透明线条，放置阶段也保持可见。`[CarAR Ray]` 每 5 秒记录实际姿态来源、手柄距头部距离、线条开关、端点和顶点数，避免把已跟踪标志误当作射线已显示。
- 解除锁定后，握键按住车身可拖动；两只手柄同时握住车身并拉开 / 靠近可缩放。MRTK ObjectManipulator 处理变换；约束为水平平移、绕竖轴旋转、0.1–2 倍缩放。
- 解除锁定后，左摇杆平移，右摇杆左右旋转、上下缩放。握键正在抓取车辆时，摇杆不同时修改整车变换。
- “选择停放位置”：收起面板，射线指向地面显示整车占地轮廓；摇杆上下调整预览地面高度；扳机确认位置与高度并锁定车辆；菜单 / Y / B 取消。确认前不会移动已停放的车辆。
- “定位板校准”：调用原厂单码板与 WorldSync，锁住车辆、收起面板。按原厂时间戳只读取本次请求的结果，等待完整对齐数据和检测线程完成；12 秒未收到结果时继续等待，30 秒超时则提示重启，避免向仍忙碌的原厂模块重复提交。最终矩阵通过校验后用于统一输入与原厂渲染坐标，绿色参考框再显示 30 秒；结果面板自动重新显示。此时仍需从不同角度检查实体黑框重合。定位板是竖直 XY 参考，不能当作水平地面；不会自动搬动车辆。校准后的下一次“选择停放位置”重新按当前眼高估算预览地面，用户可调整高度、按扳机确认。
- “精细调整”：每次平移 10cm、旋转 5°、缩放 10%、地面 ±1cm，以及光强 / 色温调整和自动光照请求。
- “1:1 尺寸”和“1:4 展示”需要先解锁。摆放好后选择“停车锁定”，避免误操作。
- “切换流畅画质”：原生分辨率、4096 阴影和 SSAO，与 80% 分辨率、2048 阴影、关闭 SSAO 两档切换；均保持 4x MSAA 与 MR 透明背景。

当前采用标准 OpenXR Touch / Index / Vive / Simple / Microsoft 控制器配置；按键名称随手柄外形而异。Pimax 运行时在头显未佩戴、OpenXR 会话未进入 Focused 时会暂停手柄同步；已配对手柄也可能暂时不出现在应用中。Windows 桌面窗口失去焦点时仍接收 XR 输入；键盘快捷键仅在应用窗口有焦点时响应。

## 渲染与环境数据

车身使用 Unity URP Complex Lit 不透明金属清漆；车窗和灯罩保留透明。使用原生反射探针卷积生成大厅预设反射、URP 原生 SSAO、4096 软阴影、透明地面接收阴影及柔和接触阴影。纹理采用 16 倍各向异性过滤，取消负 mip 偏移以减少走动时纹理闪烁。

大厅反射与默认灯光是预设，不是现场采集。ARFoundation 已请求环境光强、颜色、主光方向、球谐与环境反射探针；只有设备实际提供数据时才会自动更新。当前 HJXR demo / 文档没有可直接供 Unity 读取的实时测光或环境贴图接口，运行中没有收到此类数据时会显示“大厅光照预设”，允许手动调光强、色温与方向。HDR 中间颜色缓冲关闭，以保留厂家合成需要的 Alpha。

## 空间稳定性

车辆是独立世界根对象，未挂在相机或 XR Origin 下；默认禁用操控，释放抓取后不保留速度。没有任何停车状态下跟随头部或持续追地的逻辑。

`huijing-mr.log` 中 `[CarAR Stability]` 每 5 秒记录车辆位置 / 角度变化、头部变化、跟踪标志、佩戴状态（若运行时提供）、Origin、帧率和手柄状态。`[CarAR Head Source]` 记录实际绑定的头部输入；`[CarAR View Sample]` 记录原始输入、相机与引擎双眼视图中心。`[CarAR Tracking Anomaly]` 在位姿跳变或来源不一致时同时记录前后样本，`[CarAR Tracking State]` 记录跟踪状态、参考空间、佩戴状态切换。事件采样覆盖 Dynamic 与 BeforeRender，不再用五秒末的佩戴状态推断中间跳变发生时的条件。车辆根坐标稳定不等于透视合成已经对齐；走动时的视觉晃动还可能来自跟踪、参考空间、视频与位姿时延。厂家手册第 6 页说明了 VST 位姿时间和 VSYNC 偏移校准。

用户随后找到厂家定位板并提供照片，OpenCV 从原图识别出 DICT_6X6_50、ID 0；图片识别证据在 `tmp/marker-board-detected.jpg`。面板已恢复“定位板校准”，也可在应用有键盘焦点时按 K 请求。用户随后报告黑框边长约 5cm，程序参考框、安装目录 `res/config/global.ini` 的 `Core.marker.size` 和当前设备专用 `marker_hjxr2_2496_P407B0D200640900034.yaml` 的 `markerLength` 已同步为 0.05 米。只改尺寸，原文件分别备份为同目录的 `*.carar-before-5cm-20261019-180109.bak`；相机标定参数不变。此值来自用户估计，不能当作精确测量。启动不自动对齐，需先固定并看清定位板，再主动请求。R 重新放到当前视线前方，H 呼出 / 收起面板，M 切换锁定。

定位板工作流构建见 `marker-calibration-build.log`，三组验证见 `marker-calibration-smoke.log`，退出码 0。已核对更新后的面板截图并重新启动头显程序。当前仍待精确黑框尺寸及真机定位结果；不把照片识别、构建通过或 WorldSync 请求接受作为现实世界固定已成功的证明。

约 5cm 尺寸更新已通过 `marker-5cm-build.log` 构建检查，生成场景的 `markerLength` 为 0.05。18:02 重启应用后，`huijing-mr.log` 确认原厂服务器 `size 0.050000`、识别模块 `markerLength_ = 0.0500`，两处均已读取新尺寸。此轮只调整尺寸，未重复此前已通过的交互和动画测试；物理板识别与透视对齐仍需佩戴验证。

随后“始终不重合，或看不到绿色框”的真机日志见 `marker-5cm-failed.log`：原厂持续报 `fillFrameData ... to cvmat failed`，识别线程无法正常使用相机图像，重复请求则报原厂仍忙。定位模块在 Unity OpenXR 的临时 PreInit 实例销毁后保留了已失效的相机配置，读到图像宽高 0。Windows 构建现使用 Unity 自带的 `OpenXRLoaderNoPreInit`，避免先创建并销毁临时实例；构建会检查输出 `boot.config` 不含 `xrsdk-pre-init-library=UnityOpenXR`。未改动厂家 DLL。初次修正运行的日志保存在 `marker-no-preinit-initial-device.log`；只读检查确认定位模块与当前相机配置一致，图像尺寸恢复为 1120×832。这是识别输入链路的修正，尚不能证明实体板已识别或车辆已固定在现实中。

正式构建 `marker-no-preinit-final-build.log` 已通过原生 ABI、坐标转换、异步识别反馈回放和禁用 PreInit 检查；`marker-no-preinit-smoke.log` 的 MRTK、Input System 手柄及车辆动画三组检查全部通过。19:22 真机启动日志确认实际加载器为 `OpenXRLoaderNoPreInit`、仅创建真实 `CarAR 911` OpenXR 实例，VST / VR 请求正常。反馈中的“已收到原厂对齐计算”仅代表本次请求后出现计算矩阵日志；请求接受及初始 `id 1 no data` 均不算识别成功。实体板识别、约 5cm 尺寸准确性及走动时的现实对齐仍需真机验证。

19:26:26 的实际请求已识别 ID 0，原厂输出 `localize success by LocalizeCheck` 与完整 `T_slamw2objw`，证据为 `marker-no-preinit-first-detected.log` / `marker-input-mismatch-device.log`。佩戴者仍看不到框和结果。诊断显示校准后输入头部约为 `(0.008, 0.113, -0.314)`，双眼渲染中心仍约为 `(-2.33, -0.13, 8.31)`，两条链路相差约 9 米。安装版原厂 `trackLocateSpace=1` 对齐输入并保留应用原始双眼视图；默认值 0 则对齐双眼视图并保留原始输入。现保留原厂默认渲染模式 0，由 `HjxrMarkerTrackingFeature` 使用 Unity OpenXR 的标准函数挂接接口，将 VIEW 与手柄 action space 的输入变换到同一定位坐标。只对当前 app space 下的这些输入空间应用一次原厂完整矩阵，保留有效性标志并旋转速度；不修改双眼渲染、投影矩阵或停车模型坐标。矩阵标题、半矩阵、非有限或非刚性矩阵均不接受。物理贴合与走动稳定性仍待该修正版真机验证。

`marker-input-coordinates-build.log` 已通过构建和所捕获原厂矩阵的坐标回放、OpenXR ABI、位置有效性与速度基底检查；`marker-input-coordinates-smoke.log` 的 MRTK、Input System 手柄、车辆动画三组检查全部通过，退出码 0。19:40 真机重新启动，日志确认原厂渲染模式 0、输入挂接已安装，并捕获 21 个 VIEW / action 输入空间。`[HJXR Marker Input]` 的 `correctedLocations` 在接受完整矩阵后应持续增加；需结合输入 / 双眼视图日志及佩戴者的实体板重合检查验收，启动成功不代表现实对齐完成。

当前 PimaxXR 0.4.5 公布的 13 个扩展中没有空间锚点扩展；没有可用于该设备的 ARFoundation 锚点提供方证据。普通会话内的世界固定可以依靠一致的 6DoF 坐标，不必以空间锚点组件为前提；反过来，添加一个无提供方的 ARAnchor 也不能消除透视合成滑动。本工程尚未验证跨会话的大厅定位恢复。

### 动态滑动排查

佩戴者反馈车辆静止会抽动、转头或走动时滑动，停下后基本回到原位。即使停车坐标固定，这也仍是需要处理的问题。安装的 HJXR SDK 默认 `layerPoseDefer=1`，其视图位姿队列会保留旧位姿。应用请求 `SetLayerPoseDefer(0)` 后，佩戴者反馈“有所改善，但仍会滑”。保留缓存 0，继续单独对照 VST 位姿时序，尚未确认消除了滑动。

2026-10-19 16:14 的在线对照将 VST 位姿偏移从厂家启动配置的 10ms 改为 0ms，SDK 原生日志确认 `set layer 1 pose time offset 0 ns`。佩戴者随后反馈车辆仍会随视角改变在现实中的位置，未确认这一时序值能解决问题。当前输出目录的请求文件仍保存该实验值。调整前日志为 `alignment-defer-zero-before-timing.log`，此次参考空间修改前的状态保存在 `alignment-before-local-reference.log`。

16:34 重启的版本在活动 OpenXR 输入子系统上请求 LOCAL，日志确认 `LOCAL request=True actual=Device`，随后诊断也记录 `originMode=Device`、Origin 与相机高度偏移均为 0。设置必须在输入启动后应用：提前请求曾被初始化过程误判为不支持并回退，见 `world-reference-early-request.log`。本轮保留缓存 0、VST 位姿偏移 0ms 和原有渲染质量。参考空间切换及自动验证通过均不代表现实位置稳定性已通过验收。

该次日志发现过单帧约 6.75 米的相机跳变，尚未确定来源，也不能直接归因于真实走动。当前 MRTK rig 原本没有绑定头部 trackingState，TPD 会将已绑定的位姿始终视为有效；荟镜模式现补上 `<XRHMD>/trackingState`，继续由 Unity 原生 TrackedPoseDriver 更新相机。没有加入相机平滑或连续移动车辆的补偿。运行时额外相机渲染 / GPU 截图改为仅 `--xr-capture` 显式启用，避免评审中自动截图造成卡顿。

本轮构建见 `tracking-validity-build.log`。`tracking-validity-smoke.log` 中 MRTK、真实 Input System 手柄射线与动画三组验证通过，进程退出码 0；主面板和精调面板截图已检查。最后仅过滤了运行时在 Dynamic / BeforeRender 之间变化的速度有效性标志，重新构建并于 16:49 启动头显版本。日志确认 LOCAL、生效的头部 trackingState 绑定、位置与旋转各一个有效控制源。`tracking-validity-device.log` 随后记录到佩戴、首次摆放和右手柄跟踪，停车期间车辆根坐标不变，约 72fps；截至该次采样未复现 6.75 米跳变。现实地面对齐的主观结果仍待反馈，不能把软件检查视为滑动已修复。

可在应用运行时切换，无需重启或丢失当前摆放：

```powershell
# 当前测试：缓存 0，VST 位姿偏移 0ms。
& ./Tools/Set-HuijingAlignment.ps1 -PoseDeferFrames 0 -VideoPoseOffsetMs 0
# 返回佩戴者已确认部分改善的上一组：缓存 0，VST 位姿偏移 10ms。
& ./Tools/Set-HuijingAlignment.ps1 -PoseDeferFrames 0 -VideoPoseOffsetMs 10
# 仅改变缓存，保留本次进程当前的视频时序（不代表恢复厂家值）。
& ./Tools/Set-HuijingAlignment.ps1 -PoseDeferFrames 0
# 厂家启动配置对照：缓存 1，VST 位姿偏移 10ms。
& ./Tools/Set-HuijingAlignment.ps1 -PoseDeferFrames 1 -VideoPoseOffsetMs 10
```

请求写入本应用输出目录的 `hjxr-alignment.json`，约 0.5 秒后应用到本次进程。日志 `[HJXR Alignment]` 记录请求及 SDK 实际设置结果；启动时也会读取该文件。未传 `VideoPoseOffsetMs` 时保留当前视频时序。VST 位姿偏移可在 -30 到 60ms 范围内做人工对照，不能由 Unity 的稳定停车坐标推算出正确值。工具不修改厂家全局文件，也不调用 SaveConfigToFile。删除请求文件并重新启动应用可恢复应用默认策略（图层缓存 0、视频时序采用厂家配置）。

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
