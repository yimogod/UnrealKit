using System.Text;
using System.Text.Json;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;

namespace UnrealKit.Core.RemoteControl;

/// <summary>
/// 默认 <see cref="IRemoteControlService"/> 实现，调用 UE Web Remote Control API：
/// PUT http://127.0.0.1:{port}/remote/object/call
/// </summary>
public sealed class RemoteControlService : IRemoteControlService
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 共享默认客户端：调用方每次操作都可能新建设备服务（GUI 每次发送指令都会），
    /// 每个实例各自持有 HttpClient 会泄漏连接池并耗尽端口。
    /// </summary>
    private static readonly HttpClient SharedHttpClient = new() { Timeout = DefaultTimeout };

    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;

    public RemoteControlService(HttpClient? httpClient = null, TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ProcessExecutionResult> SendConsoleCommandAsync(
        RemoteControlCommandRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        progress?.Report(new OperationProgress(
            "remote-control-send",
            "Sending",
            null,
            null,
            $"Sending console command via Remote Control: {request.Command}"));

        var startedAt = _timeProvider.GetUtcNow();
        var uri = BuildUri(request.HttpPort);
        using var content = new StringContent(
            JsonSerializer.Serialize(BuildPayload(request)),
            Encoding.UTF8,
            "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PutAsync(uri, content, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 调用方主动取消：保持取消语义上抛。
            throw;
        }
        catch (OperationCanceledException exception)
        {
            // HttpClient 超时抛的是 TaskCanceledException（派生自 OperationCanceledException）而非
            // TimeoutException，若原样上抛会绕过 CLI 的可预期失败处理，变成裸堆栈。
            throw BuildFailure(
                $"Remote Control request timed out after {_httpClient.Timeout.TotalSeconds:F0}s: {uri}. "
                    + "请确认 UE 已启动且 Web Remote Control 插件已启用。",
                exception,
                startedAt);
        }
        catch (HttpRequestException exception)
        {
            // HttpRequestException.Message 几乎总是「An error occurred while sending the request.」，
            // 真正的原因（连接被拒绝、DNS 失败等）在 InnerException 里，必须透传才能定位。
            var detail = exception.InnerException?.Message ?? exception.Message;
            throw BuildFailure(
                $"Remote Control request failed: {detail}. "
                    + "请确认 UE 已启动且 Web Remote Control 插件已启用。",
                exception,
                startedAt);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var completedAt = _timeProvider.GetUtcNow();
            var result = new ProcessExecutionResult(
                response.IsSuccessStatusCode ? 0 : (int)response.StatusCode,
                body,
                response.IsSuccessStatusCode
                    ? string.Empty
                    : $"Remote Control HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
                startedAt,
                completedAt);

            if (!response.IsSuccessStatusCode)
            {
                throw new RemoteControlException(
                    $"Remote Control returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {body}",
                    result);
            }

            return result;
        }
    }

    private RemoteControlException BuildFailure(string message, Exception exception, DateTimeOffset startedAt) =>
        new(
            message,
            new ProcessExecutionResult(-1, string.Empty, exception.Message, startedAt, _timeProvider.GetUtcNow()),
            exception);

    private static object BuildPayload(RemoteControlCommandRequest request) => new
    {
        objectPath = request.ObjectPath,
        functionName = request.FunctionName,
        parameters = new Dictionary<string, string>
        {
            [request.CommandParameterName] = request.Command
        },
        generateTransaction = true
    };

    private static Uri BuildUri(int httpPort) =>
        new($"http://127.0.0.1:{httpPort}/remote/object/call");

    private static void ValidateRequest(RemoteControlCommandRequest request)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.HttpPort);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(request.HttpPort, 65535);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ObjectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FunctionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CommandParameterName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Command);
    }
}

/// <summary>
/// Remote Control HTTP 调用失败。与设备层异常隔离，避免 Core 依赖具体平台通道。
/// </summary>
public sealed class RemoteControlException : Exception
{
    public RemoteControlException(string message, ProcessExecutionResult result, Exception? innerException = null)
        : base(message, innerException)
    {
        Result = result;
    }

    public ProcessExecutionResult Result { get; }
}
