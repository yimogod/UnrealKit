# UnrealKit 第四阶段 TODO：Win64 全流程重构

基于第二阶段 P6（Win64 设备基础支持）的延续，将 Win64 支持从 Core 层扩展到全链路（WPF Desktop GUI + Capture + 启动闭环），使 Windows 版 UE 游戏与 Android 获得同等的工具链体验。

最后更新：2026-08-10 | 框架 .NET 9 / WPF | P1-P5 完成 | 构建 0 警告 140/140 测试通过

---

## 背景

第二阶段 P6 已完成 Core 层 Win64 基础设施：
- `Win64DeviceService`：`IDeviceService` 实现，通过 `System.Diagnostics.Process` 采集进程内存
- `Win64MemInfoParser`：解析 Win64 meminfo 结构化文本
- `Win64Device`：`IDevice` 实现（`Id="localhost"`, `Platform="Win64"`）
- `PullDirectoryAsync` / `PushFileAsync` / `DeleteRemoteFileAsync`：映射为本地文件系统操作
- `.ukit` 工程创建支持 `--platform Win64`
- `TargetPlatform.Win64` / `Win64Executable` / `Win64WorkingDirectory` 持久化到 `DefaultGame.ini`
- CLI：`devices` 命令同时列出 Win64 与 ADB 设备

**但以下层面尚未集成：**
- ❌ WPF Desktop GUI：设备列表为 `ObservableCollection<AdbDevice>`，不显示 Win64 设备
- ❌ Desktop 采集/启动全链路硬编码走 `AdbDeviceService`
- ❌ Desktop 项目设置页无 Win64 平台配置入口
- ❌ Win64 上 `SendConsoleCommandAsync` 抛 `NotSupportedException`
- ❌ Win64 进程启动/停止未与 GUI 闭环对接

---

## 完成情况

### P1：Desktop 设备抽象层重构 ✅

- [x] `ShellViewModel.Devices` 从 `ObservableCollection<AdbDevice>` 改为 `ObservableCollection<IDevice>`
- [x] `SelectedDevice` 从 `AdbDevice?` 改为 `IDevice?`
- [x] Core 新增 `IDeviceServiceFactory` / `DeviceServiceFactory`，根据设备 Platform 返回 `AdbDeviceService` 或 `Win64DeviceService`
- [x] `RefreshDevicesAsync` 合并 ADB 设备与 Win64 设备列表
- [x] 所有依赖 `SelectedDevice.SerialNumber` 的代码适配 `IDevice.Id`
- [x] XAML 设备列表绑定更新（`Id` / `IsAvailable` / `Name`）

### P2：Desktop 采集流程统一 ✅

- [x] `RunCaptureAsync` 从硬编码 `new CaptureService(new AdbDeviceService(...))` 改为通过工厂创建
- [x] `CaptureService` 接受 `IDevice`（已有支持）
- [x] GUI 采集预览适配 Win64 路径

### P3：Desktop Win64 项目设置页 ✅

- [x] 项目设置页新增 Platform 选择（Android / Win64）
- [x] Win64 选中时显示 `Win64Executable` / `Win64WorkingDirectory` 输入框
- [x] Android 选中时隐藏 Win64 字段（Android 字段同样条件显示）
- [x] 保存/加载时正确读写 `DefaultGame.ini` 的 `Platform` / `Win64Executable` / `Win64WorkingDirectory`

### P4：Win64 进程启动/停止闭环 ✅

- [x] 桌面启动按钮适配 Win64（`ShellViewModel.StartApplicationAsync` 按平台分发）
- [x] 启动状态反馈（进程路径、退出码）
- [x] 停止进程支持（`StopApplicationAsync`：Android `am force-stop` / Win64 `Process.Kill()`）

### P5：Win64 控制台指令 ✅

- [x] Android/Win64 上 `SendConsoleCommandAsync`：通过 UE Web Remote Control HTTP API（`PUT localhost:30010/remote/object/call`）
- [x] Desktop GUI 控制台页适配：`SendConsoleCommandAsync` 统一走 Remote Control（Android 先 `adb forward`，Win64 本机 HTTP）

### P6：测试与构建验证 ✅

- [x] 构建 0 警告 0 错误
- [x] 140/140 测试通过
- [ ] Win64 采集端到端集成测试（后续）

---

## 新增/修改文件

| 文件 | 变更 |
|------|------|
| `UnrealKit.Core/Devices/DeviceServiceFactory.cs` | **新增**：IDeviceServiceFactory 接口 + 默认实现 |
| `UnrealKit.Desktop/ShellViewModel.cs` | **修改**：AdbDevice → IDevice；新增 Platform/Win64 属性；采集使用工厂 |
| `UnrealKit.Desktop/MainWindow.xaml` | **修改**：设备列表绑定；新增 Platform 选择器 + Win64 字段 |
| `UnrealKit.Tests/DesktopShellViewModelTests.cs` | **修改**：适配 IDevice API |
| `UnrealKit.Core/Adb/IAdbService.cs` | **修改**：新增 `ForceStopApplicationAsync` |
| `UnrealKit.Core/Adb/AdbService.cs` | **修改**：实现 `ForceStopApplicationAsync` |
| `UnrealKit.Core/Devices/IDeviceService.cs` | **修改**：新增 `StopApplicationAsync` |
| `UnrealKit.Core/Devices/AdbDeviceService.cs` | **修改**：实现 `StopApplicationAsync` |
| `UnrealKit.Core/Devices/Win64DeviceService.cs` | **修改**：实现 `StopApplicationAsync`（进程 Kill） |
| `UnrealKit.Tests/CaptureServiceTests.cs` | **修改**：FakeAdbService 新增 `ForceStopApplicationAsync` |
| `UnrealKit.Tests/LaunchParameterServiceTests.cs` | **修改**：RecordingAdbService 新增 `ForceStopApplicationAsync` |

---

## 架构约束

遵循根 AGENTS.md 核心不变项。

---

## 下一步优先级

1. ✅ **P1 Desktop 设备抽象层** -- `IDevice` 替换 `AdbDevice`
2. ✅ **P2 Desktop 采集统一** -- `IDeviceServiceFactory` + Win64 采集流程
3. ✅ **P3 Desktop Win64 项目设置** -- Platform 选择 + 字段切换
4. ✅ **P4 Win64 进程启停** -- Start/Stop 闭环
5. ✅ **P5 Win64 控制台指令** -- HTTP Remote Control
6. ⬜ **P6 端到端集成测试** -- Win64 Capture 全链路
