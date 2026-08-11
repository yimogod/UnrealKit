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
    public async Task StartAndStop_CmdExe_Lifecycle()
    {
        var service = new Win64DeviceService();
        var device = (await service.ListDevicesAsync())[0];
        var cmdPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");

        if (!File.Exists(cmdPath))
            return; // Skip if cmd.exe not found (shouldn't happen on Windows)

        // Start cmd.exe — it will run briefly and exit, so start /c exit
        var startResult = await service.StartApplicationAsync(device, cmdPath);
        // cmd.exe exits immediately, so this is expected to work or fail fast
        Assert.Equal(0, startResult.ExitCode);

        // cmd /c exit exits immediately, so we may not be able to kill it.
        // StopApplicationAsync may throw if the process already exited.
        // We accept either success or DeviceCommandException indicating no such process.
        try
        {
            var stopResult = await service.StopApplicationAsync(device, "cmd");
            Assert.Equal(0, stopResult.ExitCode);
            Assert.Contains("Stopped", stopResult.StandardOutput);
        }
        catch (DeviceCommandException ex)
        {
            // Process already exited — that's fine for this test
            Assert.Contains("No process named", ex.Message);
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
            Platform = TargetPlatform.Win64,
            PackageName = "WinGame",
            Win64WorkingDirectory = @"C:\Projects\WinGame",
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
    public void CaptureService_CreatePlan_ForWin64_NoWorkingDir_FallsBack()
    {
        var settings = ProjectSettings.CreateDefaults("WinGame") with
        {
            Platform = TargetPlatform.Win64,
            PackageName = "WinGame",
            Win64WorkingDirectory = null,
            UnrealProjectName = "WinGame"
        };
        var project = new UkitProject(
            @"C:\Projects\WinGame\WinGame.ukit",
            @"C:\Projects\WinGame",
            UkitProjectDescriptor.CreateDefault("WinGame"),
            settings);

        var device = new Win64Device();
        var service = new CaptureService();
        var plan = service.CreatePlan(new CaptureRequest(project, device, "test"));

        // Without working directory, falls back to project name
        Assert.Contains(@"WinGame\Saved", plan.DeviceSavedDirectory);
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
