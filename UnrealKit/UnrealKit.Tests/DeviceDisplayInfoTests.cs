using UnrealKit.Core.Adb;
using UnrealKit.Core.Devices;
using UnrealKit.Core.Projects;

namespace UnrealKit.Tests;

public sealed class DeviceDisplayInfoTests
{
    private static AdbDevice AndroidDevice(string serial = "R58M123ABC") =>
        new(serial, AdbDeviceStatus.Device, null, "Pixel", null, AdbConnectionType.Usb, $"{serial} device model:Pixel");

    private static ProjectSettings SettingsWithAliases(params (string DeviceId, string Alias)[] aliases) =>
        ProjectSettings.CreateDefaults("Sample") with
        {
            DeviceAliases = DeviceAliasMap.Create(aliases.Select(pair => new KeyValuePair<string, string>(pair.DeviceId, pair.Alias)))
        };

    [Fact]
    public void Create_ConfiguredAlias_KeepsDeviceIdAndExposesAlias()
    {
        var info = DeviceDisplayInfo.Create(AndroidDevice(), SettingsWithAliases(("R58M123ABC", "测试机A")));

        // id 不被别名替换：所有设备操作以 id 为准，界面上换掉它会让日志与操作对不上。
        Assert.Equal("R58M123ABC", info.Id);
        Assert.Equal("测试机A", info.Alias);
        Assert.True(info.HasAlias);
        Assert.Equal("测试机A", info.DisplayLabel);
    }

    [Fact]
    public void Create_NoAlias_LeavesAliasNullAndFallsBackToModelName()
    {
        var info = DeviceDisplayInfo.Create(AndroidDevice(), SettingsWithAliases(("OTHER-SERIAL", "别的机器")));

        // 别名为 null 而不是 id 或型号：调用方据此区分「配过别名」与「只有型号」。
        Assert.Null(info.Alias);
        Assert.False(info.HasAlias);
        Assert.Equal("Pixel", info.DisplayLabel);
    }

    [Fact]
    public void Create_WithoutProjectSettings_StillListsDevice()
    {
        // 未打开工程时别名无从查起，但设备本身照常可用。
        var info = DeviceDisplayInfo.Create(AndroidDevice(), settings: null);

        Assert.Equal("R58M123ABC", info.Id);
        Assert.Null(info.Alias);
        Assert.Equal("Android", info.Platform);
        Assert.True(info.IsAvailable);
    }

    [Fact]
    public void StatusText_UsesAdbWording_SoListAndSummaryAgree()
    {
        // 沿用 `adb devices` 的措辞，且只有这一处定义：列表列与选中设备摘要都取它。
        var online = DeviceDisplayInfo.Create(AndroidDevice(), settings: null);
        var offline = DeviceDisplayInfo.Create(
            new AdbDevice("R58M999XYZ", AdbDeviceStatus.Offline, null, "Pixel", null, AdbConnectionType.Usb, "R58M999XYZ offline"),
            settings: null);

        Assert.Equal("device", online.StatusText);
        Assert.Equal("offline", offline.StatusText);
        Assert.False(offline.IsAvailable);
    }

    [Fact]
    public void Device_IsPreservedForOperations()
    {
        // 展示投影原样保留枚举得到的设备：操作传它，而不是传投影。
        // 「投影不是 IDevice」由类型系统保证，无需运行时断言。
        var device = AndroidDevice();
        var info = DeviceDisplayInfo.Create(device, SettingsWithAliases(("R58M123ABC", "测试机A")));

        Assert.Same(device, info.Device);
    }

    [Fact]
    public void DeviceAliasMap_DiscardsBlankEntriesAndMatchesCaseInsensitively()
    {
        var map = DeviceAliasMap.Create(new Dictionary<string, string>
        {
            ["r58m123abc"] = "测试机A",
            ["  192.168.1.100:5555  "] = "  测试机B  ",
            ["BLANK"] = "   ",
            ["   "] = "无键别名"
        });

        Assert.Equal(2, map.Count);
        Assert.Equal("测试机A", map.TryGet("R58M123ABC"));
        Assert.Equal("测试机B", map.TryGet("192.168.1.100:5555"));
        Assert.Null(map.TryGet("BLANK"));
    }

    [Fact]
    public void DeviceAliasMap_EqualityIsByContent()
    {
        // ProjectSettings 是 record：别名表若按引用比较，未改动别名的 `with` 复制也会判为不同。
        var first = DeviceAliasMap.Create(new Dictionary<string, string> { ["A"] = "甲" });
        var second = DeviceAliasMap.Create(new Dictionary<string, string> { ["a"] = "甲" });
        var different = DeviceAliasMap.Create(new Dictionary<string, string> { ["A"] = "乙" });

        Assert.Equal(first, second);
        Assert.NotEqual(first, different);
        Assert.Equal(DeviceAliasMap.Empty, DeviceAliasMap.Create(new Dictionary<string, string>()));
    }
}
