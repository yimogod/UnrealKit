# 方案 B — UE 客户端 Web Remote Control HTTP 控制台命令通道

最后更新：2026-08-21 | 状态：UnrealKit 侧已统一到 Web Remote Control HTTP，Android 需改引擎白名单（用户侧待办）

> **实施进度**：UnrealKit 侧的传输层已统一为 HTTP——`ICommandTransport` 只有一个实现
> `HttpCommandTransport`，Android 与 Win64 的 `SendConsoleCommandAsync` 都走引擎自带
> Web Remote Control 的 `PUT /remote/object/call`。自研的 `RemoteControlLite` TCP 插件已删除。
> **唯一剩余事项在引擎侧**：Android 构建要带上 Web Remote Control，需改引擎白名单（见下），
> 这不在 UnrealKit 代码内，由用户手动完成。

## 目标

让 UnrealKit（Windows PC 桌面工具）向**运行中的 UE 客户端**发送控制台指令（如 `stat unit`），覆盖两类目标：

| 目标 UE 客户端 | 连接方式 | 说明 |
| --- | --- | --- |
| Windows 本机 | localhost | 与本机运行的 UE 进程通信 |
| Android 手机 | USB / ADB | 经 `adb forward` 端口转发 |

## 为什么统一走 Web Remote Control HTTP（而非自研 TCP 插件）

引擎自带的 **Web Remote Control** 是首选通道，其 HTTP 客户端实现成熟、协议稳定。此前顾虑的是 Android 支持：

- 插件 `Engine/Plugins/VirtualProduction/RemoteControl/RemoteControl.uplugin` 中，实际承载 HTTP/WebSocket 服务的模块 `WebRemoteControl` 带 `PlatformAllowList`，默认**只允许 `Mac` / `Win64` / `Linux`，不含 Android**。
- 因此 Android 构建默认不编译 Remote Control 的 HTTP 服务器。

绕过它需要**改引擎白名单**（同时改 `WebRemoteControl` 与依赖的 `WebSocketNetworking.uplugin` 两处 `PlatformAllowList`，把 `Android` 加进去）。这一改动在引擎源码内，**不在 UnrealKit 仓库**，由用户负责并自行维护引擎 fork。

这是唯一一处「改动引擎」的代价；换来的是 Android 与 Win64 共用同一条成熟通道、同一套协议，不再维护自研 TCP 插件 + 自定义 JSON 分帧协议，与项目「逻辑不重复」的约定一致。

## 现状盘点

代码库里现有的 HTTP 客户端实现（`UnrealKit.Core.RemoteControl` 下的 `IRemoteControlService` /
`RemoteControlService` / `RemoteControlModels`）继续保留，作为 `HttpCommandTransport` 的底层：

| 文件 | 作用 | 去向 |
| --- | --- | --- |
| `UnrealKit/UnrealKit.Core/RemoteControl/IRemoteControlService.cs` | HTTP 客户端抽象 | 保留 |
| `UnrealKit/UnrealKit.Core/RemoteControl/RemoteControlService.cs` | `PUT /remote/object/call` → `ExecuteConsoleCommand` | 保留 |
| `UnrealKit/UnrealKit.Core/RemoteControl/RemoteControlModels.cs` | `RemoteControlOptions` / `RemoteControlCommandRequest` | 保留 |
| `UnrealKit/UnrealKit.Core/CommandChannel/HttpCommandTransport.cs` | HTTP 传输适配 | 保留，唯一 `ICommandTransport` 实现 |
| `UnrealKit/UnrealKit.Core/Devices/AdbDeviceService.cs` | `SendConsoleCommandAsync`：`adb forward` + 走 HTTP | 走 `HttpCommandTransport` |
| `UnrealKit/UnrealKit.Core/Devices/Win64DeviceService.cs` | `SendConsoleCommandAsync`：直连本机回环 | 走 `HttpCommandTransport` |
| `UnrealKit/UnrealKit.Core/Console/ConsoleCommandService.cs` | 面向 `IDeviceService` 的统一指令/序列/条件执行 | 未改（已解耦平台） |
| `UnrealKit/UnrealKit.Core/Projects/ProjectModels.cs` | 通道配置字段 | `RemoteControlHttpPort` / `RemoteControlObjectPath` / `RemoteControlFunctionName` / `RemoteControlCommandParameter` |

**结论：UnrealKit 侧改动集中在「传输层」——`ICommandTransport` 只保留 HTTP 实现，
两个 `DeviceService` 都走 `HttpCommandTransport`。** 两平台对上层 `ConsoleCommandService` 保持无感。

## 设计

### 1. UE 侧：Web Remote Control（Android 需改白名单）

- 启用引擎 Web Remote Control 插件，Win64 开箱即用（`WebRemoteControl` 白名单已含 `Win64`）。
- Android 构建需把 `Android` 加进以下两处 `PlatformAllowList`（引擎源码，用户侧改动）：
  - `Engine/Plugins/VirtualProduction/RemoteControl/RemoteControl.uplugin` 的 `WebRemoteControl` 模块
  - `Engine/Plugins/Networking/WebSocketNetworking/WebSocketNetworking.uplugin`（依赖模块，同样带白名单）
- 命令入口为 `PUT http://127.0.0.1:<port>/remote/object/call`，走
  `ExecuteConsoleCommand`，参数由 `parameters.<RemoteControlCommandParameter>` 携带。

> 注意：Android 上 `ExecuteConsoleCommand` 内部依赖 PlayerController，命令通道要等 World 就绪后再发，否则早期指令找不到执行对象。

### 2. UnrealKit 侧：传输层抽象

命令传输抽象为 `ICommandTransport`，唯一实现 `HttpCommandTransport`：

```
ICommandTransport
  CommandTransportKind Kind { get; }   // 恒为 Http
  int Port { get; }                    // 取自 RemoteControlOptions.HttpPort
  Task<ProcessExecutionResult> SendConsoleCommandAsync(string command, ...)
```

`HttpCommandTransport` 是 `RemoteControlService` 的薄适配：把 `RemoteControlException`
归一到带 `UKC*` 码的 `CommandTransportException`，让上层只处理一种失败类型；HTTP 请求构造与
错误文案仍留在 `RemoteControlService`，不复制一份。

`AdbDeviceService.SendConsoleCommandAsync` 的改动：

1. `EnsurePortForwardedAsync` 保持不动（`adb forward tcp:<port> tcp:<port>` 对 HTTP 通道同样适用）。
2. 端口取自 `ICommandTransport.Port`（即 `RemoteControlHttpPort`），与实际连接端口同源。
3. 失败包装 `CommandTransportException` → `DeviceCommandException` 的逻辑保留。

### 3. 协议

引擎 Web Remote Control 的既有协议，无自定义分帧：

- **请求**：`PUT http://127.0.0.1:<port>/remote/object/call`，JSON body：

```json
{
  "objectPath": "/Script/Engine.Default__KismetSystemLibrary",
  "functionName": "ExecuteConsoleCommand",
  "parameters": { "Command": "stat unit" },
  "generateTransaction": true
}
```

- **响应**：成功返回 HTTP 2xx；非成功状态由 `RemoteControlService` 抛 `RemoteControlException`，
  再归一到 `UKC102`。命令本体放进 JSON 字段，特殊字符由 JSON 序列化安全编码。

### 4. 配置

`ProjectSettings` 中的通道字段（默认值）：

| 字段 | 默认值 |
| --- | --- |
| `RemoteControlHttpPort` | `30010` |
| `RemoteControlObjectPath` | `/Script/Engine.Default__KismetSystemLibrary` |
| `RemoteControlFunctionName` | `ExecuteConsoleCommand` |
| `RemoteControlCommandParameter` | `Command` |

`ProjectService` 读写与校验照 `RemoteControlHttpPort` 的模式补齐：端口越界或非数字报错、
不静默回退；空值回退默认值。

## 诊断码

新增分域 `UKC`（UnrealKit Command 通道），向后追加：

| 码 | 含义 |
| --- | --- |
| `UKC101` | 连接通道失败或超时（连接被拒绝 / 超时，UE 未启动、端口未监听、`adb forward` 未生效） |
| `UKC102` | 命令执行失败（Remote Control 返回非成功 HTTP 状态） |
| `UKC103` | 协议异常（响应缺失、超长或不是预期格式） |

> 现有诊断码分域见 `Doc/解析导出与诊断.md`。新增码只在 `UKC` 域内追加。

## 实施步骤

1. ~~Core：传输抽象 `ICommandTransport` + `HttpCommandTransport`~~ 已完成，见下。
2. ~~Core：`AdbDeviceService` / `Win64DeviceService` 切到 HTTP；`ProjectSettings` 用 `RemoteControl*` 字段~~ 已完成。
3. ~~CLI / WPF：确认上层无感~~ 已完成——`ConsoleCommandService` 依赖 `IDeviceService`，通道切换对 CLI 与 WPF 无改动，因此**没有**引入 `--transport` 参数：通道是工程配置，不是每条命令的选项。
4. ~~测试：`RemoteControlServiceTests` 的 HTTP 假响应金样测试~~ 已完成，6 个用例。
5. **引擎侧（用户负责）**：Android 构建改 `WebRemoteControl` + `WebSocketNetworking` 两处 `PlatformAllowList`，加 `Android`；真机验证 `stat unit` 可用。

## 实施结果（UnrealKit 侧）

命名空间 `UnrealKit.Core.CommandChannel`（`UnrealKit/UnrealKit.Core/CommandChannel/`）：

| 文件 | 内容 |
| --- | --- |
| `ICommandTransport.cs` | 传输抽象：`Kind` / `Port` / `SendConsoleCommandAsync(command, …)` |
| `CommandChannelModels.cs` | `CommandTransportKind`（仅 `Http`）、`CommandChannelOptions`、`CommandTransportException`、`CommandChannelDiagnosticCodes`（`UKC*`） |
| `HttpCommandTransport.cs` | HTTP 通道：经 `RemoteControlService` 发 `PUT /remote/object/call`、`UKC*` 归类 |

对既有代码的改动：

- `IRemoteControlService` / `RemoteControlService` / `RemoteControlModels` 继续保留，作为 HTTP 通道的底层。
- `AdbDeviceService` / `Win64DeviceService` 的构造参数为 `CommandChannelOptions` + `ICommandTransport`（破坏性，见 `CHANGELOG.md`）。
- `EnsurePortForwardedAsync` 转发的端口取自 `ICommandTransport.Port`，与实际连接端口同源。
- `ProjectSettings` 使用 `RemoteControlHttpPort` / `RemoteControlObjectPath` / `RemoteControlFunctionName` / `RemoteControlCommandParameter`，向后追加，既有 `.ukit` 与归档照常反序列化。

已按取舍拍板的待决策：

1. **通道统一** → Android 与 Win64 都走 HTTP，`ICommandTransport` 唯一实现 `HttpCommandTransport`。
2. **UE 插件监听范围** → 客户端只连 `127.0.0.1`，引擎 Web Remote Control 默认监听回环。「`adb forward` 后连接来源表现为回环」在真机上验证。
3. **端口选择** → 默认 `30010`（Web Remote Control 的既有默认），与 Unreal Insights（1980）、Session Frontend（6666/6776）不冲突；设备上若冲突改 `RemoteControlHttpPort` 即可，无需改代码。

## 关键源码引用（便于后续复核）

- 引擎 Web Remote Control 白名单：`Engine/Plugins/VirtualProduction/RemoteControl/RemoteControl.uplugin`（`WebRemoteControl` 模块 `PlatformAllowList`）
- 依赖模块白名单：`Engine/Plugins/Networking/WebSocketNetworking/WebSocketNetworking.uplugin`（`PlatformAllowList`）
- 控制台命令门控：`...\WebRemoteControl\Private\WebRemoteControlInternalUtils.cpp`（`ValidateFunctionCall`，约 545 行起）
- Runtime 默认关闭：`...\WebRemoteControl\Private\WebRemoteControl.cpp`（`IsWebControlEnabledInEditor`，约 222 行起）
- HTTP 通道实现：`UnrealKit/UnrealKit.Core/CommandChannel/HttpCommandTransport.cs`
- HTTP 客户端：`UnrealKit/UnrealKit.Core/RemoteControl/RemoteControlService.cs`
- Android 端口转发：`UnrealKit/UnrealKit.Core/Devices/AdbDeviceService.cs`（`EnsurePortForwardedAsync`）
- 金样测试：`UnrealKit/UnrealKit.Tests/RemoteControlServiceTests.cs`
