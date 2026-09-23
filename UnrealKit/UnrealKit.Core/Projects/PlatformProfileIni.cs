namespace UnrealKit.Core.Projects;

/// <summary>
/// 平台配置与 INI 之间的映射。放在此处而不是 ProjectService 内，
/// 是为了让「新增一个平台」只需改动 PlatformProfile 及其映射，
/// 而不必翻动工程读写流程。
///
/// 节名形如 <c>[UnrealKit.Platform.Android]</c>，由 <see cref="PlatformNames"/>
/// 的稳定标识派生，属于配置格式契约。
/// </summary>
internal static class PlatformProfileIni
{
    private const string SectionPrefix = "UnrealKit.Platform.";

    internal static string SectionName(TargetPlatform platform) =>
        SectionPrefix + PlatformNames.ToName(platform);

    /// <summary>
    /// 该平台是否在工程中配置过。
    ///
    /// 只看 override 层（工程的 DefaultGame.ini），不看 base 层（BaseGame.ini）：
    /// 「本工程支持哪些平台」是工程自己的声明，若 base 层的节也算配置过，
    /// 每个工程都会看起来支持全部平台，「该平台未配置」的报错就永远不会触发。
    /// 各字段的取值仍走分层合并，因此 BaseGame.ini 可以提供组织级默认值（如 AdbPath）。
    /// </summary>
    internal static bool IsConfigured(LayeredIniDocument document, TargetPlatform platform) =>
        document.Override.HasSection(SectionName(platform));

    /// <summary>
    /// 读取该平台配置。未配置时返回 null——调用方据此报「该平台未配置」，
    /// 不能用一份全默认值的 profile 冒充已配置。
    ///
    /// 返回类型由 <typeparamref name="TProfile"/> 固定：若平台与 profile 类型对不上，
    /// 立即抛出而不是静默返回 null，否则一个映射错误会表现为「用户没配这个平台」。
    /// </summary>
    internal static TProfile? Read<TProfile>(LayeredIniDocument document, TargetPlatform platform)
        where TProfile : PlatformProfile
    {
        if (!IsConfigured(document, platform))
        {
            return null;
        }

        var section = SectionName(platform);
        string Value(string key, string fallback) =>
            document.GetValue(section, key) is { Length: > 0 } value ? value : fallback;

        PlatformProfile profile = platform switch
        {
            TargetPlatform.Android => ReadAndroid(Value, document, section),
            TargetPlatform.Win64 => ReadWin64(Value, document, section),
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported platform.")
        };

        return profile as TProfile
            ?? throw new InvalidOperationException(
                $"平台 {PlatformNames.ToName(platform)} 的配置类型是 {profile.GetType().Name}，与请求的 {typeof(TProfile).Name} 不符。");
    }

    /// <summary>写入该平台配置。</summary>
    internal static void Write(IniDocument document, PlatformProfile profile)
    {
        var section = SectionName(profile.Platform);
        switch (profile)
        {
            case AndroidPlatformProfile android:
                document.SetValue(section, "PackageName", android.PackageName);
                document.SetValue(section, "Activity", android.Activity);
                document.SetValue(section, "GameRoot", android.GameRoot);
                document.SetValue(section, "AdbPath", android.AdbPath);
                document.SetValue(section, "FtpPath", android.FtpPath);
                document.SetValue(section, "PakFtpPath", android.PakFtpPath);
                document.SetValue(section, "PakAesKey", android.PakAesKey);
                WritePakVersionOverrides(document, section, android.PakVersionOverrides);
                break;

            case Win64PlatformProfile win64:
                document.SetValue(section, "PackageName", win64.PackageName);
                document.SetValue(section, "FtpPath", win64.FtpPath);
                document.SetValue(section, "PakFtpPath", win64.PakFtpPath);
                document.SetValue(section, "PakAesKey", win64.PakAesKey);
                WritePakVersionOverrides(document, section, win64.PakVersionOverrides);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(profile), profile.Platform, "该平台配置尚未实现 INI 映射。");
        }
    }

    private const string PakVersionOverridePrefix = "PakVersionOverride.";

    private static void WritePakVersionOverrides(
        IniDocument document, string section, IReadOnlyDictionary<string, bool>? overrides)
    {
        if (overrides is null) return;
        foreach (var (key, value) in overrides)
            document.SetValue(section, PakVersionOverridePrefix + key, value ? "True" : "False");
    }

    private static IReadOnlyDictionary<string, bool>? ReadPakVersionOverrides(
        LayeredIniDocument document, string section)
    {
        var all = document.GetSection(section);
        Dictionary<string, bool>? result = null;
        foreach (var (key, value) in all)
        {
            if (!key.StartsWith(PakVersionOverridePrefix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!bool.TryParse(value, out var flag))
                continue;
            result ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            result[key[PakVersionOverridePrefix.Length..]] = flag;
        }
        return result;
    }

    private static AndroidPlatformProfile ReadAndroid(
        Func<string, string, string> value, LayeredIniDocument document, string section)
    {
        var defaults = AndroidPlatformProfile.CreateDefaults();
        return new AndroidPlatformProfile(
            PackageName: value("PackageName", defaults.PackageName),
            Activity: value("Activity", defaults.Activity),
            GameRoot: value("GameRoot", defaults.GameRoot),
            AdbPath: value("AdbPath", defaults.AdbPath),
            FtpPath: value("FtpPath", defaults.FtpPath),
            PakFtpPath: value("PakFtpPath", defaults.PakFtpPath),
            PakAesKey: value("PakAesKey", defaults.PakAesKey),
            PakVersionOverrides: ReadPakVersionOverrides(document, section));
    }

    private static Win64PlatformProfile ReadWin64(
        Func<string, string, string> value, LayeredIniDocument document, string section)
    {
        var defaults = Win64PlatformProfile.CreateDefaults();
        return new Win64PlatformProfile(
            PackageName: value("PackageName", defaults.PackageName),
            FtpPath: value("FtpPath", defaults.FtpPath),
            PakFtpPath: value("PakFtpPath", defaults.PakFtpPath),
            PakAesKey: value("PakAesKey", defaults.PakAesKey),
            PakVersionOverrides: ReadPakVersionOverrides(document, section));
    }
}
