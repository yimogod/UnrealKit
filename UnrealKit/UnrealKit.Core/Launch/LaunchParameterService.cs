using UnrealKit.Core.Devices;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;
using UnrealKit.Core.Projects;

namespace UnrealKit.Core.Launch;

/// <summary>
/// 启动参数（uecommandline.txt）的构建与投放。
///
/// 此类不含任何平台分支：路径与启动目标由 <see cref="PlatformTarget"/> 提供，
/// 写文件、删文件、启动应用一律委托 IDeviceService。目标平台由传入的设备决定，
/// 不来自工程配置——同一工程可以同时跑多个平台。
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

    /// <summary>
    /// 启动参数文件在设备上的路径。平台取自本服务所绑定的设备服务，
    /// 因此同一工程针对不同平台的设备会解析出各自正确的路径。
    /// </summary>
    public string GetRemotePath(ProjectSettings settings, string? remotePathOverride = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var target = ResolveTarget(settings);
        return string.IsNullOrWhiteSpace(remotePathOverride)
            ? target.CombineDevicePath(target.GameRootPath, FileName)
            : ValidateOverridePath(target, remotePathOverride);
    }

    public async Task<LaunchParameterPushResult> PushAsync(UkitProject project, LaunchParameterRequest request, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SerialNumber);
        var content = BuildContent(project.Settings, request.PresetNames, request.CustomArguments);
        var remotePath = GetRemotePath(project.Settings, request.RemotePathOverride);
        var device = ResolveDevice(request.SerialNumber);

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
        return _deviceService.DeleteRemoteFileAsync(ResolveDevice(serialNumber), path, progress, cancellationToken);
    }

    public Task<ProcessExecutionResult> StartApplicationAsync(UkitProject project, string serialNumber, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(serialNumber);

        var target = ResolveTarget(project.Settings);
        return _deviceService.StartApplicationAsync(
            ResolveDevice(serialNumber), target.LaunchTarget, target.LaunchActivity, progress, cancellationToken);
    }

    /// <summary>
    /// 解析本服务所绑定平台的落地值。该平台在工程中未配置时报错并列出已配置平台。
    /// </summary>
    private PlatformTarget ResolveTarget(ProjectSettings settings) =>
        settings.ResolveTarget(_deviceService.Platform, "投放启动参数需要该平台的配置。");

    /// <summary>
    /// 校验调用方显式指定的路径。覆盖值绕过了模板展开，因此必须在此确认它符合
    /// 目标平台的路径风格——相对路径会按当前进程工作目录解析，GUI 与 CLI 下指向不同位置。
    /// </summary>
    private static string ValidateOverridePath(PlatformTarget target, string path) => target.PathStyle switch
    {
        DevicePathStyle.Unix when !path.StartsWith('/') || path.Contains('\\') || path.Contains('\0') =>
            throw new ArgumentException($"{target.PlatformName} 启动参数路径必须是绝对 Unix 路径: {path}", nameof(path)),
        DevicePathStyle.Unix => path,
        DevicePathStyle.Windows when !Path.IsPathFullyQualified(path) =>
            throw new ArgumentException($"{target.PlatformName} 启动参数路径必须是绝对路径: {path}", nameof(path)),
        DevicePathStyle.Windows => Path.GetFullPath(path),
        _ => throw new ArgumentOutOfRangeException(nameof(target), target.PathStyle, "Unsupported device path style.")
    };

    private IDevice ResolveDevice(string serialNumber) =>
        DeviceReference.Create(serialNumber, _deviceService.Platform);
}
