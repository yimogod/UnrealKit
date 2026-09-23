namespace UnrealKit.Core.Processes;

public interface IProcessRunner
{
    Task<ProcessExecutionResult> RunAsync(
        ProcessExecutionRequest request,
        IProgress<Operations.OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 启动一个不等待其退出的进程（如长期运行的 GUI 客户端）。
    /// 只做 <c>Process.Start()</c> 与「确实启动成功」的确认，不重定向 stdout/stderr、
    /// 不设超时、不等待退出——返回值代表「已发出启动请求」，不代表进程已结束。
    /// <see cref="ProcessExecutionRequest.Timeout"/> 与 <see cref="ProcessExecutionRequest.Output"/>
    /// 对本方法无意义，会被忽略。
    /// </summary>
    Task<ProcessExecutionResult> StartDetachedAsync(
        ProcessExecutionRequest request,
        IProgress<Operations.OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
