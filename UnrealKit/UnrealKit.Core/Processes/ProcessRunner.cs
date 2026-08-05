using System.ComponentModel;
using System.Diagnostics;
using UnrealKit.Core.Operations;

namespace UnrealKit.Core.Processes;

public sealed class ProcessRunner : IProcessRunner
{
    private readonly IOperationLogger _logger;

    public ProcessRunner(IOperationLogger? logger = null)
    {
        _logger = logger ?? NullOperationLogger.Instance;
    }

    public async Task<ProcessExecutionResult> RunAsync(
        ProcessExecutionRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        var operationId = $"process-{Guid.NewGuid():N}";
        var startInfo = CreateStartInfo(request);
        var startedAt = DateTimeOffset.UtcNow;
        Report(progress, operationId, "Starting", $"正在启动外部进程: {request.FileName}");
        Log(LogLevel.Information, operationId, "Starting external process", request);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"无法启动外部进程: {request.FileName}");
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            Log(LogLevel.Error, operationId, "External process could not start", request, exception);
            throw new InvalidOperationException($"无法启动外部进程 '{request.FileName}': {exception.Message}", exception);
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutCancellationSource = new CancellationTokenSource(request.Timeout ?? ProcessExecutionRequest.DefaultTimeout);
        using var linkedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellationSource.Token);

        try
        {
            await process.WaitForExitAsync(linkedCancellationSource.Token);
            var standardOutput = await standardOutputTask;
            var standardError = await standardErrorTask;
            var result = new ProcessExecutionResult(process.ExitCode, standardOutput, standardError, startedAt, DateTimeOffset.UtcNow);
            Report(progress, operationId, "Completed", $"外部进程已结束，退出码: {result.ExitCode}");
            Log(result.Succeeded ? LogLevel.Information : LogLevel.Warning, operationId, "External process completed", request, result: result);
            return result;
        }
        catch (OperationCanceledException) when (timeoutCancellationSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            KillProcessTree(process);
            var result = await CreateResultAsync(process, standardOutputTask, standardErrorTask, startedAt);
            var message = $"外部进程在 {(request.Timeout ?? ProcessExecutionRequest.DefaultTimeout).TotalSeconds:0} 秒后超时: {request.FileName}";
            Report(progress, operationId, "TimedOut", message);
            Log(LogLevel.Error, operationId, message, request, result: result);
            throw new TimeoutException(message);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            Report(progress, operationId, "Canceled", "外部进程已取消。");
            Log(LogLevel.Warning, operationId, "External process canceled", request);
            throw;
        }
    }

    private static ProcessStartInfo CreateStartInfo(ProcessExecutionRequest request)
    {
        var startInfo = new ProcessStartInfo(request.FileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (request.EnvironmentVariables is not null)
        {
            foreach (var environmentVariable in request.EnvironmentVariables)
            {
                startInfo.Environment[environmentVariable.Key] = environmentVariable.Value;
            }
        }

        return startInfo;
    }

    private static async Task<ProcessExecutionResult> CreateResultAsync(Process process, Task<string> standardOutputTask, Task<string> standardErrorTask, DateTimeOffset startedAt)
    {
        var standardOutput = await ReadCompletedTaskAsync(standardOutputTask);
        var standardError = await ReadCompletedTaskAsync(standardErrorTask);
        var exitCode = process.HasExited ? process.ExitCode : -1;
        return new ProcessExecutionResult(exitCode, standardOutput, standardError, startedAt, DateTimeOffset.UtcNow);
    }

    private static async Task<string> ReadCompletedTaskAsync(Task<string> task)
    {
        try
        {
            return await task;
        }
        catch (OperationCanceledException)
        {
            return string.Empty;
        }
    }

    private static void KillProcessTree(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }

    private static void Report(IProgress<OperationProgress>? progress, string operationId, string stage, string message) =>
        progress?.Report(new OperationProgress(operationId, stage, null, null, message));

    private void Log(LogLevel level, string operationId, string message, ProcessExecutionRequest request, Exception? exception = null, ProcessExecutionResult? result = null)
    {
        var properties = new Dictionary<string, string>
        {
            ["fileName"] = request.FileName,
            ["argumentCount"] = request.Arguments.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (result is not null)
        {
            properties["exitCode"] = result.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
            properties["durationMilliseconds"] = result.Duration.TotalMilliseconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
        }

        _logger.Log(new LogEvent(DateTimeOffset.UtcNow, level, operationId, message, properties, exception));
    }
}
