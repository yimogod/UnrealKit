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
    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;

    public RemoteControlService(HttpClient? httpClient = null, TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            var failureResult = new ProcessExecutionResult(
                -1,
                string.Empty,
                exception.Message,
                startedAt,
                _timeProvider.GetUtcNow());
            throw new RemoteControlException($"Remote Control request failed: {exception.Message}", failureResult, exception);
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