using UnrealKit.Core.Devices;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;
using UnrealKit.Core.Projects;

namespace UnrealKit.Core.Launch;

/// <summary>
/// 启动参数（uecommandline.txt）的构建与投放。
///
/// 平台差异只体现在「路径长什么样」上（Android 是 Unix 绝对路径，Win64 是本机路径）；
/// 写文件、删文件、启动应用一律委托 IDeviceService，不在此处按平台分支重复实现——
/// 那会让 Win64DeviceService 已有的实现被绕过，同一逻辑存在两份。
/// </summary>
public sealed class LaunchParameterService : ILaunchParameterService
{
    private const string FileName = "uecommandline.txt";

    private readonly IDeviceService _deviceService;

    public LaunchParameterService(IDeviceService deviceService)
    {
        _deviceService = deviceService ?? throw new ArgumentNullException(nameof(deviceService));
    }

    public string BuildContent(ProjectSettings settings, IReadOnlyList<string> presetNames, string? customArguments = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(presetNames);
        var selectedNames = presetNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var presets = selectedNames.Select(name => settings.LaunchParameterPresets.FirstOrDefault(preset => string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown launch parameter preset: {name}", nameof(presetNames))).ToArray();
        var nonComposable = presets.Where(preset => !preset.IsComposable).ToArray();
        if (nonComposable.Length > 1 || nonComposable.Length == 1 && presets.Length > 1)
        {
            throw new ArgumentException("A non-composable launch parameter preset must be used alone.", nameof(presetNames));
        }

        return string.Join(Environment.NewLine, presets.Select(preset => preset.Arguments).Append(customArguments ?? string.Empty).Where(argument => !string.IsNullOrWhiteSpace(argument)).Select(argument => argument.Trim()));
    }

    public string GetRemotePath(ProjectSettings settings, string? remotePathOverride = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!string.IsNullOrWhiteSpace(remotePathOverride))
        {
            return ValidatePath(settings, remotePathOverride);
        }

        if (settings.Platform == TargetPlatform.Win64)
        {
            if (string.IsNullOrWhiteSpace(settings.Win64WorkingDirectory))
            {
                throw new InvalidOperationException(
                    "Win64 启动参数需要在工程配置中设置 Win64WorkingDirectory 以定位 uecommandline.txt。" +
                    "回退到当前工作目录会让 GUI 与 CLI 写到不同位置。");
            }

            return ValidatePath(settings, Path.Combine(settings.Win64WorkingDirectory, settings.UnrealProjectName, FileName));
        }

        var root = settings.DeviceGameRootTemplate
            .Replace("{PackageName}", settings.PackageName, StringComparison.Ordinal)
            .Replace("{UnrealProjectName}", settings.UnrealProjectName, StringComparison.Ordinal);
        return ValidatePath(settings, $"{root.TrimEnd('/')}/{FileName}");
    }

    public async Task<LaunchParameterPushResult> PushAsync(UkitProject project, LaunchParameterRequest request, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SerialNumber);
        var content = BuildContent(project.Settings, request.PresetNames, request.CustomArguments);
        var remotePath = GetRemotePath(project.Settings, request.RemotePathOverride);
        var device = ResolveDevice(project.Settings, request.SerialNumber);

        // 内容先落到本地临时文件，再交给设备服务投放。Win64 的「推送」就是复制，
        // Android 是 adb push——两者都由 IDeviceService.PushFileAsync 负责。
        var directory = Path.Combine(Path.GetTempPath(), "UnrealKit", "LaunchParameters");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"{Guid.NewGuid():N}-{FileName}");
        try
        {
            progress?.Report(new OperationProgress("commandline-push", "Writing", 1, 2, $"Writing temporary {FileName}."));
            await File.WriteAllTextAsync(temporaryPath, content, new System.Text.UTF8Encoding(false), cancellationToken);
            progress?.Report(new OperationProgress("commandline-push", "Pushing", 2, 2, $"Pushing to {remotePath}."));
            var result = await _deviceService.PushFileAsync(device, temporaryPath, remotePath, progress, cancellationToken);
            return new LaunchParameterPushResult(content, remotePath, result);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task<ProcessExecutionResult> DeleteAsync(UkitProject project, string serialNumber, string? remotePathOverride = null, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(serialNumber);
        var path = GetRemotePath(project.Settings, remotePathOverride);
        return _deviceService.DeleteRemoteFileAsync(ResolveDevice(project.Settings, serialNumber), path, progress, cancellationToken);
    }

    public Task<ProcessExecutionResult> StartApplicationAsync(UkitProject project, string serialNumber, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(serialNumber);

        var settings = project.Settings;
        var device = ResolveDevice(settings, serialNumber);

        // 启动目标在 Android 上是包名 + Activity，在 Win64 上是可执行文件路径。
        var (target, activity) = settings.Platform == TargetPlatform.Win64
            ? (!string.IsNullOrWhiteSpace(settings.Win64Executable)
                ? settings.Win64Executable
                : throw new InvalidOperationException("Win64Executable is not configured in project settings."), null)
            : (settings.PackageName, settings.Activity);

        return _deviceService.StartApplicationAsync(device, target, activity, progress, cancellationToken);
    }

    /// <summary>
    /// 校验启动参数路径。Android 要求 Unix 绝对路径；Win64 要求绝对本机路径——
    /// 相对路径会按当前进程工作目录解析，GUI 与 CLI 下指向不同位置。
    /// </summary>
    private static string ValidatePath(ProjectSettings settings, string path)
    {
        if (settings.Platform == TargetPlatform.Win64)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Launch parameter path must not be empty.", nameof(path));
            }

            if (!Path.IsPathFullyQualified(path))
            {
                throw new ArgumentException($"Win64 launch parameter path must be absolute: {path}", nameof(path));
            }

            return Path.GetFullPath(path);
        }

        if (!path.StartsWith("/", StringComparison.Ordinal) || path.Contains('\\') || path.Contains('\0'))
        {
            throw new ArgumentException("Launch parameter remote path must be an absolute Unix path.", nameof(path));
        }

        return path;
    }

    private static IDevice ResolveDevice(ProjectSettings settings, string serialNumber) =>
        DeviceReference.Create(serialNumber, settings.Platform);
}
