# CSMR / AMG GT XR 整车评审

Windows 上展示 AMG 汽车，支持独立门盖、整车爆炸 / 组装和 MR 手柄交互。实际 Unity 工程是 **CarAR**。模型以根目录 `AMG.blend` 为源，默认按源模型米制单位 1:1 展示。

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
3. 也可在仓库根目录构建并运行四组回归：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\CarAR\Tools\Build-Windows.ps1 -Smoke
# Unity 不在默认位置时，追加 -UnityPath 'E:\Unity\6000.6.3f1\Editor\Unity.exe'
```

构建产物为 `CarAR/Build/Windows/CarAR.exe`；日志为 `CarAR/build-windows.log` 和 `CarAR/smoke-windows.log`。首次源码导入需要访问 Unity 包注册表，编辑器需要有效的 Unity 许可。已有导出模型和材质可以直接构建；仅在修改 `.blend` 后重导出时才需要 Blender。

大模型、Blender 源文件与 HDR / EXR 通过 **Git LFS** 保存。GitHub 的“Download ZIP”通常不能替代完整 LFS 克隆；如果模型是以 `version https://git-lfs.github.com/spec/v1` 开头的文本，请安装 Git LFS 后执行 `git lfs pull`。

依赖来源与许可证见 [Vendor](Vendor/README.md)，设备与渲染细节见 [CarAR](CarAR/README.md)，已知缺口与验证边界见 [代码审查](CarAR/CODE-REVIEW.md)。
