namespace UnrealKit.Core.Adb;

/// <summary>
/// ADB 设备解析器
/// </summary>
public static class AdbDeviceParser
{
    /// <summary>
    /// 解析 ADB 设备列表输出
    /// </summary>
    public static IReadOnlyList<AdbDevice> Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var devices = new List<AdbDevice>();
        foreach (var rawLine in output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("List of devices attached", StringComparison.OrdinalIgnoreCase) || line.StartsWith('*'))
            {
                continue;
            }

            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2)
            {
                continue;
            }

            var attributes = fields.Skip(2)
                .Select(field => field.Split(':', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);

            devices.Add(new AdbDevice(
                fields[0],
                ParseStatus(fields[1]),
                GetAttribute(attributes, "product"),
                GetAttribute(attributes, "model")?.Replace('_', ' '),
                GetAttribute(attributes, "device"),
                ParseConnectionType(fields[0]),
                rawLine));
        }

        return devices;
    }

    private static AdbDeviceStatus ParseStatus(string value) => value.ToLowerInvariant() switch
    {
        "device" => AdbDeviceStatus.Device,
        "offline" => AdbDeviceStatus.Offline,
        "unauthorized" => AdbDeviceStatus.Unauthorized,
        "no" => AdbDeviceStatus.NoPermissions,
        _ => AdbDeviceStatus.Unknown
    };

    private static AdbConnectionType ParseConnectionType(string serialNumber) =>
        serialNumber.Contains(':', StringComparison.Ordinal) ? AdbConnectionType.Network : AdbConnectionType.Usb;

    private static string? GetAttribute(IReadOnlyDictionary<string, string> attributes, string key) =>
        attributes.TryGetValue(key, out var value) ? value : null;
}
