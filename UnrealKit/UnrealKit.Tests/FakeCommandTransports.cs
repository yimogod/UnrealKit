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

    public int ForwardPort => port;

    public List<string> Commands { get; } = [];

    /// <summary>收到的 cvar 读回请求，按顺序记录。</summary>
    public List<(string VariableName, ConsoleVariableType VariableType)> Queries { get; } = [];
    public List<string> ActorVisibilityQueries { get; } = [];
    public List<(string ActorObjectPath, bool Hidden)> ActorVisibilitySets { get; } = [];

    /// <summary>读回时返回的响应 body。默认是 Remote Control 对数值 cvar 的回包形状。</summary>
    public string QueryResponseBody { get; set; } = """{"ReturnValue":80.0}""";

    public Task<ProcessExecutionResult> SendConsoleCommandAsync(
        string command,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Commands.Add(command);
        return Task.FromResult(new ProcessExecutionResult(
            0, "ok", string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }

    public Task<ProcessExecutionResult> QueryConsoleVariableAsync(
        string variableName,
        ConsoleVariableType variableType,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Queries.Add((variableName, variableType));
        return Task.FromResult(new ProcessExecutionResult(
            0, QueryResponseBody, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }

    public Task<ProcessExecutionResult> QueryActorHiddenInGameAsync(
        string actorObjectPath,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ActorVisibilityQueries.Add(actorObjectPath);
        return Task.FromResult(new ProcessExecutionResult(
            0, """{"ReturnValue":false}""", string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }

    public Task<ProcessExecutionResult> SetActorHiddenInGameAsync(
        string actorObjectPath,
        bool hidden,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ActorVisibilitySets.Add((actorObjectPath, hidden));
        return Task.FromResult(new ProcessExecutionResult(
            0, string.Empty, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
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

    public int ForwardPort => port;

    public Task<ProcessExecutionResult> SendConsoleCommandAsync(
        string command,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        throw Failure(command);

    public Task<ProcessExecutionResult> QueryConsoleVariableAsync(
        string variableName,
        ConsoleVariableType variableType,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        throw Failure(variableName);

    public Task<ProcessExecutionResult> QueryActorHiddenInGameAsync(
        string actorObjectPath,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        throw Failure(actorObjectPath);

    public Task<ProcessExecutionResult> SetActorHiddenInGameAsync(
        string actorObjectPath,
        bool hidden,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        throw Failure(actorObjectPath);

    private CommandTransportException Failure(string subject) =>
        new(code,
            $"[{code}] 通道替身按约定失败: {subject}",
            new ProcessExecutionResult(-1, string.Empty, "fake failure", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
}
