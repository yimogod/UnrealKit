using UnrealKit.Core.Processes;
using UnrealKit.Core.Projects;
using UnrealKit.Core.RemoteControl;

namespace UnrealKit.Core.CommandChannel;

/// <summary>
/// 控制台指令通道的传输方式。
///
/// 取值是配置契约（<c>Config/DefaultGame.ini</c> 的 <c>RemoteControl*</c>），
/// 不从平台隐式推断。当前只有 <c>Http</c> 一种：Android 与 Win64 都走引擎自带
/// Web Remote Control 的 HTTP 服务（Android 需改引擎 <c>WebRemoteControl</c> /
/// <c>WebSocketNetworking</c> 两处 <c>PlatformAllowList</c> 加入 Android）。
/// </summary>
public enum CommandTransportKind
{
    /// <summary>引擎自带 Web Remote Control 的 HTTP 服务。</summary>
    Http
}

/// <summary>
/// 读回 cvar 时的取值类型。
///
/// 定义在 CommandChannel 而不是 Console：<c>Devices</c> 与 <c>Console</c> 都要引用它，
/// 放 <c>Console</c> 会造成 <c>Devices → Console</c> 的命名空间环。
/// </summary>
public enum ConsoleVariableType
{
    /// <summary>开关型 cvar，如 <c>showflag.Fog</c>。</summary>
    Bool,

    /// <summary>数值型 cvar，如 <c>r.screenpercentage</c>。整数 cvar 也归这里。</summary>
    Number
}

/// <summary>
/// <c>UKC</c> 域诊断码：控制台指令通道。向后追加，不复用已发布编号。
/// </summary>
public static class CommandChannelDiagnosticCodes
{
    /// <summary>连接被拒绝或超时（UE 未启动 / Remote Control 未启用 / <c>adb forward</c> 未生效）。</summary>
    public const string ConnectFailed = "UKC101";

    /// <summary>命令执行失败（Remote Control 返回非成功 HTTP 状态码）。</summary>
    public const string CommandFailed = "UKC102";

    /// <summary>响应缺失、超长或不是预期的格式。</summary>
    public const string ProtocolError = "UKC103";
}

/// <summary>
/// 指令通道失败。带 <c>UKC*</c> 诊断码，与设备层异常隔离——
/// Core 的通道实现不应依赖具体平台的设备服务。
/// </summary>
public sealed class CommandTransportException : Exception
{
    public CommandTransportException(
        string code,
        string message,
        ProcessExecutionResult result,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Result = result;
    }

    /// <summary><c>UKC*</c> 诊断码，取值见 <see cref="CommandChannelDiagnosticCodes"/>。</summary>
    public string Code { get; }

    public ProcessExecutionResult Result { get; }
}

/// <summary>
/// 指令通道配置：所有平台统一走 Web Remote Control HTTP，连接参数从工程配置取。
///
/// 平台与通道不再有对应关系——Android 与 Win64 都走同一条 HTTP 通道，
/// 差异只在引擎侧是否把 Android 加入 <c>PlatformAllowList</c>（属用户改引擎的职责）。
/// </summary>
public sealed record CommandChannelOptions(RemoteControlOptions RemoteControl)
{
    public static CommandChannelOptions Default { get; } = new(RemoteControlOptions.Default);

    public static CommandChannelOptions FromProjectSettings(ProjectSettings? settings) =>
        new(RemoteControlOptions.FromProjectSettings(settings));

    /// <summary>
    /// 构造通道实例。所有平台共用，无需平台分支。
    /// </summary>
    public ICommandTransport CreateTransport(IRemoteControlService? remoteControlService = null) =>
        new HttpCommandTransport(RemoteControl, remoteControlService);
}
