namespace UnrealKit.Core.Runtime;

/// <summary>
/// 软件级设置：不属于任何一个工程、跟着这份 UnrealKit 安装走的状态，目前是上次打开的工程。
///
/// 「上次打开哪个工程」显然不能存在工程里——要先知道打开哪个工程才能去读它的配置。
/// 因此它落在软件自己的 <c>Config/EditorSetting.ini</c>，与工程内的
/// <see cref="IUserSettingStore"/> 分工明确：前者定位工程，后者记录工程内的选择。
/// </summary>
public interface IEditorSettingStore
{
    /// <summary>
    /// 读取上次打开的工程路径。没有记录或状态文件不可读时返回 <c>null</c>，
    /// 不猜测某个「默认工程」。
    /// </summary>
    Task<string?> TryGetLastProjectFilePathAsync(CancellationToken cancellationToken = default);

    /// <summary>记录当前工程路径，覆盖上一条记录。</summary>
    Task SaveLastProjectFilePathAsync(string projectFilePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// 基于 INI 的软件级设置实现，默认写入 <c>&lt;程序目录&gt;\Config\EditorSetting.ini</c>。
/// </summary>
public sealed class EditorSettingStore : IEditorSettingStore
{
    private const string RecentProjectSection = "UnrealKit.RecentProject";
    private const string LastProjectKey = "LastProjectFilePath";

    private readonly string _settingFilePath;

    public EditorSettingStore(string? settingFilePath = null)
    {
        _settingFilePath = settingFilePath is { Length: > 0 } path
            ? Path.GetFullPath(path)
            : DefaultSettingFilePath;
    }

    public static string DefaultSettingFilePath => Path.Combine(ApplicationPaths.AppConfigDir, "EditorSetting.ini");

    public string SettingFilePath => _settingFilePath;

    public async Task<string?> TryGetLastProjectFilePathAsync(CancellationToken cancellationToken = default)
    {
        var value = await IniStateFile.TryReadValueAsync(_settingFilePath, RecentProjectSection, LastProjectKey, cancellationToken);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public Task SaveLastProjectFilePathAsync(string projectFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);
        return IniStateFile.WriteValueAsync(
            _settingFilePath, RecentProjectSection, LastProjectKey,
            Path.GetFullPath(projectFilePath.Trim()),
            cancellationToken);
    }
}
