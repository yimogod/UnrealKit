using System.Text.Json;
using UnrealKit.Core.Download;
using UnrealKit.Core.Processes;
using UnrealKit.Core.Projects;

namespace UnrealKit.Cli;

/// <summary>
/// <c>unrealkit download</c>：从 FTP 下载最新构建；
/// <c>unrealkit download install</c>：把本地 APK 安装到 Android 设备。
/// </summary>
internal static class DownloadCommands
{
    internal static async Task<int> RunAsync(string[] arguments)
    {
        var (commandArguments, adbPath) = CliOptions.ParseAdbPath(arguments);
        if (commandArguments.Length == 0)
        {
            return FailUsage();
        }

        return commandArguments[0].ToLowerInvariant() switch
        {
            "install" => await InstallAsync(commandArguments[1..], adbPath),
            _ => await DownloadAsync(commandArguments, adbPath)
        };
    }

    private static async Task<int> DownloadAsync(string[] options, string? adbPath)
    {
        // 顶层动词本身可能作为首项被误传（如 "download download ..."），这里不处理，
        // 只校验选项。--platform 必填：FtpPath 按平台配置，不替用户猜。
        CliOptions.EnsureOnly(options, CliOptions.Allowed("--project", "--platform", "--format"));
        var json = CliOptions.IsJsonFormat(options);
        var project = await new ProjectService().OpenProjectAsync(CliOptions.GetRequired(options, "--project"));
        var platform = PlatformNames.Parse(CliOptions.GetRequired(options, "--platform"), "--platform");

        var profile = project.Settings.ProfileFor(platform);
        if (profile is null)
        {
            Console.Error.WriteLine(
                $"工程尚未配置 {PlatformNames.ToName(platform)} 平台。已配置的平台: " +
                $"{string.Join(", ", project.Settings.ConfiguredPlatforms)}。");
            return 2;
        }

        var localBaseDirectory = Path.Combine(project.IntermediateDir, "Download", PlatformNames.ToName(platform));
        var request = new DownloadRequest(
            platform,
            project.Settings.FtpSettings,
            profile.FtpPath,
            localBaseDirectory);

        var result = await new FtpDownloadService(new FluentFtpClientFactory()).DownloadAsync(request);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                result.Succeeded,
                result.LocalPath,
                result.SourceSubdir,
                result.FileCount,
                Diagnostics = result.Diagnostics.Select(d => new
                {
                    Severity = d.Severity.ToString(),
                    d.Code,
                    d.Message,
                    d.Path,
                    d.SuggestedFix,
                    d.LineNumber
                })
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            if (result.Succeeded)
            {
                Console.WriteLine($"Downloaded latest '{result.SourceSubdir}' for {PlatformNames.ToName(platform)}.");
                Console.WriteLine($"Local: {result.LocalPath}");
                Console.WriteLine($"Files: {result.FileCount}");
            }

            CliOutput.WriteDiagnostics(result.Diagnostics);
        }

        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> InstallAsync(string[] options, string? adbPath)
    {
        CliOptions.EnsureOnly(options, CliOptions.Allowed("--project", "--device", "--platform", "--apk", "--adb-path"));
        var project = await new ProjectService().OpenProjectAsync(CliOptions.GetRequired(options, "--project"));
        var apkPath = CliOptions.GetRequired(options, "--apk");
        var fullApkPath = Path.GetFullPath(apkPath);

        if (!File.Exists(fullApkPath))
        {
            Console.Error.WriteLine($"APK not found: {fullApkPath}");
            return 2;
        }

        var resolved = await DeviceResolver.ResolveDeviceTargetAsync(project, options, adbPath);
        if (resolved.DeviceService.Platform != TargetPlatform.Android)
        {
            Console.Error.WriteLine(
                $"install 仅支持 Android 设备，当前设备属于 {PlatformNames.ToName(resolved.DeviceService.Platform)} 平台。");
            return 2;
        }

        if (!resolved.DeviceService.Supports(UnrealKit.Core.Devices.DeviceCapability.InstallApplication))
        {
            Console.Error.WriteLine($"设备 {resolved.DeviceId} 不支持安装应用包。");
            return 2;
        }

        var result = await resolved.DeviceService.InstallApplicationAsync(resolved.Device, fullApkPath);
        Console.WriteLine($"Installed {fullApkPath} to {resolved.DeviceId}.");
        if (!result.Succeeded)
        {
            Console.Error.WriteLine($"Install failed with exit code {result.ExitCode}.");
            if (!string.IsNullOrWhiteSpace(result.StandardError))
            {
                Console.Error.WriteLine(result.StandardError);
            }

            return 1;
        }

        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            Console.WriteLine(result.StandardOutput);
        }

        return 0;
    }

    private static int FailUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  unrealkit download --project <project.ukit> --platform <Android|Win64> [--format text|json]");
        Console.Error.WriteLine("  unrealkit download install --project <project.ukit> --device <serial> --apk <path> [--adb-path <path>]");
        return 2;
    }
}
