namespace UnrealKit.Core.Projects;

/// <summary>
/// <c>Config/DefaultGame.ini</c> 的布局版本。
///
/// v1 把平台配置平铺在 <c>[UnrealKit.ProjectSettings]</c> 下，并用一个 <c>Platform</c>
/// 字段表示「当前平台」，因此一个工程只能配置一个平台。v2 改为每平台一个
/// <c>[UnrealKit.Platform.*]</c> 节，多平台并存，「当前平台」由所选设备决定。
///
/// 两种布局不做自动迁移：v1 的 <c>Platform=Win64</c> 说明另一个平台的字段从未被填写过，
/// 迁移只能凭猜测补全，而猜错的设备端路径会让采集拉到空目录却报告成功。
/// 这里显式失败并给出改法，让用户自己确认每个平台的配置。
/// </summary>
public static class ProjectSettingsFormat
{
    /// <summary>当前布局版本。</summary>
    public const int CurrentVersion = 2;

    /// <summary>
    /// 校验工程配置的布局版本。
    /// </summary>
    /// <param name="rawVersion">工程 DefaultGame.ini 中 SettingsVersion 的原始值，缺失时为 null。</param>
    /// <param name="hasSettingsSection">工程 DefaultGame.ini 是否含有设置节。</param>
    /// <param name="path">配置文件路径，用于错误提示。</param>
    public static void RequireSupportedVersion(string? rawVersion, bool hasSettingsSection, string path)
    {
        // 完全没有设置节：全新或纯默认值工程，按当前版本处理，不算旧格式。
        if (!hasSettingsSection)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            throw new InvalidDataException(
                $"工程配置缺少 SettingsVersion，按 v1 旧布局处理: {path}{Environment.NewLine}" +
                BuildMigrationHelp());
        }

        if (!int.TryParse(rawVersion, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var version))
        {
            throw new InvalidDataException(
                $"工程配置的 SettingsVersion 不是整数: '{rawVersion}' ({path})。当前支持的版本为 {CurrentVersion}。");
        }

        if (version == CurrentVersion)
        {
            return;
        }

        throw new InvalidDataException(version < CurrentVersion
            ? $"工程配置使用旧布局版本 {version}，当前版本为 {CurrentVersion}: {path}{Environment.NewLine}{BuildMigrationHelp()}"
            : $"工程配置使用更高的布局版本 {version}，当前版本为 {CurrentVersion}: {path}{Environment.NewLine}" +
              "请升级 UnrealKit，或改用与该配置匹配的版本。");
    }

    private static string BuildMigrationHelp() =>
        $"""
         v{CurrentVersion} 将平台配置按平台分节，同一工程可同时配置多个平台。请按下例手工改写
         Config/DefaultGame.ini，只填写本工程实际会跑的平台：

         [UnrealKit.ProjectSettings]
         SettingsVersion={CurrentVersion}
         UnrealProjectName=<UE 工程名>

         [{PlatformProfileIni.SectionName(TargetPlatform.Android)}]
         PackageName=<原 PackageName>
         Activity=<原 Activity>
         GameRootTemplate=<原 DeviceGameRootTemplate>
         AdbPath=<原 AdbPath>

         [{PlatformProfileIni.SectionName(TargetPlatform.Win64)}]
         Executable=<原 Win64Executable>
         WorkingDirectory=<原 Win64WorkingDirectory>

         原 Platform 字段已移除：本次操作用哪个平台由所选设备决定，不再写进工程配置。
         原 DeviceSavedRootTemplate 也已移除：Saved 目录固定是 Game 目录下的 Saved 子目录。
         其余字段（DefaultCaptureTag、DefaultExportDirectory、RemoteControl* 等）位置不变。
         """;
}
