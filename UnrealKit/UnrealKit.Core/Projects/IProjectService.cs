using UnrealKit.Core.Operations;

namespace UnrealKit.Core.Projects;

public interface IProjectService
{
    Task<ProjectCreateResult> CreateProjectAsync(
        CreateProjectRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<UkitProject> OpenProjectAsync(
        string projectFilePath,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<UkitProject> UpdateSettingsAsync(
        UkitProject project,
        ProjectSettings settings,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<ProjectValidationResult> ValidateProjectAsync(
        string projectFilePath,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
