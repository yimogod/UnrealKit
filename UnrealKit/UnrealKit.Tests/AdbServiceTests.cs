using UnrealKit.Core.Adb;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;

namespace UnrealKit.Tests;

public sealed class AdbServiceTests
{
    [Fact]
    public void Parse_RecognizesDeviceStatusMetadataAndConnectionType()
    {
        const string output = "List of devices attached\n" +
                              "R58M123ABC\tdevice product:oriole model:Pixel_6 device:oriole transport_id:1\n" +
                              "192.168.1.40:5555\toffline product:foo model:Test_Device device:test\n" +
                              "ZX1G22\tunauthorized usb:1-1\n";

        var devices = AdbDeviceParser.Parse(output);

        Assert.Collection(
            devices,
            device =>
            {
                Assert.Equal("R58M123ABC", device.SerialNumber);
                Assert.Equal(AdbDeviceStatus.Device, device.Status);
                Assert.Equal("Pixel 6", device.Model);
                Assert.Equal(AdbConnectionType.Usb, device.ConnectionType);
            },
            device =>
            {
                Assert.Equal(AdbDeviceStatus.Offline, device.Status);
                Assert.Equal(AdbConnectionType.Network, device.ConnectionType);
            },
            device => Assert.Equal(AdbDeviceStatus.Unauthorized, device.Status));
    }

    [Fact]
    public async Task RunDumpsysAsync_PassesExplicitDeviceSerialInArgumentList()
    {
        var runner = new RecordingProcessRunner(new ProcessExecutionResult(0, "memory report", string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        var service = new AdbService(runner, "custom-adb");

        var result = await service.RunDumpsysAsync("R58M123ABC", "com.example.game");

        Assert.True(result.Succeeded);
        Assert.NotNull(runner.Request);
        Assert.Equal("custom-adb", runner.Request.FileName);
        Assert.Equal(["-s", "R58M123ABC", "shell", "dumpsys", "meminfo", "com.example.game"], runner.Request.Arguments);
    }

    [Fact]
    public async Task PushFileAsync_PreservesNonZeroResultInAdbCommandException()
    {
        var expectedResult = new ProcessExecutionResult(1, string.Empty, "adb: error", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var service = new AdbService(new RecordingProcessRunner(expectedResult));

        var exception = await Assert.ThrowsAsync<AdbCommandException>(() => service.PushFileAsync("R58M123ABC", "input.txt", "/sdcard/input.txt"));

        Assert.Same(expectedResult, exception.Result);
        Assert.Contains("退出码 1", exception.Message, StringComparison.Ordinal);
        Assert.Equal("adb: error", exception.Result.StandardError);
    }

    [Fact]
    public async Task DeviceCommand_RejectsMissingSerialNumber()
    {
        var service = new AdbService(new RecordingProcessRunner(ProcessExecutionResultForSuccess()));

        await Assert.ThrowsAsync<ArgumentException>(() => service.RunDumpsysAsync(string.Empty, "com.example.game"));
    }

    [Fact]
    public async Task DeviceCommand_RejectsNonUnixRemotePath()
    {
        var service = new AdbService(new RecordingProcessRunner(ProcessExecutionResultForSuccess()));

        await Assert.ThrowsAsync<ArgumentException>(() => service.PushFileAsync("R58M123ABC", "input.txt", "C:\\temp\\input.txt"));
    }

    private static ProcessExecutionResult ProcessExecutionResultForSuccess() => new(0, string.Empty, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class RecordingProcessRunner(ProcessExecutionResult result) : IProcessRunner
    {
        public ProcessExecutionRequest? Request { get; private set; }

        public Task<ProcessExecutionResult> RunAsync(ProcessExecutionRequest request, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(result);
        }
    }
}
