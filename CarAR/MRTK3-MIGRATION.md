# MRTK / Unity 6 集成维护说明

六个 MRTK 包固定在仓库 `Vendor/MRTK`，来源与许可证见 [Vendor 说明](../Vendor/README.md)。XRI 2.6.5 和 Graphics Tools 0.8.0 嵌入 `CarAR/Packages`。注册表包及确切版本由 `Packages/manifest.json` 和 `packages-lock.json` 管理，不依赖旧的外部 MRTK 源码目录。

## Unity 6000.6 兼容修改

- XRI 编辑器调试器使用泛型 TreeView 和 EntityId，新版 Unity 不编译旧 VR 相机测试辅助代码。
- Graphics Tools 编辑器对象查找使用 EntityId，网格合并诊断采用其哈希值。
- Unity 6000.3 及以上不编译依赖旧 URP 接口的 Acrylic、Magnifier 和旧全屏渲染功能；MRTK 标准 UI 材质保留。

更新依赖时应检查这些兼容补丁，并重跑 `Tools/Build-Windows.ps1 -Smoke`。

## 当前交互与设备边界

MRTK 提供 XR Rig、标准 Action Button 和 ObjectManipulator，XRI 提供原生手柄动作、射线与选择。手柄与头显共用 CameraFloorOffsetObject；应用负责车辆停车、地面轴心、门盖动画和误触保护。握键与按钮使用不同交互层，视线不触发选择。双手缩放使用 `VehicleScaleLogic`，跟踪退化时重新建立距离基准。

荟镜 HJXR 使用安装在本机的厂商 SDK 与 Pimax OpenXR，运行时显式加载 SDK；仓库不分发替代 DLL。Windows 使用 `OpenXRLoaderNoPreInit`，避免临时 OpenXR 实例破坏厂商相机配置，构建会检查 boot.config。输入启动后 MR 请求 Device / LOCAL，额外眼高为零；VR 优先请求 Floor，异常眼高才回退 Device 并建立站立眼高。

MR 相机保持透明背景，停用会覆盖透视相机配置的 MRTK CameraSettingsManager / UnboundedTrackingMode。真实透视由厂商合成层负责。ARFoundation 没有已验证的本机测光与环境探针提供方，默认使用静态 HDR。

实体定位板、厂商日志结果解析及自定义 OpenXR 输入空间变换已经移除。保留原生跟踪；自动回归验证世界根节点固定、控制器交互与失跟恢复，不能证明真实房间中的透视配准精度。设备显示、手柄跟踪、现实地面贴合和跨视角稳定性仍需佩戴验收，当前未实现跨会话持久锚点。
