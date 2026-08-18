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
        var service = new AdbService(new RecordingProcessRunner(expectedResult), "adb");

        var exception = await Assert.ThrowsAsync<AdbCommandException>(() => service.PushFileAsync("R58M123ABC", "input.txt", "/sdcard/input.txt"));

        Assert.Same(expectedResult, exception.Result);
        Assert.Contains("退出码 1", exception.Message, StringComparison.Ordinal);
        Assert.Equal("adb: error", exception.Result.StandardError);
    }

    [Fact]
    public async Task DeviceCommand_RejectsMissingSerialNumber()
    {
        var service = new AdbService(new RecordingProcessRunner(ProcessExecutionResultForSuccess()), "adb");

        await Assert.ThrowsAsync<ArgumentException>(() => service.RunDumpsysAsync(string.Empty, "com.example.game"));
    }

    [Fact]
    public async Task DeviceCommand_RejectsNonUnixRemotePath()
    {
        var service = new AdbService(new RecordingProcessRunner(ProcessExecutionResultForSuccess()), "adb");

        await Assert.ThrowsAsync<ArgumentException>(() => service.PushFileAsync("R58M123ABC", "input.txt", "C:\\temp\\input.txt"));
    }

    [Fact]
    public void ParseAddresses_ClassifiesInterfacesAndSkipsLoopback()
    {
        const string output = """
            1: lo: <LOOPBACK,UP,LOWER_UP> mtu 65536 qdisc noqueue state UNKNOWN group default qlen 1000
                inet 127.0.0.1/8 scope host lo
            25: wlan0: <BROADCAST,MULTICAST,UP,LOWER_UP> mtu 1500 qdisc mq state UP group default qlen 3000
                inet 192.168.1.23/24 brd 192.168.1.255 scope global wlan0
            30: rmnet_data0@if12: <UP,LOWER_UP> mtu 1500 qdisc pfifo_fast state UNKNOWN group default qlen 1000
                inet 10.148.22.7/30 scope global rmnet_data0
            33: rndis0: <BROADCAST,MULTICAST,UP,LOWER_UP> mtu 1500 qdisc pfifo_fast state UP group default qlen 1000
                inet 192.168.42.129/24 brd 192.168.42.255 scope global rndis0
            """;

        var addresses = AdbNetworkParser.ParseAddresses(output);

        Assert.Collection(
            addresses,
            address =>
            {
                Assert.Equal("wlan0", address.InterfaceName);
                Assert.Equal("192.168.1.23", address.Address);
                Assert.Equal(24, address.PrefixLength);
                Assert.Equal(DeviceNetworkInterfaceKind.WiFi, address.Kind);
            },
            address =>
            {
                // 头行是 rmnet_data0@if12，@ 后的对端索引不属于接口名。
                Assert.Equal("rmnet_data0", address.InterfaceName);
                Assert.Equal(DeviceNetworkInterfaceKind.Cellular, address.Kind);
            },
            address =>
            {
                Assert.Equal("rndis0", address.InterfaceName);
                Assert.Equal(DeviceNetworkInterfaceKind.UsbTethering, address.Kind);
            });
    }

    [Fact]
    public void ParseAddresses_IgnoresMalformedAndNonIpv4Lines()
    {
        const string output = """
            25: wlan0: <BROADCAST,MULTICAST,UP,LOWER_UP> mtu 1500
                inet6 fe80::1234/64 scope link
                inet 192.168.1.999/24 scope global wlan0
                inet 192.168.1.23/99 scope global wlan0
                inet
            garbage line without structure
            """;

        var addresses = AdbNetworkParser.ParseAddresses(output);

        // 超范围的地址、非法前缀长度、截断行、IPv6 都不产生条目，也不抛异常——
        // 单行畸形不应让整台设备的查询失败。
        Assert.Empty(addresses);
    }

    [Fact]
    public void ParseRouteSourceAddresses_TakesSourceAddressWithoutPrefixAndDeduplicates()
    {
        const string output = """
            default via 192.168.1.1 dev wlan0 table 1021 proto static
            192.168.1.0/24 dev wlan0 proto kernel scope link src 192.168.1.23
            192.168.1.0/24 dev wlan0 table 1021 proto static scope link src 192.168.1.23
            10.148.22.4/30 dev rmnet_data0 proto kernel scope link src 10.148.22.7
            local 127.0.0.1 dev lo table local proto kernel scope host src 127.0.0.1
            """;

        var addresses = AdbNetworkParser.ParseRouteSourceAddresses(output);

        Assert.Collection(
            addresses,
            address =>
            {
                Assert.Equal("wlan0", address.InterfaceName);
                Assert.Equal("192.168.1.23", address.Address);
                Assert.Null(address.PrefixLength);
            },
            address => Assert.Equal("rmnet_data0", address.InterfaceName));
    }

    [Fact]
    public async Task GetIpAddressesAsync_UsesIpAddrAndPassesSerialInArgumentList()
    {
        const string output = """
            25: wlan0: <BROADCAST,MULTICAST,UP,LOWER_UP> mtu 1500
                inet 192.168.1.23/24 brd 192.168.1.255 scope global wlan0
            """;
        var runner = new ScriptedProcessRunner(new ProcessExecutionResult(0, output, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        var service = new AdbService(runner, "adb");

        var addresses = await service.GetIpAddressesAsync("R58M123ABC");

        Assert.Equal("192.168.1.23", Assert.Single(addresses).Address);
        Assert.Equal(["-s", "R58M123ABC", "shell", "ip", "-f", "inet", "addr"], Assert.Single(runner.Requests).Arguments);
    }

    [Fact]
    public async Task GetIpAddressesAsync_FallsBackToIpRouteWhenAddrCommandFails()
    {
        var runner = new ScriptedProcessRunner(
            new ProcessExecutionResult(1, string.Empty, "ip: not found", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new ProcessExecutionResult(0, "192.168.1.0/24 dev wlan0 proto kernel scope link src 192.168.1.23", string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        var service = new AdbService(runner, "adb");

        var addresses = await service.GetIpAddressesAsync("R58M123ABC");

        Assert.Equal("192.168.1.23", Assert.Single(addresses).Address);
        Assert.Equal(["-s", "R58M123ABC", "shell", "ip", "route"], runner.Requests[1].Arguments);
    }

    [Fact]
    public async Task GetIpAddressesAsync_ThrowsListingAttemptedCommandsWhenNoAddressFound()
    {
        // 两条命令都成功执行但只有回环地址，等价于「设备未联网」。
        var runner = new ScriptedProcessRunner(
            new ProcessExecutionResult(0, "1: lo: <LOOPBACK,UP>\n    inet 127.0.0.1/8 scope host lo", string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new ProcessExecutionResult(0, string.Empty, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        var service = new AdbService(runner, "adb");

        var exception = await Assert.ThrowsAsync<AdbDeviceAddressUnavailableException>(
            () => service.GetIpAddressesAsync("R58M123ABC"));

        Assert.Equal("R58M123ABC", exception.SerialNumber);
        Assert.Equal(2, exception.AttemptedCommands.Count);
        Assert.Contains("ip -f inet addr", exception.AttemptedCommands[0], StringComparison.Ordinal);
        Assert.Contains("ip route", exception.AttemptedCommands[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetIpAddressesAsync_RejectsMissingSerialNumber()
    {
        var service = new AdbService(new RecordingProcessRunner(ProcessExecutionResultForSuccess()), "adb");

        await Assert.ThrowsAsync<ArgumentException>(() => service.GetIpAddressesAsync(" "));
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

    /// <summary>按调用顺序返回预设结果，用于验证多命令探测序列。</summary>
    private sealed class ScriptedProcessRunner(params ProcessExecutionResult[] results) : IProcessRunner
    {
        private int _callCount;

        public List<ProcessExecutionRequest> Requests { get; } = [];

        public Task<ProcessExecutionResult> RunAsync(ProcessExecutionRequest request, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(results[Math.Min(_callCount++, results.Length - 1)]);
        }
    }
}
