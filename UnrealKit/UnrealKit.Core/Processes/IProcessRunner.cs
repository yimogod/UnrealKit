namespace UnrealKit.Core.Processes;

public interface IProcessRunner
{
    Task<ProcessExecutionResult> RunAsync(
        ProcessExecutionRequest request,
        IProgress<Operations.OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
