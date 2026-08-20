using UnrealKit.Core.Operations;
using UnrealKit.Core.Projects;
using UnrealKit.Core.Processes;

namespace UnrealKit.Core.Launch;

public interface ILaunchParameterService
{
    string BuildContent(ProjectSettings settings, IReadOnlyList<string> presetNames, string? customArguments = null);
    string GetRemotePath(ProjectSettings settings);
    Task<LaunchParameterPushResult> PushAsync(UkitProject project, LaunchParameterRequest request, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<ProcessExecutionResult> DeleteAsync(UkitProject project, string serialNumber, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<ProcessExecutionResult> StartApplicationAsync(UkitProject project, string serialNumber, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default);
}
