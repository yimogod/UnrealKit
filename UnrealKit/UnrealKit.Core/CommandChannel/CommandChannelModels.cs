using UnrealKit.Core.Processes;
using UnrealKit.Core.Projects;
using UnrealKit.Core.RemoteControl;

namespace UnrealKit.Core.CommandChannel;

/// <summary>
/// 控制台指令通道的传输方式。
///
/// 取值是配置契约（<c>Config/DefaultGame.ini</c> 的 <c>*CommandTransport</c>），
/// 不从平台隐式推断：Android 之所以不能用 <see cref="Http"/>，是因为引擎的
/// <c>WebRemoteControl</c> 模块带 <c>PlatformAllowList</c>（只含 Mac/Win64/Linux），
/// Android 构建里没有 HTTP 服务器——这个理由属于工程实际用的引擎版本，
/// 应由配置明说，而不是写死在代码分支里。
/// </summary>
public enum CommandTransportKind
{
    /// <summary>引擎自带 Web Remote Control：<c>PUT /remote/object/call</c>。</summary>
    Http,

    /// <summary>UE 侧自研 TCP 命令插件：单行命令 + 单行 JSON 响应。</summary>
    Tcp
}

/// <summary>
/// <c>UKC</c> 域诊断码：控制台指令通道。向后追加，不复用已发布编号。
/// </summary>
public static class CommandChannelDiagnosticCodes
{
    /// <summary>连接被拒绝或超时（UE 未启动 / 插件未监听 / <c>adb forward</c> 未生效）。</summary>
    public const string ConnectFailed = "UKC101";

    /// <summary>命令执行失败（UE 返回 <c>ok=false</c>，或 HTTP 非成功状态）。</summary>
    public const string CommandFailed = "UKC102";

    /// <summary>响应缺失、超长或不是预期的 JSON。</summary>
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
/// 指令通道配置：每个平台走哪条通道，以及各通道的连接参数。
///
/// 平台与通道的对应关系是配置项而不是代码分支：同一份 UnrealKit 要面对
/// 「Android 装了 TCP 插件」「Android 用的是改过白名单的引擎 fork」等不同工程，
/// 由配置说明才不必为每种情况改代码。
/// </summary>
public sealed record CommandChannelOptions(
    int TcpPort,
    CommandTransportKind AndroidTransport,
    CommandTransportKind Win64Transport,
    RemoteControlOptions RemoteControl)
{
    /// <summary>
    /// UE 侧 TCP 命令插件的默认监听端口，同时是 <c>adb forward</c> 的两端端口。
    /// 与引擎既有服务（Remote Control 30010/30020、Unreal Insights 1980、
    /// Session Frontend 6666/6776）都不重叠。
    /// </summary>
    public const int DefaultTcpPort = 39010;

    /// <summary>
    /// Android 默认走 TCP，Win64 默认走 HTTP：Win64 用引擎自带的 Remote Control
    /// 即可，不必为它在 UE 侧再带一个插件。
    /// </summary>
    public static CommandChannelOptions Default { get; } = new(
        DefaultTcpPort,
        CommandTransportKind.Tcp,
        CommandTransportKind.Http,
        RemoteControlOptions.Default);

    public static CommandChannelOptions FromProjectSettings(ProjectSettings? settings)
    {
        if (settings is null)
        {
            return Default;
        }

        return new CommandChannelOptions(
            settings.CommandTcpPort,
            settings.AndroidCommandTransport,
            settings.Win64CommandTransport,
            RemoteControlOptions.FromProjectSettings(settings));
    }

    /// <summary>该平台配置的传输方式。</summary>
    public CommandTransportKind TransportFor(TargetPlatform platform) => platform switch
    {
        TargetPlatform.Android => AndroidTransport,
        TargetPlatform.Win64 => Win64Transport,
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "该平台尚未声明指令通道。")
    };

    /// <summary>
    /// 构造该平台的通道实例。
    /// </summary>
    /// <param name="platform">目标平台，决定用哪条通道。</param>
    /// <param name="remoteControlService">HTTP 通道使用的 Remote Control 客户端，仅用于测试注入。</param>
    public ICommandTransport CreateTransport(
        TargetPlatform platform,
        IRemoteControlService? remoteControlService = null) => TransportFor(platform) switch
    {
        CommandTransportKind.Http => new HttpCommandTransport(RemoteControl, remoteControlService),
        CommandTransportKind.Tcp => new TcpCommandTransport(TcpPort),
        var kind => throw new ArgumentOutOfRangeException(
            nameof(platform), kind, $"未实现的指令通道传输方式: {kind}。")
    };
}
