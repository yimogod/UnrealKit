using UnrealKit.Core.Adb;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Projects;
using UnrealKit.Core.Processes;

namespace UnrealKit.Core.Launch;

public sealed class LaunchParameterService(IAdbService adbService) : ILaunchParameterService
{
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
            return ValidateRemotePath(remotePathOverride);
        }

        var root = settings.DeviceGameRootTemplate.Replace("{PackageName}", settings.PackageName, StringComparison.Ordinal).Replace("{UnrealProjectName}", settings.UnrealProjectName, StringComparison.Ordinal);
        return ValidateRemotePath($"{root.TrimEnd('/')}/{FileName}");
    }

    public async Task<LaunchParameterPushResult> PushAsync(UkitProject project, LaunchParameterRequest request, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SerialNumber);
        var content = BuildContent(project.Settings, request.PresetNames, request.CustomArguments);
        var remotePath = GetRemotePath(project.Settings, request.RemotePathOverride);
        var directory = Path.Combine(Path.GetTempPath(), "UnrealKit", "LaunchParameters");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"{Guid.NewGuid():N}-{FileName}");
        try
        {
            progress?.Report(new OperationProgress("commandline-push", "Writing", 1, 2, "Writing temporary uecommandline.txt."));
            await File.WriteAllTextAsync(temporaryPath, content, new System.Text.UTF8Encoding(false), cancellationToken);
            progress?.Report(new OperationProgress("commandline-push", "Pushing", 2, 2, $"Pushing to {remotePath}."));
            var result = await adbService.PushFileAsync(request.SerialNumber, temporaryPath, remotePath, progress, cancellationToken);
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
        return adbService.DeleteRemoteFileAsync(serialNumber, GetRemotePath(project.Settings, remotePathOverride), progress, cancellationToken);
    }

    public Task<ProcessExecutionResult> StartApplicationAsync(UkitProject project, string serialNumber, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(serialNumber);
        return adbService.StartApplicationAsync(serialNumber, project.Settings.PackageName, project.Settings.Activity, progress, cancellationToken);
    }

    private static string ValidateRemotePath(string remotePath)
    {
        if (!remotePath.StartsWith("/", StringComparison.Ordinal) || remotePath.Contains('\\') || remotePath.Contains('\0'))
        {
            throw new ArgumentException("Launch parameter remote path must be an absolute Unix path.", nameof(remotePath));
        }

        return remotePath;
    }
}
