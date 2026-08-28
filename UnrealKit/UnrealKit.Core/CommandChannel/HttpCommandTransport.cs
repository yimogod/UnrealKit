using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;
using UnrealKit.Core.RemoteControl;

namespace UnrealKit.Core.CommandChannel;

/// <summary>
/// 走引擎自带 Web Remote Control 的指令通道，服务 Android 与 Win64。
///
/// 是既有 <see cref="RemoteControlService"/> 的薄适配：把 <see cref="RemoteControlException"/>
/// 归一到带 <c>UKC*</c> 码的 <see cref="CommandTransportException"/>，让上层只处理一种失败类型。
/// HTTP 的请求构造与错误文案仍留在 <see cref="RemoteControlService"/>，不在此处复制一份。
/// </summary>
public sealed class HttpCommandTransport : ICommandTransport
{
    private readonly RemoteControlOptions _options;
    private readonly IRemoteControlService _remoteControl;

    public HttpCommandTransport(
        RemoteControlOptions? options = null,
        IRemoteControlService? remoteControlService = null)
    {
        _options = options ?? RemoteControlOptions.Default;
        _remoteControl = remoteControlService ?? new RemoteControlService();
    }

    public CommandTransportKind Kind => CommandTransportKind.Http;

    public int Port => _options.HttpPort;

    public Task<ProcessExecutionResult> SendConsoleCommandAsync(
        string command,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        return TranslateFailuresAsync(() => _remoteControl.SendConsoleCommandAsync(
            new RemoteControlCommandRequest(
                _options.HttpPort,
                _options.ObjectPath,
                _options.FunctionName,
                _options.CommandParameterName,
                command),
            progress,
            cancellationToken));
    }

    /// <summary>
    /// 读回 cvar：同一 HTTP 端点、同一 objectPath，只是换成 UE 的
    /// <c>GetConsoleVariable*Value</c>。函数名不走 <see cref="RemoteControlOptions.FunctionName"/>
    /// 配置——那一项配的是「发指令用哪个函数」，读回的两个 getter 与之绑定的是
    /// <see cref="ConsoleVariableType"/>，混用会让改了发送函数名就读不到值。
    /// </summary>
    public Task<ProcessExecutionResult> QueryConsoleVariableAsync(
        string variableName,
        ConsoleVariableType variableType,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variableName);

        return TranslateFailuresAsync(() => _remoteControl.QueryConsoleVariableAsync(
            new RemoteControlVariableQueryRequest(
                _options.HttpPort,
                _options.ObjectPath,
                variableName,
                variableType == ConsoleVariableType.Bool
                    ? RemoteControlVariableType.Bool
                    : RemoteControlVariableType.Number),
            progress,
            cancellationToken));
    }

    private static async Task<ProcessExecutionResult> TranslateFailuresAsync(
        Func<Task<ProcessExecutionResult>> call)
    {
        try
        {
            return await call();
        }
        catch (RemoteControlException exception)
        {
            // 连接层失败（ExitCode -1，由 RemoteControlService 的 BuildFailure 产生）与
            // 「服务在但拒绝了这次调用」（HTTP 状态码）要分开：前者是通道没通，
            // 后者是调用本身的问题，两种情况的排查方向不同。
            var code = exception.Result.ExitCode < 0
                ? CommandChannelDiagnosticCodes.ConnectFailed
                : CommandChannelDiagnosticCodes.CommandFailed;
            throw new CommandTransportException(
                code,
                $"[{code}] {exception.Message}",
                exception.Result,
                exception);
        }
    }
}
