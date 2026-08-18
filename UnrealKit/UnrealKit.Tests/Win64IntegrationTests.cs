using System.Diagnostics;
using UnrealKit.Core.Adb;
using UnrealKit.Core.Capture;
using UnrealKit.Core.Devices;
using UnrealKit.Core.Processes;
using UnrealKit.Core.Projects;

namespace UnrealKit.Tests;

public sealed class Win64IntegrationTests
{
    [Fact]
    public async Task CaptureMemoryAsync_SelfProcess_ReturnsValidOutput()
    {
        var service = new Win64DeviceService();
        var device = (await service.ListDevicesAsync())[0];

        // Capture the current test host process
        var processName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
        var result = await service.CaptureMemoryAsync(device, processName);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("** WIN64 MEMINFO for process", result.StandardOutput);
        Assert.Contains("WorkingSetMB:", result.StandardOutput);
        Assert.Contains("PrivateMemoryMB:", result.StandardOutput);
        Assert.Contains("Threads:", result.StandardOutput);
    }

    [Fact]
    public async Task StartApplicationAsync_InvalidPath_ThrowsDeviceCommandException()
    {
        var service = new Win64DeviceService();
        var device = (await service.ListDevicesAsync())[0];

        var ex = await Assert.ThrowsAsync<DeviceCommandException>(
            () => service.StartApplicationAsync(device, @"C:\NonExistent\FakeApp.exe"));

        Assert.Equal(1, ex.Result.ExitCode);
        Assert.Contains("Executable not found", ex.Result.StandardError);
    }

    [Fact]
    public async Task StopApplicationAsync_NoSuchProcess_ThrowsDeviceCommandException()
    {
        var service = new Win64DeviceService();
        var device = (await service.ListDevicesAsync())[0];

        var ex = await Assert.ThrowsAsync<DeviceCommandException>(
            () => service.StopApplicationAsync(device, "NonExistentProcess_XYZ_12345"));

        Assert.Equal(1, ex.Result.ExitCode);
        Assert.Contains("No process named", ex.Result.StandardError);
    }

    [Fact]
    public async Task StartApplicationAsync_RunsExecutableToCompletion()
    {
        var service = new Win64DeviceService();
        var device = (await service.ListDevicesAsync())[0];
        using var executable = UniquelyNamedExecutable.CreateFromCommandProcessor();

        // StartApplicationAsync 会等待进程结束，因此这里只验证「能启动并正常退出」。
        var startResult = await service.StartApplicationAsync(device, executable.Path);

        Assert.Equal(0, startResult.ExitCode);
    }

    [Fact]
    public async Task StopApplicationAsync_TerminatesOnlyTheNamedProcess()
    {
        var service = new Win64DeviceService();
        var device = (await service.ListDevicesAsync())[0];

        // 必须用唯一进程名：StopApplicationAsync 按名字杀进程，
        // 若这里传 "cmd" 就会杀掉本机所有 cmd.exe——包括并行测试启动的子进程
        // 和开发者自己的终端。曾因此导致 ProcessRunnerTests 超时测试随机失败。
        using var executable = UniquelyNamedExecutable.CreateFromCommandProcessor();
        using var running = Process.Start(new ProcessStartInfo(executable.Path, "/d /c ping -n 60 127.0.0.1 > nul")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Failed to start the test process.");

        try
        {
            var stopResult = await service.StopApplicationAsync(device, executable.ProcessName);

            Assert.Equal(0, stopResult.ExitCode);
            Assert.Contains("Stopped", stopResult.StandardOutput);
            Assert.True(running.WaitForExit(TimeSpan.FromSeconds(10)), "The target process should have been terminated.");
        }
        finally
        {
            if (!running.HasExited)
            {
                running.Kill(entireProcessTree: true);
                running.WaitForExit(TimeSpan.FromSeconds(10));
            }
        }
    }

    /// <summary>
    /// cmd.exe 的唯一命名副本。用于按进程名操作的测试，避免影响本机同名进程。
    /// </summary>
    private sealed class UniquelyNamedExecutable : IDisposable
    {
        private UniquelyNamedExecutable(string path)
        {
            Path = path;
            ProcessName = System.IO.Path.GetFileNameWithoutExtension(path);
        }

        public string Path { get; }

        /// <summary>Process.GetProcessesByName 使用的名称（不含扩展名）。</summary>
        public string ProcessName { get; }

        public static UniquelyNamedExecutable CreateFromCommandProcessor()
        {
            var source = System.IO.Path.Combine(Environment.SystemDirectory, "cmd.exe");
            Assert.True(File.Exists(source), $"Command processor not found: {source}");

            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ukit_exe_{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var destination = System.IO.Path.Combine(directory, $"ukit_shell_{Guid.NewGuid():N}.exe");
            File.Copy(source, destination);
            return new UniquelyNamedExecutable(destination);
        }

        public void Dispose()
        {
            try
            {
                var directory = System.IO.Path.GetDirectoryName(Path);
                if (directory is not null && Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (IOException)
            {
                // 进程刚退出时文件可能仍被占用，临时目录留给系统清理即可。
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public async Task PullDirectoryAsync_CopiesDirectoryContents()
    {
        var service = new Win64DeviceService();
        var device = (await service.ListDevicesAsync())[0];
        var sourceDir = Path.Combine(Path.GetTempPath(), $"ukit_test_source_{Guid.NewGuid():N}");
        var destDir = Path.Combine(Path.GetTempPath(), $"ukit_test_dest_{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(sourceDir);
            await File.WriteAllTextAsync(Path.Combine(sourceDir, "test.txt"), "hello");
            Directory.CreateDirectory(Path.Combine(sourceDir, "subdir"));
            await File.WriteAllTextAsync(Path.Combine(sourceDir, "subdir", "nested.txt"), "world");

            var result = await service.PullDirectoryAsync(device, sourceDir, destDir);
            Assert.Equal(0, result.ExitCode);

            Assert.True(File.Exists(Path.Combine(destDir, "test.txt")));
            Assert.True(File.Exists(Path.Combine(destDir, "subdir", "nested.txt")));
        }
        finally
        {
            if (Directory.Exists(sourceDir)) Directory.Delete(sourceDir, recursive: true);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        }
    }

    [Fact]
    public void DeviceServiceFactory_CreatesWin64Service()
    {
        var factory = new DeviceServiceFactory(null, new ProcessRunner());
        var device = new Win64Device();

        var service = factory.CreateForDevice(device);

        Assert.IsType<Win64DeviceService>(service);
    }

    [Fact]
    public void DeviceServiceFactory_RejectsUnknownPlatform()
    {
        var factory = new DeviceServiceFactory();
        var unknownDevice = new UnknownDevice("unsupported", "Unknown");

        Assert.Throws<ArgumentException>(() => factory.CreateForDevice(unknownDevice));
    }

    [Fact]
    public void CaptureService_CreatesPlan_ForWin64Project()
    {
        var settings = ProjectSettings.CreateDefaults("WinGame") with
        {
            Win64 = new Win64PlatformProfile(@"C:\Projects\WinGame\WinGame.exe", @"C:\Projects\WinGame"),
            UnrealProjectName = "WinGame"
        };
        var project = new UkitProject(
            @"C:\Projects\WinGame\WinGame.ukit",
            @"C:\Projects\WinGame",
            UkitProjectDescriptor.CreateDefault("WinGame"),
            settings);

        var device = new Win64Device();
        var service = new CaptureService();
        var plan = service.CreatePlan(new CaptureRequest(project, device, "baseline"));

        Assert.Contains("Win64", plan.CaptureDirectory);
        Assert.Contains("baseline", plan.CaptureDirectory);
        Assert.Contains(@"C:\Projects\WinGame\WinGame\Saved", plan.DeviceSavedDirectory);
    }

    [Fact]
    public void CaptureService_CreatePlan_ForWin64_NoWorkingDir_ThrowsWithActionableMessage()
    {
        var settings = ProjectSettings.CreateDefaults("WinGame") with
        {
            Win64 = new Win64PlatformProfile(@"C:\Projects\WinGame\WinGame.exe", WorkingDirectory: string.Empty),
            UnrealProjectName = "WinGame"
        };
        var project = new UkitProject(
            @"C:\Projects\WinGame\WinGame.ukit",
            @"C:\Projects\WinGame",
            UkitProjectDescriptor.CreateDefault("WinGame"),
            settings);

        var device = new Win64Device();
        var service = new CaptureService();

        // Falling back to a relative path would resolve against the current process
        // working directory, so GUI and CLI would archive from different locations.
        // The Saved directory must be explicitly configured instead.
        var exception = Assert.Throws<InvalidOperationException>(
            () => service.CreatePlan(new CaptureRequest(project, device, "test")));

        Assert.Contains("WorkingDirectory", exception.Message);
    }

    [Fact]
    public void CaptureService_CreatePlan_ForWin64_ResolvesSavedDirectoryToAbsolutePath()
    {
        var settings = ProjectSettings.CreateDefaults("WinGame") with
        {
            Win64 = new Win64PlatformProfile(@"C:\Builds\WinGame\WinGame.exe", @"C:\Builds\WinGame"),
            UnrealProjectName = "WinGame"
        };
        var project = new UkitProject(
            @"C:\Projects\WinGame\WinGame.ukit",
            @"C:\Projects\WinGame",
            UkitProjectDescriptor.CreateDefault("WinGame"),
            settings);

        var plan = new CaptureService().CreatePlan(new CaptureRequest(project, new Win64Device(), "test"));

        Assert.Equal(@"C:\Builds\WinGame\WinGame\Saved", plan.DeviceSavedDirectory);
        Assert.True(Path.IsPathFullyQualified(plan.DeviceSavedDirectory));
    }

    [Fact]
    public void CaptureService_CreatePlan_RejectsDeviceOfUnconfiguredPlatform()
    {
        // 只配置了 Android 的工程遇到 Win64 设备必须报错并列出已配置平台，
        // 不能拿 Android 的路径去采 Win64——那会拉到空目录却报告成功。
        var settings = ProjectSettings.CreateDefaults("AndroidGame") with
        {
            Android = AndroidPlatformProfile.CreateDefaults() with { PackageName = "com.example.game" },
            Win64 = null
        };
        var project = new UkitProject(
            @"C:\Projects\AndroidGame\AndroidGame.ukit",
            @"C:\Projects\AndroidGame",
            UkitProjectDescriptor.CreateDefault("AndroidGame"),
            settings);

        var exception = Assert.Throws<InvalidOperationException>(
            () => new CaptureService().CreatePlan(new CaptureRequest(project, new Win64Device(), "test")));

        Assert.Contains("Win64", exception.Message);
        Assert.Contains("Android", exception.Message);
    }

    [Fact]
    public void CaptureService_CreatePlan_SameProjectServesBothPlatforms()
    {
        // 同一工程配置了两个平台时，归档目录随设备平台走，互不干扰。
        var settings = ProjectSettings.CreateDefaults("DualGame") with
        {
            UnrealProjectName = "DualGame",
            Android = AndroidPlatformProfile.CreateDefaults() with { PackageName = "com.example.dual" },
            Win64 = new Win64PlatformProfile(@"C:\Builds\DualGame\DualGame.exe", @"C:\Builds\DualGame")
        };
        var project = new UkitProject(
            @"C:\Projects\DualGame\DualGame.ukit",
            @"C:\Projects\DualGame",
            UkitProjectDescriptor.CreateDefault("DualGame"),
            settings);
        var service = new CaptureService();

        var win64Plan = service.CreatePlan(new CaptureRequest(project, new Win64Device(), "test"));
        var androidPlan = service.CreatePlan(new CaptureRequest(
            project,
            new AdbDevice("device-01", AdbDeviceStatus.Device, null, "Pixel", null, AdbConnectionType.Usb, string.Empty),
            "test"));

        Assert.Contains(Path.Combine("Content", "Win64"), win64Plan.CaptureDirectory);
        Assert.Equal(@"C:\Builds\DualGame\DualGame\Saved", win64Plan.DeviceSavedDirectory);
        Assert.Contains(Path.Combine("Content", "Android"), androidPlan.CaptureDirectory);
        Assert.StartsWith("/sdcard/", androidPlan.DeviceSavedDirectory, StringComparison.Ordinal);
    }

    private sealed class UnknownDevice : IDevice
    {
        public UnknownDevice(string id, string name) { Id = id; Name = name; }
        public string Id { get; }
        public string Name { get; }
        public string Platform => "Unsupported";
        public bool IsAvailable => true;
    }
}
