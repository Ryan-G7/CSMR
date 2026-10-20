# AMG GT 大厅 MR 整车评审

实际 Unity 工程在本目录 `CarAR`，使用 Unity 6000.6.3f1。部署链路为 Windows / Pimax OpenXR / 荟镜 HJXR 透视。系统审查与尚缺少的正式评审能力见 [代码审查报告](CODE-REVIEW.md)。

## 当前空间定位方式

2026-10-20 已删除实体定位板工作流，包括绿色参考框、校准按钮、长按 X / K 快捷键、WorldSync 调用、厂商日志结果解析和自定义 OpenXR 输入空间变换。

删除依据是设备日志中校准后相机输入与双眼视图坐标不一致，自定义输入修正次数为 0，随后请求超时。此前调整板尺寸、结果读取与输入适配仍未获得佩戴者的贴合验收。应用现在保留原生头部 / 手柄跟踪，不再用定位板改变参考空间。厂家安装目录的相机标定文件没有在此次修改。

荟镜模式在 OpenXR 输入启动后请求厂家 demo 使用的 Device / LOCAL 参考空间，并将相机附加眼高偏移设为 0。实际结果记录在 `[HJXR Reference]`。车辆是独立世界根对象，停车后不随相机、平面更新或手柄移动；恢复摆放和锁定不代表厂家透视合成已达到现实中的稳定精度。

## 运行与头显操作

双击工作区根目录 `启动汽车MR.cmd`，或运行：

```powershell
& D:/Code/CSMR/CarAR/Launch/Start-HuijingMR.ps1
```

启动脚本自动启动或复用厂家相机后台和 MR 服务器，等待新启动的相机就绪，不重复启动现有 CarAR。缺少真实画面时运行根目录 `修复透视并重启.cmd`，或加 `-RestartCameraBackend`。日志为 `CarAR/huijing-mr.log`，重启前自动备份旧日志。

启动环境变量只传给本次应用，不修改 OpenXR 注册表、不替换厂家 DLL、不调用 SaveConfigToFile。Unity 保留透明 Alpha，VST 在背景、车辆在前景，显式关闭 VR head lock。

1. 完成设备定位和手柄配对，佩戴头显，等待位置跟踪恢复。足够大的水平面可触发首次摆放；没有平面数据时约 8 秒后放到视线前方约 4.5 米，按 1.5 米眼高估计地面。
2. 按菜单键、左 Y 或右 B 呼出面板，选择“选择停放位置”。用手柄射线指向地面，摇杆上下调整整车占地预览的高度。
3. 扳机确认，车辆停放并锁定。菜单 / Y / B 可取消未确认的摆放。
4. 需要细调时解除锁定，在“精细调整”内按 10cm / 5° / 地面 ±1cm 修正，完成后再次停车锁定。
5. 收起面板绕车观察。点选左右门、尾门和发动机盖可开合；门盖和爆炸 / 组装在停车状态下也可操作。

面板仅在呼出时定位，随后固定在世界中。使用标准 XRI 射线，aim 无效时回退 grip，恢复后自动切回。解除锁定后可握住车身拖动，双手同时抓取缩放；左摇杆平移，右摇杆旋转 / 缩放。抓取期间摇杆不同时改动车辆。缩放范围为 0.1–2 倍。锁定、尚未摆放或正在爆炸时，相关整车变换被阻止。

“切换流畅画质”在原生分辨率 / 4096 阴影 / SSAO 和 80% 分辨率 / 2048 阴影 / 无 SSAO 间切换，均保持 4x MSAA 与 MR 透明背景。窗口失去焦点仍接收 XR 输入；键盘快捷键只在应用有焦点时响应。R 重新摆放，H 呼出面板，M 切换锁定。

## 模型与渲染

当前模型来自根目录 `AMG.blend`，默认按源模型米制单位 1:1 展示，未经原厂尺寸标定。后台导出不覆盖 `.blend`：

```powershell
& D:/Soft/Blender/blender.exe --background --factory-startup D:/Code/CSMR/AMG.blend --python-exit-code 1 --python D:/Code/CSMR/CarAR/Tools/prepare_amg.py
```

导出应用 Mirror / Subdivision，仅沿原有不相连的网格岛分离左右门。门壳、玻璃、门饰件、把手和后视镜使用经过确认的源组件；最新源文件中已拆开的门内饰、扶手、扬声器与按键按明确来源清单归属左右门，座椅 / 仪表台 / 车身密封框保持固定，不再按坐标猜测归属。零碎内饰按总成和材质合批，导出为 165 个网格、1,306,154 三角形，来源保留在 JSON。后窗随尾门、发动机盖独立开合，四轮组件整体爆炸 / 组装。源模型缺少门盖内层厚度与完整发动机，双面渲染只能显示现有几何。

车漆颜色直接读取 Blender 的 Carpaint 节点组，随导出写入 `AMG-interactive.json`，Unity Setup 从同一份数据生成 URP Complex Lit 材质。当前源线性 RGB 约为 `(0.700126, 0.700126, 0.700126)`。Blender 程序化分层着色转换为金属清漆 PBR 近似，无法保证两种渲染器逐像素一致。

渲染保留源纹理、法线与切线，使用金属底层 / 清漆高光、1024 HDR 反射探针及卷积 mip、4096 软阴影、SSAO、透明地面阴影与接触阴影。反射中的窗带和条形灯用于呈现车身曲率。纹理使用 16 倍各向异性过滤，不使用负 mip 偏移。

大厅反射与默认灯光是预设。ARFoundation 请求测光与环境探针，只有提供方实际输出数据时才更新；过期 / 非法测光恢复预设。当前厂家链路没有已验证的实时环境贴图接口。为保留透视 Alpha，主颜色缓冲关闭 HDR；反射纹理保留 HDR。

## 验证与现实精度边界

```powershell
Start-Process -FilePath 'C:/Program Files/Unity/Hub/Editor/6000.6.3f1/Editor/Unity.exe' -ArgumentList '-batchmode -projectPath D:/Code/CSMR/CarAR -executeMethod CarAR.EditorTools.CarARSetupWizard.BuildPreview -logFile D:/Code/CSMR/CarAR/mr-refresh-build.log' -WindowStyle Hidden -Wait
Start-Process -FilePath './Build/Windows/CarAR.exe' -ArgumentList '--carar-smoke -force-d3d11 -screen-fullscreen 0 -screen-width 1440 -screen-height 900 -logFile D:/Code/CSMR/CarAR/mr-refresh-smoke.log' -WindowStyle Hidden -Wait
```

自动验证覆盖 MRTK 单 / 双手操控、真实 Input System 控制器事件与 XRI 射线、摆放高度和地面轴心、锁定、车门来源白名单与独立运动、门盖、爆炸组装还原、模拟测光失效与模式切换。原生 URP 离屏检查图输出为 `Build/Windows/assembled.png`、`left-door.png`、`panels.png`、`exploded.png`。这些图片不包含真实相机视频。

`[CarAR Stability]` 记录车辆与头部变化、跟踪、Origin、帧率和手柄状态；`[CarAR View Sample]` 对比输入、相机和双眼中心。车辆根坐标不变不能单独证明现实中无滑动。头显走动、转头、跟踪丢失 / 恢复、真实地面贴合仍需要佩戴验收，当前没有跨会话持久锚点方案。

保留 `Tools/Set-HuijingAlignment.ps1` 供显式诊断视频时序使用；它不是定位板校准。默认只设置 pose defer 为 0，不猜测视频时差。旧的 0ms 视频实验请求已移出活动文件，值保存在 `tmp/hjxr-alignment-before-marker-removal.json`，启动保留厂家视频时序。诊断改动仅影响该进程，不写回厂家配置。

此前设备排查日志和 Git 历史保留作证据，旧版定位板操作不适用于当前版本。MRTK 依赖及 Unity 6 兼容改动见 [迁移说明](MRTK3-MIGRATION.md)。旧 911 资源仍保留作历史资源，当前场景使用 AMG。
