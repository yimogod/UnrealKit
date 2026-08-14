using UnrealKit.Core.Operations;

namespace UnrealKit.Core.Projects;

/// <summary>
/// 项目服务接口
/// </summary>
public interface IProjectService
{
    /// <summary>
    /// 创建项目
    /// </summary>
    Task<ProjectCreateResult> CreateProjectAsync(
        CreateProjectRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 打开项目
    /// </summary>
    Task<UkitProject> OpenProjectAsync(
        string projectFilePath,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新项目设置
    /// </summary>
    Task<UkitProject> UpdateSettingsAsync(
        UkitProject project,
        ProjectSettings settings,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 
    /// </summary>
    Task<ProjectValidationResult> ValidateProjectAsync(
        string projectFilePath,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
