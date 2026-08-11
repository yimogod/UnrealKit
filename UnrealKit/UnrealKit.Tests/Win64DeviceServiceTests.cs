using UnrealKit.Core.Devices;

namespace UnrealKit.Tests;

public sealed class Win64DeviceServiceTests
{
    [Fact]
    public async Task ListDevicesAsync_ReturnsLocalhost()
    {
        var service = new Win64DeviceService();
        var devices = await service.ListDevicesAsync();

        Assert.Single(devices);
        var device = devices[0];
        Assert.Equal("localhost", device.Id);
        Assert.Equal("Win64", device.Platform);
        Assert.True(device.IsAvailable);
        Assert.Equal(Environment.MachineName, device.Name);
    }

    [Fact]
    public async Task CaptureMemoryAsync_NoProcess_ThrowsDeviceCommandException()
    {
        var service = new Win64DeviceService();
        var device = (await service.ListDevicesAsync())[0];

        var ex = await Assert.ThrowsAsync<DeviceCommandException>(
            () => service.CaptureMemoryAsync(device, "NonExistentProcess_12345"));

        Assert.Equal(1, ex.Result.ExitCode);
        Assert.Contains("No process named", ex.Result.StandardError);
    }
}
