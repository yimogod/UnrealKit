using UnrealKit.Core.Devices;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;
using UnrealKit.Core.Projects;

namespace UnrealKit.Core.Launch;

public sealed class LaunchParameterService : ILaunchParameterService
{
    private readonly IDeviceService _deviceService;

    public LaunchParameterService(IDeviceService deviceService)
    {
        _deviceService = deviceService ?? throw new ArgumentNullException(nameof(deviceService));
    }

    private const string FileName = "uecommandline.txt";

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
            var workingDir = !string.IsNullOrWhiteSpace(settings.Win64WorkingDirectory)
                ? settings.Win64WorkingDirectory
                : ".";
            return ValidatePath(settings, Path.Combine(workingDir, settings.UnrealProjectName, FileName));
        }

        // Android
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

        if (project.Settings.Platform == TargetPlatform.Win64)
        {
            // Win64: 直接写入本地文件
            progress?.Report(new OperationProgress("commandline-push", "Writing", 1, 1, $"Writing {FileName} to {remotePath}."));
            var dir = Path.GetDirectoryName(remotePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(remotePath, content, new System.Text.UTF8Encoding(false), cancellationToken);
            return new LaunchParameterPushResult(content, remotePath, new ProcessExecutionResult(0, string.Empty, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }

        // Android: ADB push
        var directory = Path.Combine(Path.GetTempPath(), "UnrealKit", "LaunchParameters");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"{Guid.NewGuid():N}-{FileName}");
        try
        {
            progress?.Report(new OperationProgress("commandline-push", "Writing", 1, 2, "Writing temporary uecommandline.txt."));
            await File.WriteAllTextAsync(temporaryPath, content, new System.Text.UTF8Encoding(false), cancellationToken);
            progress?.Report(new OperationProgress("commandline-push", "Pushing", 2, 2, $"Pushing to {remotePath}."));
            var adbDevice = new AdbDeviceWrapper(request.SerialNumber);
            var result = await _deviceService.PushFileAsync(adbDevice, temporaryPath, remotePath, progress, cancellationToken);
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

        if (project.Settings.Platform == TargetPlatform.Win64)
        {
            if (File.Exists(path)) File.Delete(path);
            return Task.FromResult(new ProcessExecutionResult(0, string.Empty, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }

        return _deviceService.DeleteRemoteFileAsync(new AdbDeviceWrapper(serialNumber), path, progress, cancellationToken);
    }

    public Task<ProcessExecutionResult> StartApplicationAsync(UkitProject project, string serialNumber, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(serialNumber);

        if (project.Settings.Platform == TargetPlatform.Win64)
        {
            var exePath = !string.IsNullOrWhiteSpace(project.Settings.Win64Executable)
                ? project.Settings.Win64Executable
                : throw new InvalidOperationException("Win64Executable is not configured in project settings.");
            var workingDir = !string.IsNullOrWhiteSpace(project.Settings.Win64WorkingDirectory)
                ? project.Settings.Win64WorkingDirectory
                : Path.GetDirectoryName(exePath) ?? ".";
            var runner = new ProcessRunner();
            return runner.RunAsync(new ProcessExecutionRequest(exePath, Array.Empty<string>(), workingDir), progress, cancellationToken);
        }

        return _deviceService.StartApplicationAsync(new AdbDeviceWrapper(serialNumber), project.Settings.PackageName, project.Settings.Activity, progress, cancellationToken);
    }

    private static string ValidatePath(ProjectSettings settings, string path)
    {
        if (settings.Platform == TargetPlatform.Win64)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Launch parameter path must not be empty.", nameof(path));
            return path;
        }

        // Android
        if (!path.StartsWith("/", StringComparison.Ordinal) || path.Contains('\\') || path.Contains('\0'))
        {
            throw new ArgumentException("Launch parameter remote path must be an absolute Unix path.", nameof(path));
        }

        return path;
    }

    /// <summary>
    /// 从 serial number 构造最小 IDevice 实现，仅用于 ADB 调用。
    /// </summary>
    private sealed class AdbDeviceWrapper : IDevice
    {
        public AdbDeviceWrapper(string id) { Id = id; Name = id; }
        public string Id { get; }
        public string Name { get; }
        public string Platform => "Android";
        public bool IsAvailable => true;
    }
}