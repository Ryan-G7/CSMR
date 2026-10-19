Unity MR 语音控制演示项目
==========================

项目概述
--------
本项目是一个基于 Unity 的混合现实(MR)语音控制演示，展示如何通过语音命令控制虚拟物体的旋转、缩放、变色，并通过 hjxr SDK 实现 VR 世界坐标系与真实世界坐标系的同步对齐。

技术栈
------
- Unity 版本：2022.3.62t11
- XR 框架：OpenXR (com.unity.xr.openxr 1.14.4-t2)
- 语音识别：Unity.Windows.Speech (KeywordRecognizer)
- 混合现实 SDK：AIXR Agent v2.5.64 (jxrsdk.dll)
- UI 组件：TextMesh Pro

目录结构
--------
├── 3rd/hjxr/          # hjxr SDK 头文件
│   ├── hjxr_api.h
│   ├── hjxr_pfn_defines.h
│   ├── hjxr_type.h
│   └── hjxr_version.h
├── Assets/
│   ├── Scripts/       # 核心脚本
│   │   ├── Record.cs          # 主控制脚本（语音识别、坐标同步）
│   │   └── HjxrNative.cs      # hjxr SDK 封装接口
│   ├── Materials/     # 材质文件
│   ├── Textures/      # 纹理资源
│   ├── Shader/        # 自定义着色器
│   ├── XR/            # OpenXR 配置
│   ├── XRI/           # XR Interaction 配置
│   └── StreamingAssets/ # 流式资源
├── Packages/          # Unity Package Manager 依赖
├── ProjectSettings/   # 项目设置
├── out/               # 预构建输出
│   └── voice_keywords_mr.exe  # Windows 可执行文件
└── README.txt         # 本文件

核心功能
--------

1. 语音命令控制
   - "旋转"：切换物体自动旋转状态
   - "缩放"：切换物体缩放状态（自动放大/缩小循环）
   - "变色"：切换物体颜色（白→红→黄→蓝→绿→青循环）
   - "对齐"：执行 VR 世界坐标系与真实世界坐标系同步

2. 键盘快捷键
   - K 键：执行坐标系对齐（与"对齐"语音命令等效）
   - ESC 键：退出应用

3. 坐标系同步机制
   - 在 VR 世界中指定虚拟 marker 对象 (mVirtualMarkerObject)
   - 将其位姿通过 WorldSetMarkerPose 接口设置给 hjxr SDK
   - 调用 WorldSync 接口完成坐标系同步
   - 坐标转换规则：Unity 坐标系 → OpenXR 坐标系
     * position.z 取负
     * rotation.z 和 rotation.w 取负

运行方式
--------

Unity Editor 运行
1. 使用 Unity 2022.3.62t11 打开项目
2. jxrsdk.dll 已放置在C:\Users\Admin\AppData\Local\Programs\AIXR Agent\resource\tool\api_layer\Release
3. 打开 Assets/Scenes/SampleScene.unity
4. 点击 Play 按钮运行


关键脚本说明
------------

Record.cs (Assets/Scripts/Record.cs)
- 初始化 hjxr SDK 并设置虚拟 marker 位姿
- 管理语音识别器，处理语音命令回调
- 实现物体旋转、缩放、变色逻辑
- 处理键盘输入和坐标系同步

HjxrNative.cs (Assets/Scripts/HjxrNative.cs)
- C# 封装 hjxr SDK 的 C 接口
- 提供 Init/Deinit/Ioctl 方法
- 定义 HjxrResult、HjxrCmdType 枚举和位姿结构体

使用示例
--------
1. 运行应用后，物体会自动旋转
2. 说"缩放"，物体会开始自动放大/缩小
3. 说"变色"，物体颜色会切换
4. 说"对齐"或按 K 键，触发坐标系同步
5. 按 ESC 键退出应用

注意事项
--------
- 语音识别功能依赖 Windows 语音服务，需确保系统已安装中文语音包
- 坐标系同步功能依赖 hjxr SDK 和配套硬件环境
