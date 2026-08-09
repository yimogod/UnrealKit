namespace UnrealKit.Core.RenderDoc;

public interface IRenderDocService
{
    /// <summary>
    /// Executes a RenderDoc Python script with the given request parameters.
    /// The output directory will be created if it does not exist.
    /// </summary>
    Task<RenderDocExecutionResult> ExecuteAsync(
        RenderDocExecutionRequest request,
        CancellationToken cancellationToken = default);
}