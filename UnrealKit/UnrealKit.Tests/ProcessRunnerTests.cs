using UnrealKit.Core.Processes;

namespace UnrealKit.Tests;

public sealed class ProcessRunnerTests
{
    private static readonly string CommandProcessorPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Fact]
    public async Task RunAsync_CapturesOutputAndNonZeroExitCode()
    {
        var runner = new ProcessRunner();
        var request = new ProcessExecutionRequest(
            CommandProcessorPath,
            ["/d", "/c", "echo standard-output & echo standard-error 1>&2 & exit /b 7"]);

        var result = await runner.RunAsync(request);

        Assert.Equal(7, result.ExitCode);
        Assert.False(result.Succeeded);
        Assert.Contains("standard-output", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("standard-error", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ThrowsTimeoutException_WhenProcessExceedsTimeout()
    {
        var runner = new ProcessRunner();
        var request = new ProcessExecutionRequest(
            CommandProcessorPath,
            ["/d", "/c", "ping -n 6 127.0.0.1 > nul"],
            Timeout: TimeSpan.FromMilliseconds(100));

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync(request));

        Assert.Contains("超时", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ObservesCancellationToken()
    {
        var runner = new ProcessRunner();
        var request = new ProcessExecutionRequest(
            CommandProcessorPath,
            ["/d", "/c", "ping -n 6 127.0.0.1 > nul"]);
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(request, cancellationToken: cancellationSource.Token));
    }
}
