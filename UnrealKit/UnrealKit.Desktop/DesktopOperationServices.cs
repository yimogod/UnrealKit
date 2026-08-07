using System.Windows;
using UnrealKit.Core.Adb;
using UnrealKit.Core.Processes;
using UnrealKit.Core.Projects;

namespace UnrealKit.Desktop;

public interface IDesktopAdbServiceFactory
{
    IAdbService Create(ProjectSettings? settings, IProgress<ProcessOutput>? output);
}

public sealed class DesktopAdbServiceFactory(AdbPathResolver? adbPathResolver = null) : IDesktopAdbServiceFactory
{
    private readonly AdbPathResolver _adbPathResolver = adbPathResolver ?? new AdbPathResolver();

    public IAdbService Create(ProjectSettings? settings, IProgress<ProcessOutput>? output)
    {
        var adbPath = _adbPathResolver.ResolveRequired(null, settings?.AdbPath);
        return new AdbService(new ProcessRunner(), adbPath, output);
    }
}

public sealed record LaunchOperationTarget(string SerialNumber, string PackageName, string Activity, string RemoteCommandLinePath);

public interface IUserConfirmationService
{
    Task<bool> ConfirmDeleteLaunchParametersAsync(LaunchOperationTarget target);
}

public sealed class WpfUserConfirmationService(Window owner) : IUserConfirmationService
{
    public Task<bool> ConfirmDeleteLaunchParametersAsync(LaunchOperationTarget target)
    {
        var message = $"The following remote file will be deleted:\n\n" +
                      $"Device: {target.SerialNumber}\n" +
                      $"Package: {target.PackageName}\n" +
                      $"Activity: {target.Activity}\n" +
                      $"Remote path: {target.RemoteCommandLinePath}\n\n" +
                      "This operation cannot be undone.";
        var result = MessageBox.Show(owner, message, "Confirm uecommandline.txt deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return Task.FromResult(result == MessageBoxResult.Yes);
    }
}
