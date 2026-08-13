using UnrealKit.Core.Processes;
using System.Collections.Concurrent;

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

        var exception = await Assert.ThrowsAnyAsync<TimeoutException>(() => runner.RunAsync(request));

        Assert.Contains("超时", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ObservesCancellationToken()
    {
        var runner = new ProcessRunner();
        var request = new ProcessExecutionRequest(
            CommandProcessorPath,
            ["/d", "/c", "echo before-cancel & echo before-cancel-error 1>&2 & ping -n 6 127.0.0.1 > nul"]);
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var exception = await Assert.ThrowsAsync<ProcessExecutionCanceledException>(() => runner.RunAsync(request, cancellationToken: cancellationSource.Token));

        Assert.NotNull(exception.Result);
        Assert.Contains("before-cancel", exception.Result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("before-cancel-error", exception.Result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_StreamsInterleavedOutputAndPreservesAggregateOutput()
    {
        var output = new ConcurrentQueue<ProcessOutput>();
        var runner = new ProcessRunner();
        var request = new ProcessExecutionRequest(
            CommandProcessorPath,
            ["/d", "/c", "echo first-out & echo first-error 1>&2 & ping -n 2 127.0.0.1 > nul & echo second-out & echo second-error 1>&2"],
            Output: new InlineProgress<ProcessOutput>(output.Enqueue));

        var result = await runner.RunAsync(request);

        Assert.Contains(output, item => item.Stream == ProcessOutputStream.StandardOutput && item.Text.Trim() == "first-out");
        Assert.Contains(output, item => item.Stream == ProcessOutputStream.StandardError && item.Text.Trim() == "first-error");
        Assert.Contains(output, item => item.Stream == ProcessOutputStream.StandardOutput && item.Text.Trim() == "second-out");
        Assert.Contains(output, item => item.Stream == ProcessOutputStream.StandardError && item.Text.Trim() == "second-error");
        Assert.Contains("first-out", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("second-error", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_PreservesReceivedOutputWhenTimedOut()
    {
        var runner = new ProcessRunner();
        var request = new ProcessExecutionRequest(
            CommandProcessorPath,
            ["/d", "/c", "echo before-timeout & echo before-timeout-error 1>&2 & ping -n 6 127.0.0.1 > nul"],
            Timeout: TimeSpan.FromMilliseconds(150));

        var exception = await Assert.ThrowsAsync<ProcessExecutionTimeoutException>(() => runner.RunAsync(request));

        Assert.Contains("before-timeout", exception.Result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("before-timeout-error", exception.Result.StandardError, StringComparison.Ordinal);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
