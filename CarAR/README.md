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

1. 完成设备定位和手柄配对，佩戴头显并唤醒至少一只手柄。Windows 首次摆放须等待头显位置 / 旋转有效、已佩戴，且至少一只原生 grip 位姿距头显不超过 3 米，连续稳定 1 秒。随后寻找足够大的水平面；没有平面数据时约 8 秒后放到视线前方约 4.5 米，按 1.5 米眼高估计地面。异常跟踪会重置等待时间。
2. 按菜单键、左 Y 或右 B 呼出面板，选择“选择停放位置”。用手柄射线指向地面，摇杆上下调整整车占地预览的高度。
3. 扳机确认，车辆停放并锁定。菜单 / Y / B 可取消未确认的摆放。
4. 需要细调时解除锁定，在“精细调整”内按 10cm / 5° / 地面 ±1cm 修正，完成后再次停车锁定。
5. 收起面板绕车观察。点选左右门、尾门和发动机盖可开合；门盖和爆炸 / 组装在停车状态下也可操作。

面板仅在呼出时定位，随后固定在世界中。使用标准 XRI 射线，aim 无效时回退 grip，恢复后自动切回。解除锁定后可握住车身拖动，双手同时抓取缩放；左摇杆平移，右摇杆旋转 / 缩放。抓取期间摇杆不同时改动车辆。缩放范围为 0.1–2 倍。锁定、尚未摆放或正在爆炸时，相关整车变换被阻止。

Windows 运行中持续检查跟踪。头显失效或已跟踪手柄与头显空间不一致时，锁车并暂时关闭车辆渲染和碰撞，动画对象保持活动。同一坐标空间恢复稳定后恢复原停车位置，手柄休眠不影响已经摆好的车辆。有效位置发生超出合理运动速度的突变时，显示“空间定位已重置，请重新选择停放位置”，必须重新确认停放；预览地面改用当前眼高估计，避免沿用旧坐标的地面。应用不修正原生相机或控制器坐标。

解锁、手柄恢复跟踪或输入重新启用后，必须先松开再按握键才能抓车，摇杆须先回中再操作；拿起已按住握键或推着摇杆的手柄不会自动拖拽或缩放。单手抓取只移动/旋转，不采用射线附着点的缩放。双手距离不足 8cm 或位置非有限值时保持当前比例，恢复有效距离后重新建立基准，避免距离除法让车辆突然缩小或消失。

“切换流畅画质”在原生分辨率 / 4096 阴影 / SSAO 和 80% 分辨率 / 2048 阴影 / 无 SSAO 间切换，均保持 4x MSAA 与 MR 透明背景。窗口失去焦点仍接收 XR 输入；键盘快捷键只在应用有焦点时响应。R 重新摆放，H 呼出面板，M 切换锁定。

## 模型与渲染

当前模型来自根目录 `AMG.blend`，默认按源模型米制单位 1:1 展示，未经原厂尺寸标定。后台导出不覆盖 `.blend`：

```powershell
$env:OIIO_THREADS = "1"
$env:OPENIMAGEIO_THREADS = "1"
& D:/Soft/Blender/blender.exe --background --factory-startup --threads 1 D:/Code/CSMR/AMG.blend --python-exit-code 1 --python D:/Code/CSMR/CarAR/Tools/prepare_amg.py
```

导出应用 Mirror / Subdivision，仅沿原有不相连的网格岛分离左右门。门壳、玻璃、门饰件、把手和后视镜使用经过确认的源组件；门内饰、扶手、扬声器与按键按明确来源清单归属左右门，座椅 / 仪表台 / 车身密封框保持固定。上沿长条 `Cube.013` 的两侧网格岛分别随门运动；混合源 `Cube.092` 仅取两侧各 109 顶点的门饰条，中间两块中控台网格保持静态。分岛数量、顶点数及边界有导出断言，来源保留在 JSON。零碎内饰按总成和材质合批，目前为 168 个网格、1,306,154 个三角形。后窗随尾门、发动机盖独立开合，四轮组件整体爆炸 / 组装。源模型缺少门盖内层厚度与完整发动机，双面渲染只能显示现有几何。

外观以 `AMG.blend` 为唯一材质来源。导出器保留 Principled 数值，使用 Cycles 烘焙 glTF 无法表达的颜色、粗糙度和法线输入图；不再替换车漆、轮毂、皮革、地毯、玻璃或灯管的颜色/粗糙度。没有材质的源面显式导出 Blender 默认 Principled 表面，不再套深色装饰材质。

源 Carpaint 是三层 BSDF 混合，不能直接当成单个 glTF 金属清漆材质。`AMG-interactive.json` 记录原始线性颜色、粗糙度、混合权重、Fresnel IOR、烘焙纹理与源文件 SHA-256；Setup/Build 检查源文件哈希，过期导出会明确报错。Unity 的 `BlenderLayeredPaint` 按这些值重建三层。当前底层粗糙度约 0.668、占比约 93.65%，没有额外的清漆权重 1 或金属度 0.9 覆盖。玻璃和发光材质的视角响应从源节点生成 LUT。

默认光照使用用户提供的 `Assets/Environment/lauter_waterfall_2k.hdr`，强度 1、旋转 0。该 HDR 同时生成带粗糙度卷积 mip 的反射 Cubemap 和漫反射球谐环境光。已删除虚构大厅、反射灯带和默认强直射灯；补光初始强度为 0，菜单可手动添加。MR 相机仍清为透明，HDR 仅参与照明与反射。桌面预览共用相同光照。纹理使用 16 倍各向异性过滤，无负 mip 偏移。

ARFoundation 请求测光与环境探针，只有提供方实际输出数据时才更新；过期/非法测光恢复 HDR 环境和零补光。当前厂家链路没有已验证的实时环境贴图接口。为保留透视 Alpha，主颜色缓冲关闭 HDR，环境反射纹理保留 HDR。

当前 Windows / Pimax OpenXR / HJXR 实机日志明确报告没有 AR Camera 和 Environment Probe 提供方，因此目前**不能接收现实房间的实时光照**，也不会根据房间开关灯更新 HDR 反射。厂家透视视频由合成层提供，不等于 ARFoundation 测光输入。设置页显示“静态 HDR · 现实测光未接入”；“HDR / 自动光照”在无提供方时恢复静态 HDR。`[CarAR Lighting]` 记录实际提供方、当前功能和是否收到有效样本。

Blender 截图使用 `forest.exr` 材质预览环境和 AgX；当前使用用户指定的瀑布 HDR，环境内容本身会改变反射。URP 与 Blender 的 BSDF、多次散射、色调映射存在差异，不能宣称逐像素一致。程序化图烘焙在一个 UV 单元内；Object/Generated 坐标纹理在单位平面上求值，不能完全保留每个原始物体的三维程序化分布。实时玻璃不做真实折射，彩色透射近似为标量透明度。这些是转换限制，不通过改色或额外加灯掩盖。

这台 Windows 机器上的 Blender 5.2 在 glTF 导出成功后的原生退出清理中会崩溃。导出器使用临时 `.blend` 副本和一次性导出进程，在文件写入、GLB 头和 JSON 检查后直接退出该进程，父进程继续校验材质、法线、切线并发布结果；失败不会替换模型。原始 `AMG.blend` 不会保存。HDR 漫反射的 27 个球谐系数显式存入场景组件，避免编辑器 RenderSettings 的临时值在播放器中丢失。

## 验证与现实精度边界

```powershell
Start-Process -FilePath 'C:/Program Files/Unity/Hub/Editor/6000.6.3f1/Editor/Unity.exe' -ArgumentList '-batchmode -projectPath D:/Code/CSMR/CarAR -executeMethod CarAR.EditorTools.CarARSetupWizard.BuildPreview -logFile D:/Code/CSMR/CarAR/mr-refresh-build.log' -WindowStyle Hidden -Wait
Start-Process -FilePath './Build/Windows/CarAR.exe' -ArgumentList '--carar-smoke -force-d3d11 -screen-fullscreen 0 -screen-width 1440 -screen-height 900 -logFile D:/Code/CSMR/CarAR/mr-refresh-smoke.log' -WindowStyle Hidden -Wait
```

自动验证覆盖 MRTK 单 / 双手操控、真实 Input System 控制器事件与 XRI 射线、摆放高度和地面轴心、锁定、车门来源白名单与独立运动、门盖、爆炸组装还原、模拟测光失效与模式切换。原生 URP 离屏检查图输出为 `Build/Windows/hdr-default.png`、`assembled.png`、`left-door.png`、`panels.png`、`exploded.png`。这些图片不包含真实相机视频。

`[CarAR Stability]` 记录车辆与头部变化、跟踪、Origin、帧率和手柄状态；`[CarAR View Sample]` 对比输入、相机和双眼中心。车辆根坐标不变不能单独证明现实中无滑动。头显走动、转头、跟踪丢失 / 恢复、真实地面贴合仍需要佩戴验收，当前没有跨会话持久锚点方案。

停车诊断记录 local/world scale，用于区分尺寸改变与摆放距离或头显合成造成的视觉变化。已删除重复的逐次缩放日志及命令来源参数；保留已验证的握键 / 摇杆防误触和双手距离退化保护。回归加入实机记录的 14.7 米头显坐标跳变：不一致期间禁止摆放、恢复后地面正确、同源定位恢复不搬车、有效坐标重置须重停。另验证上下门饰条随门运动，中控台保持静止。

保留 `Tools/Set-HuijingAlignment.ps1` 供显式诊断视频时序使用；它不是定位板校准。默认只设置 pose defer 为 0，不猜测视频时差。旧的 0ms 视频实验请求已移出活动文件，值保存在 `tmp/hjxr-alignment-before-marker-removal.json`，启动保留厂家视频时序。诊断改动仅影响该进程，不写回厂家配置。

此前设备排查日志和 Git 历史保留作证据，旧版定位板操作不适用于当前版本。MRTK 依赖及 Unity 6 兼容改动见 [迁移说明](MRTK3-MIGRATION.md)。旧 911 资源仍保留作历史资源，当前场景使用 AMG。
