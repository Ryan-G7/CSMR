# CSMR / AMG GT XR 整车评审

Windows 上展示 AMG 汽车，支持独立门盖、整车爆炸 / 组装、MR 手柄交互和室内 VR 漫游。实际 Unity 工程是 **CarAR**。模型以根目录 `AMG.blend` 为源，默认按源模型米制单位 1:1 展示。

## VR 场景漫游

使用 `D:\BaiduNetdiskDownload\CarRoom-2.blend` 的室内展厅场景，提供独立 VR 模式和无需头显的桌面漫游。先用下方源码构建命令生成新播放器，然后运行 `启动VR漫游.cmd` 或 `启动VR桌面漫游.cmd`；原桌面预览中也有 **VR CAR ROOM / WALKTHROUGH** 入口。旧 GitHub Release 不包含这次新增场景，VR 启动器会检查播放器能力。

头显使用系统当前的 OpenXR 运行时。默认开启左摇杆连续移动，前后左右按头部朝向行走；右摇杆向前保持以显示传送抛物线，松开提交，左右拨动转向 30°。摇杆须先回中，主动移动会自动收起面板。菜单 / Y / B 呼出中文面板，可选“进入驾驶位”以坐姿眼高看内饰，选“下车 / 恢复站立”或“返回入口”离开；仍可关闭连续移动、调整步速和精细 / 流畅画质。默认画质为精细。扳机点选车辆门盖，保留爆炸与还原。

车辆采用可穿行的内饰观察方式：车身不阻挡访客，允许贴近、穿过车门进入车内；房间墙壁与地面边界仍限制移动。传送只接受连通地面，不能把车顶当落点。传送和转向带淡入淡出，连续行走带边缘遮罩；失去跟踪立即停止操作，短于 0.15 秒的偶发失跟不直接闪黑，持续失跟才渐黑，稳定恢复 0.12 秒后重新等待摇杆回中。

桌面：WASD 行走，按住右键环视，Shift 加速，T 后指向地面单击传送，Tab / Esc 面板，F 返回入口，1–4 门盖，空格爆炸 / 还原。摄像机按真实眼高漫游，车辆与场景保持 1:1。头显启动失败时可按 F2 进入桌面模式。

录屏使用 `启动VR录屏模式.cmd`，再用 EV 录屏选择电脑上的 CarAR 镜像窗口区域，建议先用 1080p / 30 帧。录屏档把每眼渲染边长降到精细档的 65%（像素约 42%），为抓取和编码留出余量；模型、材质、灯光和阴影设置保留精细档，头显刷新率仍由 XR 运行时控制。窗口为 1920×1080，实际可见尺寸取决于显示器。运行中可在菜单切换“录屏优化”，或让 CarAR 窗口获得焦点后按 F8。关闭后恢复所选画质的正常分辨率，普通启动仍默认精细，不自动降低画质。录屏模式会降低头显清晰度；若 EV 仍黑屏，应检查抓取来源而非继续降低画质。

场景保留岩壁几何、木地板、灯带、原 UV 和最高 4K PBR 贴图；复杂颜色与法线节点由 Cycles 烘焙。使用源文件的曝光、Filmic 与 Medium High Contrast 生成颜色查找表，保留原 HDR 的方向映射，并使用 Blender Cycles 将原场景的材质颜色、直接光与间接光烘焙为 4K / 2K UV2 图集；室内 HDR 全景提供车辆反射与漫反射环境光。来源见 [场景资源说明](CarAR/Assets/Environment/VR/SOURCE.md)。实时渲染与 Cycles 在玻璃折射、置换、反射和间接光精度上仍有差异。头显舒适度和实际帧率需佩戴验收。

修改场景后重新导出（原 `.blend` 不会被覆盖）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\CarAR\Tools\Export-VRScene.ps1 -SourcePath 'D:\BaiduNetdiskDownload\CarRoom-2.blend' -BlenderPath 'D:\Soft\Blender\blender.exe' -Reference
powershell -NoProfile -ExecutionPolicy Bypass -File .\CarAR\Tools\Bake-VRLighting.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\CarAR\Tools\Build-Windows.ps1 -Smoke
```

构建生成 `CarAR/Assets/CarAR/Scenes/CarVR.unity`，首次烘焙需要较长时间。源文件、导出数据和最终光照 UV 布局匹配时复用烘焙；过期光照会阻止构建并提示重新烘焙。构建回归同时验证原 MR / 桌面预览和 VR 漫游。VR 检查图保存在 `CarAR/Build/Windows/vr-*.png`。

## 直接运行 Windows 预览

```powershell
git lfs install
git clone https://github.com/Ryan-G7/CSMR.git
cd CSMR
git lfs pull
.\启动桌面预览.cmd
```

首次启动会从本项目 GitHub Releases 下载对应版本的 Windows x64 运行包，校验 SHA-256 后解压到 `CarAR/Build/Windows`。需要联网、Windows 10/11 x64 和支持 Direct3D 11 的显卡；**运行预览无需 Unity、Blender 或头显**。之后可离线启动。也可从 [Releases](https://github.com/Ryan-G7/CSMR/releases) 手动下载 ZIP，解压后运行 `CarAR.exe`。完整保留 ZIP 中的 `CarAR_Data`、`MonoBleedingEdge`、DLL 等目录和文件。

鼠标右键拖动环绕，滚轮缩放；1 / 2 控制左右门，3 尾门，4 发动机盖，空格爆炸 / 还原。运行包与源码分开发行，Unity 缓存、临时 Python 环境和本机诊断日志不在仓库内。

## 荟镜 MR

先安装厂家提供的 **AIXR Agent、配套 Pimax OpenXR 运行时和 Inuitive 相机驱动**，完成设备定位与手柄配对，再运行 `启动汽车MR.cmd`。这些硬件驱动不能由源码或桌面预览包替代。

脚本检测常见安装目录；其他路径可在 PowerShell 指定：

```powershell
$env:CARAR_AGENT_ROOT = 'E:\Apps\AIXR Agent'
powershell -NoProfile -ExecutionPolicy Bypass -File .\CarAR\Launch\Start-HuijingMR.ps1
```

当前使用原生 LOCAL 空间和显式地面摆放，已移除实体定位板。请佩戴头显并唤醒至少一只手柄，定位一致且稳定后才会出车；菜单 / Y / B 打开面板。设备链路目前使用静态 HDR，未接入真实房间测光。细节与实机限制见 [CarAR 说明](CarAR/README.md)。

## 打开源码 / 构建

1. 安装 **Unity 6000.6.3f1**（Windows Build Support），在 Unity Hub 中打开仓库下的 **CarAR 文件夹**，等待首次依赖下载和导入。不要打开仓库根目录的历史工程设置。
2. 打开 `Assets/CarAR/Scenes/CarAR.unity`，播放可查看桌面预览。六个 MRTK 包已经固定在 `Vendor/MRTK`，XRI / Graphics Tools 固定在 `CarAR/Packages`，无需初始化子模块。
3. 也可在仓库根目录构建并运行原有四组回归及 VR 回归：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\CarAR\Tools\Build-Windows.ps1 -Smoke
# Unity 不在默认位置时，追加 -UnityPath 'E:\Unity\6000.6.3f1\Editor\Unity.exe'
```

构建产物为 `CarAR/Build/Windows/CarAR.exe`；日志为 `CarAR/build-windows.log`、`CarAR/smoke-windows.log` 和 `CarAR/smoke-vr.log`。首次源码导入需要访问 Unity 包注册表，编辑器需要有效的 Unity 许可。已有导出模型和材质可以直接构建；仅在修改 `.blend` 后重导出时才需要 Blender。

大模型、Blender 源文件与 HDR / EXR 通过 **Git LFS** 保存。GitHub 的“Download ZIP”通常不能替代完整 LFS 克隆；如果模型是以 `version https://git-lfs.github.com/spec/v1` 开头的文本，请安装 Git LFS 后执行 `git lfs pull`。

依赖来源与许可证见 [Vendor](Vendor/README.md)，设备与渲染细节见 [CarAR](CarAR/README.md)，已知缺口与验证边界见 [代码审查](CarAR/CODE-REVIEW.md)。
