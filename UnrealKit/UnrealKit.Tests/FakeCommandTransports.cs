using UnrealKit.Core.CommandChannel;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;
using UnrealKit.Core.RemoteControl;

namespace UnrealKit.Tests;

/// <summary>
/// 记录收到的命令并一律成功的通道替身。用于验证设备服务把命令交给了通道，
/// 而不必真的起一个监听端口。
/// </summary>
internal sealed class RecordingCommandTransport(
    CommandTransportKind kind = CommandTransportKind.Http,
    int port = RemoteControlOptions.DefaultHttpPort) : ICommandTransport
{
    public CommandTransportKind Kind => kind;

    public int Port => port;

    public List<string> Commands { get; } = [];

    public Task<ProcessExecutionResult> SendConsoleCommandAsync(
        string command,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Commands.Add(command);
        return Task.FromResult(new ProcessExecutionResult(
            0, "ok", string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }
}

/// <summary>
/// 一律以指定 <c>UKC*</c> 码失败的通道替身。
/// </summary>
internal sealed class FailingCommandTransport(
    string code,
    CommandTransportKind kind = CommandTransportKind.Http,
    int port = RemoteControlOptions.DefaultHttpPort) : ICommandTransport
{
    public CommandTransportKind Kind => kind;

    public int Port => port;

    public Task<ProcessExecutionResult> SendConsoleCommandAsync(
        string command,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        throw new CommandTransportException(
            code,
            $"[{code}] 通道替身按约定失败: {command}",
            new ProcessExecutionResult(-1, string.Empty, "fake failure", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
}
