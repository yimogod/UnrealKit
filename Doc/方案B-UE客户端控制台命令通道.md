# 方案 B — UE 客户端自研 TCP 控制台命令通道

最后更新：2026-08-21 | 状态：UnrealKit 侧已实施（步骤 2–5），UE 侧插件待实现（步骤 1）

> **实施进度**：UnrealKit 的传输层、TCP 通道、配置与测试已完成，Android 的
> `SendConsoleCommandAsync` 已切到 TCP。**UE 侧的 `UkRemoteCommand` 插件尚未实现**，
> 因此在插件落地前 Android 发指令会以 `UKC101`（连接被拒绝）失败——这是预期状态，
> 不是配置错误。实际代码路径见本文末「实施结果」一节。

## 目标

让 UnrealKit（Windows PC 桌面工具）向**运行中的 UE 客户端**发送控制台指令（如 `stat unit`），覆盖两类目标：

| 目标 UE 客户端 | 连接方式 | 说明 |
| --- | --- | --- |
| Windows 本机 | localhost | 与本机运行的 UE 进程通信 |
| Android 手机 | USB / ADB | 经 `adb forward` 端口转发 |

## 为什么需要方案 B（而非引擎自带 Web Remote Control）

引擎自带的 **Web Remote Control** 是首选，但它对 Android 有一个硬性限制，源码已核实：

- 插件 `Engine/Plugins/VirtualProduction/RemoteControl/RemoteControl.uplugin` 中，实际承载 HTTP/WebSocket 服务的模块 `WebRemoteControl` 带 `PlatformAllowList`，**只允许 `Mac` / `Win64` / `Linux`，不含 Android**。
- 也就是说，Android 构建里根本不编译进 Remote Control 的 HTTP 服务器。`bAllowConsoleCommandRemoteExecution`、`/remote/object/call` → `ExecuteConsoleCommand` 这条链在手机上不存在。
- 要绕过它只能**改引擎源码**（同时改 `WebRemoteControl` 与依赖 `WebSocketNetworking` 两处白名单，后续还要维护引擎 fork），代价高、收益单一，与本项目「改动小而聚焦」的原则冲突。

因此 Android 目标走**方案 B：在 UE 客户端内自研一个轻量 TCP 命令监听插件**，UnrealKit 通过 `adb forward` 直连该端口发命令。

关键设计取舍（已确认）：

- 不碰组播、不碰反向连接、不引入 Python 运行时。
- 插件是**项目自有的 UE 侧资产**，不修改引擎。

## 现状盘点（重要）

代码库里**已经有一套 Web Remote Control 的 HTTP 客户端实现**，方案 B 要复用其中大部分结构，不要重写：

路径均以仓库根为起点（解决方案在 `UnrealKit/` 子目录下）。

| 现有文件 | 作用 | 方案 B 中的去向 |
| --- | --- | --- |
| `UnrealKit/UnrealKit.Core/RemoteControl/IRemoteControlService.cs` | HTTP 客户端抽象 | 保留原样；传输抽象另起 `ICommandTransport` |
| `UnrealKit/UnrealKit.Core/RemoteControl/RemoteControlService.cs` | `PUT /remote/object/call` → `ExecuteConsoleCommand` | 保留，由 `HttpCommandTransport` 包装成 **Win64 通道** |
| `UnrealKit/UnrealKit.Core/RemoteControl/RemoteControlModels.cs` | `RemoteControlOptions` / `RemoteControlCommandRequest` | 保留原样；通道配置另起 `CommandChannelOptions` |
| `UnrealKit/UnrealKit.Core/Devices/AdbDeviceService.cs` | `SendConsoleCommandAsync`：`adb forward` 30010 + 调 HTTP | 已改为**走 TCP 通道**（Android），转发端口取自通道 |
| `UnrealKit/UnrealKit.Core/Devices/Win64DeviceService.cs` | `SendConsoleCommandAsync`：直连本机 HTTP 30010 | 已改为走 `ICommandTransport`（默认仍是 HTTP） |
| `UnrealKit/UnrealKit.Core/Console/ConsoleCommandService.cs` | 面向 `IDeviceService` 的统一指令/序列/条件执行 | 未改（已解耦平台） |
| `UnrealKit/UnrealKit.Core/Projects/ProjectModels.cs` | `RemoteControlHttpPort` 等配置字段 | 已追加 `CommandTcpPort` / `*CommandTransport` |

**结论：方案 B 的 UnrealKit 侧改动集中在「传输层」——新增一个传输接口与 TCP 实现，`AdbDeviceService` 从 HTTP 切换到 TCP。** Win64 目标继续用现有 HTTP 实现，两平台对上层 `ConsoleCommandService` 保持无感。

## 设计

### 1. UE 侧：自研 TCP 命令监听插件（Android 必需，Win64 可选）

一个最小的 UE C++ 插件，例如 `UkRemoteCommand`：

- 在游戏线程或专用线程启动一个 `FTcpListener`（`FSocket`），监听固定端口（如 `39010`）。
- 收到一行文本（以 `\n` 结尾）视为一条控制台命令，调用 `GEngine->Exec(nullptr, *Command)` 或 `UKismetSystemLibrary::ExecuteConsoleCommand(nullptr, Command, nullptr)`。
- 返回执行结果（成功/失败 + 输出文本），格式见下节协议。
- 只接受 `127.0.0.1` 回环连接（Android 上经 `adb forward` 后，远端连接来源即为本机回环），**不监听 `0.0.0.0`**，避免暴露到局域网。
- 打包 Android 时随游戏一起编译；Win64 本地目标若也想走 TCP，可同样启用（或继续走 HTTP，二选一，见「待决策」）。

> 注意：Android 上 `ExecuteConsoleCommand` 内部会 `World->GetFirstPlayerController()->ConsoleCommand(...)`，`stat unit` 这类渲染统计命令**不依赖具体 World 对象**，`WorldContextObject` 传 `nullptr` 即可；但命令通道必须等到 World 就绪后再接受连接，否则早期连接会找不到 PlayerController。

### 2. UnrealKit 侧：传输层抽象

把现有的 `IRemoteControlService` 泛化为命令传输抽象（名字可定为 `ICommandTransport` 或保留 `IRemoteControlService`，待决策）：

```
ICommandTransport
  Task<ProcessExecutionResult> SendAsync(
      string host, int port, string command, ... )   // 或沿用 RemoteControlCommandRequest 形态
```

两个实现：

- `HttpCommandTransport` —— 即现有 `RemoteControlService`（`PUT /remote/object/call`），服务 **Win64**。
- `TcpCommandTransport` —— 新增，`TcpClient` 连 `127.0.0.1:<port>`，按协议发命令，服务 **Android**。

`AdbDeviceService.SendConsoleCommandAsync` 的改动：

1. `EnsurePortForwardedAsync` 保持不动（`adb forward tcp:<port> tcp:<port>` 的语义对 TCP 命令通道同样适用）。
2. 把 `_remoteControl.SendConsoleCommandAsync(...)` 换成 `_commandTransport.SendAsync(host: "127.0.0.1", port: TcpPort, command)`。
3. 失败包装 `RemoteControlException` → `DeviceCommandException` 的逻辑保留。

### 3. 协议（建议：换行分隔的文本 + JSON 响应）

最简、可诊断、易实现：

- **请求**：单行 UTF-8 文本 + `\n`，即命令本体（如 `stat unit`）。
- **响应**：单行 UTF-8 JSON + `\n`：

```json
{ "ok": true, "output": "..." }
{ "ok": false, "error": "Unknown command or execution failed: ..." }
```

理由：

- 换行分隔天然支持 `TcpClient` 的 `StreamReader.ReadLineAsync`，无需手写帧长解析。
- JSON 响应携带 `ok` + `output`/`error`，符合本项目「失败要具体」的约定——命令执行失败必须返回原因，不静默。
- 与现有 `ProcessExecutionResult`（`ExitCode` + `StandardOutput` + `StandardError`）可一一映射：`ok=false` → 非零 `ExitCode`，`output` → `StandardOutput`，`error` → `StandardError`。

### 4. 配置

在 `ProjectSettings` 中新增（沿用现有 `RemoteControl*` 命名风格，向后追加不破坏既有归档）：

- `CommandTcpPort`（默认 `39010`）—— UE 侧插件监听端口 + `adb forward` 端口。
- 可选 `CommandTransport`（`Http` / `Tcp`）—— 显式声明每平台用哪条通道，**不隐式推断**（符合「无隐式选择」约定）。

`ProjectService` 的读写/校验照 `RemoteControlHttpPort` 的模式补齐。

## 诊断码

新增分域 `UKC`（UnrealKit Command 通道），向后追加：

| 码 | 含义 |
| --- | --- |
| `UKC101` | TCP 命令通道连接被拒绝（UE 插件未启动 / 端口未监听 / `adb forward` 未生效） |
| `UKC102` | 命令执行失败（UE 返回 `ok=false`） |
| `UKC103` | 响应超时或协议解析失败（非 JSON / 不完整） |

> 现有诊断码分域见 `Doc/解析导出与诊断.md`。新增码只在 `UKC` 域内追加。

## 实施步骤

1. **UE 侧（待实现）**：`UkRemoteCommand` 插件（TCP 监听 + `ExecuteConsoleCommand` + JSON 响应），Android 打包验证 `stat unit` 可用。
2. ~~Core：传输抽象 + `TcpCommandTransport`~~ 已完成，见下。
3. ~~Core：`AdbDeviceService` 切到 TCP；`ProjectSettings` 补通道字段~~ 已完成。
4. ~~CLI / WPF：确认上层无感~~ 已完成——`ConsoleCommandService` 依赖 `IDeviceService`，通道切换对 CLI 与 WPF 无改动，因此**没有**引入 `--transport` 参数：通道是工程配置（每个平台一次性声明），不是每条命令的选项。
5. ~~测试：本地 `TcpListener` 假服务器金样测试~~ 已完成，14 个用例。

## 实施结果（UnrealKit 侧）

新命名空间 `UnrealKit.Core.CommandChannel`（`UnrealKit/UnrealKit.Core/CommandChannel/`）：

| 文件 | 内容 |
| --- | --- |
| `ICommandTransport.cs` | 传输抽象：`Kind` / `Port` / `SendConsoleCommandAsync(command, …)` |
| `CommandChannelModels.cs` | `CommandTransportKind`（`Http`/`Tcp`）、`CommandChannelOptions`、`CommandTransportException`、`CommandChannelDiagnosticCodes`（`UKC*`） |
| `TcpCommandTransport.cs` | TCP 通道：连回环、单行命令、单行 JSON 响应、`UKC*` 归类 |
| `HttpCommandTransport.cs` | HTTP 通道：既有 `RemoteControlService` 的薄适配，把 `RemoteControlException` 归一到 `CommandTransportException` |

对既有代码的改动：

- `IRemoteControlService` / `RemoteControlService` **保留原样**，不改名、不泛化。`RemoteControl` 一词确实特指 HTTP 方案，因此新抽象另起 `ICommandTransport`，HTTP 实现包在 `HttpCommandTransport` 里——待决策 1 按「改名」的方向落地，但通过新增而非重命名达成，既有调用方与测试不受影响。
- `AdbDeviceService` / `Win64DeviceService` 的构造参数由 `RemoteControlOptions` + `IRemoteControlService` 换成 `CommandChannelOptions` + `ICommandTransport`（破坏性，见 `CHANGELOG.md`）。
- `EnsurePortForwardedAsync` 转发的端口改为取自 `ICommandTransport.Port`，与实际连接端口同源。
- `ProjectSettings` 新增 `CommandTcpPort` / `AndroidCommandTransport` / `Win64CommandTransport`，向后追加，既有 `.ukit` 与归档照常反序列化。

已按取舍拍板的待决策：

1. **传输抽象命名** → 新增 `ICommandTransport`，保留 `IRemoteControlService` 作为 HTTP 客户端。
2. **Win64 是否也切 TCP** → 保留双实现，且**由配置声明而非代码推断**：默认 `Win64CommandTransport=Http`（零额外插件成本）、`AndroidCommandTransport=Tcp`。改过引擎白名单的 fork 可以把 Android 也设成 `Http`。
3. **UE 插件监听范围** → 客户端只连 `127.0.0.1`，插件应只监听回环。「`adb forward` 后连接来源表现为回环」这一点**仍需在真机上确认**（UnrealKit 侧无法验证）。
4. **端口选择** → 默认 `39010`，与 Remote Control（30010/30020）、Unreal Insights（1980）、Session Frontend（6666/6776）不冲突；**设备上是否与其它应用冲突仍需真机确认**，冲突时改 `CommandTcpPort` 即可，无需改代码。

## 关键源码引用（便于后续复核）

- 引擎 Web Remote Control 白名单：`E:\ProjectDev\Engine\Plugins\VirtualProduction\RemoteControl\RemoteControl.uplugin`（`WebRemoteControl` 模块 `PlatformAllowList`）
- 控制台命令门控：`...\WebRemoteControl\Private\WebRemoteControlInternalUtils.cpp`（`ValidateFunctionCall`，约 545 行起）
- Runtime 默认关闭：`...\WebRemoteControl\Private\WebRemoteControl.cpp`（`IsWebControlEnabledInEditor`，约 222 行起）
- TCP 通道实现：`UnrealKit/UnrealKit.Core/CommandChannel/TcpCommandTransport.cs`
- 现有 HTTP 客户端：`UnrealKit/UnrealKit.Core/RemoteControl/RemoteControlService.cs`
- Android 端口转发：`UnrealKit/UnrealKit.Core/Devices/AdbDeviceService.cs`（`EnsurePortForwardedAsync`）
- 金样测试：`UnrealKit/UnrealKit.Tests/TcpCommandTransportTests.cs`
