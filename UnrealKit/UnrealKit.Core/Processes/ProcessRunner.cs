using System.ComponentModel;
using System.Diagnostics;
using System.Text;
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
        Report(progress, operationId, "Starting", $"正在启动外部进程: {FormatCommandLine(request)}");
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

        var standardOutputTask = ReadStreamAsync(process.StandardOutput, ProcessOutputStream.StandardOutput, request.Output);
        var standardErrorTask = ReadStreamAsync(process.StandardError, ProcessOutputStream.StandardError, request.Output);
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
            throw new ProcessExecutionTimeoutException(message, result);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            var result = await CreateResultAsync(process, standardOutputTask, standardErrorTask, startedAt);
            Report(progress, operationId, "Canceled", "外部进程已取消。");
            Log(LogLevel.Warning, operationId, "External process canceled", request, result: result);
            throw new ProcessExecutionCanceledException(result, cancellationToken);
        }
    }

    /// <summary>
    /// 启动即返回，不等待退出。同步完成 <c>Process.Start()</c> 后立即包一层
    /// <see cref="Task.FromResult{TResult}"/> 返回——内部没有任何 await，因此天然不会等子进程退出，
    /// 也不受调用方传入的 <see cref="CancellationToken"/> 影响（没有可取消的等待）。
    ///
    /// 不能复用 <see cref="CreateStartInfo"/>：那里硬编码了
    /// <c>RedirectStandardOutput/Error = true</c>，若照搬却不去读这两个流，
    /// 子进程输出一多就会把系统管道缓冲区堵满而卡死——对游戏客户端这种会自己打日志的
    /// 长期运行进程是致命的。这里单独构造不重定向输出的 <see cref="ProcessStartInfo"/>。
    /// </summary>
    public Task<ProcessExecutionResult> StartDetachedAsync(
        ProcessExecutionRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        cancellationToken.ThrowIfCancellationRequested();

        var operationId = $"process-{Guid.NewGuid():N}";
        var startInfo = CreateDetachedStartInfo(request);
        var startedAt = DateTimeOffset.UtcNow;
        Report(progress, operationId, "Starting", $"正在启动外部进程: {FormatCommandLine(request)}");
        Log(LogLevel.Information, operationId, "Starting external process (detached)", request);

        var process = new Process { StartInfo = startInfo };
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
        finally
        {
            // 不持有 process 句柄等待退出，这里的 Process 对象生命周期到此结束即可释放；
            // 不用 using——using 会在方法返回前 Dispose，但 Dispose 不会杀掉已启动的子进程，
            // 只是释放 .NET 侧的句柄包装，对「启动后脱离管理」的语义无影响，显式 Dispose 更清楚。
            process.Dispose();
        }

        var result = new ProcessExecutionResult(0, string.Empty, string.Empty, startedAt, DateTimeOffset.UtcNow);
        Report(progress, operationId, "Launched", $"外部进程已启动: {request.FileName}");
        Log(LogLevel.Information, operationId, "External process launched (detached)", request, result: result);
        return Task.FromResult(result);
    }

    private static ProcessStartInfo CreateDetachedStartInfo(ProcessExecutionRequest request)
    {
        var startInfo = new ProcessStartInfo(request.FileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            CreateNoWindow = false
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

    private static async Task<string> ReadStreamAsync(StreamReader reader, ProcessOutputStream stream, IProgress<ProcessOutput>? output)
    {
        var content = new StringBuilder();
        while (await reader.ReadLineAsync() is { } line)
        {
            content.AppendLine(line);
            output?.Report(new ProcessOutput(DateTimeOffset.UtcNow, stream, line));
        }

        return content.ToString();
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

    /// <summary>
    /// 拼接可读的命令行，仅用于日志呈现，不参与 shell 执行（真正执行走
    /// <see cref="ProcessStartInfo.ArgumentList"/> 的参数化调用）。参数含空格时加引号，
    /// 使展示串与实际执行的命令在语义上一致。
    /// </summary>
    private static string FormatCommandLine(ProcessExecutionRequest request) =>
        request.Arguments.Count == 0
            ? request.FileName
            : $"{request.FileName} {string.Join(' ', request.Arguments.Select(QuoteForDisplay))}";

    private static string QuoteForDisplay(string argument) =>
        argument.Any(char.IsWhiteSpace) || argument.Length == 0
            ? $"\"{argument}\""
            : argument;

    private void Log(LogLevel level, string operationId, string message, ProcessExecutionRequest request, Exception? exception = null, ProcessExecutionResult? result = null)
    {
        var properties = new Dictionary<string, string>
        {
            ["fileName"] = request.FileName,
            ["commandLine"] = FormatCommandLine(request),
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
