using System.Windows;
using UnrealKit.Core.Adb;
using UnrealKit.Core.Processes;
using UnrealKit.Core.Devices;
using UnrealKit.Core.Projects;
using UnrealKit.Desktop.Models;

namespace UnrealKit.Desktop.Services;

public interface IDesktopAdbServiceFactory
{
    IAdbService Create(ProjectSettings? settings, IProgress<ProcessOutput>? output);

    AdbPathResolution Resolve(ProjectSettings? settings);
}

public sealed class DesktopAdbServiceFactory(AdbPathResolver? adbPathResolver = null) : IDesktopAdbServiceFactory
{
    private readonly AdbPathResolver _adbPathResolver = adbPathResolver ?? new AdbPathResolver();

    public IAdbService Create(ProjectSettings? settings, IProgress<ProcessOutput>? output)
    {
        var resolution = Resolve(settings);
        var adbPath = resolution.ResolvedPath ?? throw new AdbPathResolutionException(resolution);
        return new AdbService(new ProcessRunner(), adbPath, output);
    }

    // adb 路径属于 Android 配置：未配置 Android 平台的工程走环境变量与 PATH 解析。
    public AdbPathResolution Resolve(ProjectSettings? settings) => _adbPathResolver.Resolve(null, settings?.Android?.AdbPath);
}

public interface IUserConfirmationService
{
    Task<bool> ConfirmDeleteLaunchParametersAsync(LaunchOperationTarget target);

    /// <summary>
    /// 告知用户上次打开的工程已不可用。只是通知，不代替用户决定后续动作——
    /// 新建还是手动打开由用户从菜单栏选择。
    /// </summary>
    Task NotifyLastProjectUnavailableAsync(string projectFilePath, string reason);

    /// <summary>
    /// 安装应用包前确认。安装是对设备可见且可能覆盖既有应用的破坏性操作，
    /// 必须在执行前展示完整的目标设备与本地包路径并征得同意。
    /// </summary>
    Task<bool> ConfirmInstallApplicationAsync(string deviceId, string localApplicationPath);
}

public sealed class WpfUserConfirmationService(Window owner) : IUserConfirmationService
{
    public Task NotifyLastProjectUnavailableAsync(string projectFilePath, string reason)
    {
        var message = $"无法打开上次的工程：\n\n" +
                      $"{projectFilePath}\n\n" +
                      $"{reason}\n\n" +
                      "请从菜单栏「Project」新建工程，或手动打开另一个 .ukit 工程。";
        MessageBox.Show(owner, message, "上次的工程不可用", MessageBoxButton.OK, MessageBoxImage.Warning);
        return Task.CompletedTask;
    }

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

    public Task<bool> ConfirmInstallApplicationAsync(string deviceId, string localApplicationPath)
    {
        var message = $"The following application package will be installed to the selected device:\n\n" +
                      $"Device: {deviceId}\n" +
                      $"Package: {localApplicationPath}\n\n" +
                      "Installing may overwrite the existing application on the device.\nThis operation cannot be undone.";
        var result = MessageBox.Show(owner, message, "Confirm application installation", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return Task.FromResult(result == MessageBoxResult.Yes);
    }
}
