using UnrealKit.Core.Projects;

namespace UnrealKit.Core.Runtime;

/// <summary>
/// 用户级界面状态：上次打开的工程、上次选择的平台作用域。
///
/// 这些是**用户级**状态而非工程配置，因此不写进 <c>.ukit</c> 或
/// <c>Config/DefaultGame.ini</c>，落在用户目录下，换工程不会互相覆盖，
/// 工程目录也不会因为「谁上次开着哪个页面」而产生 diff。
/// </summary>
public interface IUserStateStore
{
    /// <summary>
    /// 读取上次打开的工程路径。没有记录或状态文件不可读时返回 <c>null</c>，
    /// 不猜测某个「默认工程」。
    /// </summary>
    Task<string?> TryGetLastProjectFilePathAsync(CancellationToken cancellationToken = default);

    /// <summary>记录当前工程路径，覆盖上一条记录。</summary>
    Task SaveLastProjectFilePathAsync(string projectFilePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取上次选择的平台作用域。没有记录或取值无法识别时返回
    /// <see cref="PlatformScope.All"/>——陈旧记录不应让界面聚焦到用户没选过的平台。
    /// </summary>
    Task<PlatformScope> GetPlatformScopeAsync(CancellationToken cancellationToken = default);

    /// <summary>记录当前平台作用域，覆盖上一条记录。</summary>
    Task SavePlatformScopeAsync(PlatformScope scope, CancellationToken cancellationToken = default);
}

/// <summary>
/// 基于 INI 的用户状态实现，默认写入 <c>%LOCALAPPDATA%\UnrealKit\UserState.ini</c>。
/// </summary>
public sealed class UserStateStore : IUserStateStore
{
    private const string RecentProjectSection = "UnrealKit.RecentProject";
    private const string LastProjectKey = "LastProjectFilePath";
    private const string ScopeSection = "UnrealKit.Scope";
    private const string PlatformScopeKey = "Platform";

    private readonly string _stateFilePath;

    public UserStateStore(string? stateFilePath = null)
    {
        _stateFilePath = stateFilePath is { Length: > 0 } path
            ? Path.GetFullPath(path)
            : DefaultStateFilePath;
    }

    public static string DefaultStateFilePath => Path.Combine(ApplicationPaths.UserStateDir, "UserState.ini");

    public string StateFilePath => _stateFilePath;

    public async Task<string?> TryGetLastProjectFilePathAsync(CancellationToken cancellationToken = default)
    {
        var value = await TryReadValueAsync(RecentProjectSection, LastProjectKey, cancellationToken);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public Task SaveLastProjectFilePathAsync(string projectFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);
        return WriteValueAsync(
            RecentProjectSection, LastProjectKey,
            Path.GetFullPath(projectFilePath.Trim()),
            cancellationToken);
    }

    public async Task<PlatformScope> GetPlatformScopeAsync(CancellationToken cancellationToken = default)
    {
        var value = await TryReadValueAsync(ScopeSection, PlatformScopeKey, cancellationToken);

        // 无法识别的取值回落到「全部」而不是抛出：状态文件是便利设施，
        // 手工改坏它不该让应用起不来，而「全部」不会隐藏任何设备或归档。
        PlatformScope.TryParse(value, out var scope);
        return scope;
    }

    public Task SavePlatformScopeAsync(PlatformScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return WriteValueAsync(ScopeSection, PlatformScopeKey, scope.Name, cancellationToken);
    }

    private async Task<string?> TryReadValueAsync(string section, string key, CancellationToken cancellationToken)
    {
        if (!File.Exists(_stateFilePath))
        {
            return null;
        }

        // 状态文件损坏不应阻止应用启动：读不出记录就当作没有记录。
        try
        {
            var content = await File.ReadAllTextAsync(_stateFilePath, cancellationToken);
            return IniDocument.Parse(content).GetValue(section, key);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task WriteValueAsync(string section, string key, string value, CancellationToken cancellationToken)
    {
        // 保留状态文件里的其它键：两项状态共用一个文件，写其中一项不能抹掉另一项。
        var document = File.Exists(_stateFilePath)
            ? ParseOrEmpty(await File.ReadAllTextAsync(_stateFilePath, cancellationToken))
            : new IniDocument();

        document.SetValue(section, key, value);
        var directory = Path.GetDirectoryName(_stateFilePath);
        if (directory is { Length: > 0 })
        {
            Directory.CreateDirectory(directory);
        }

        await document.SaveAsync(_stateFilePath, cancellationToken);
    }

    private static IniDocument ParseOrEmpty(string content)
    {
        try
        {
            return IniDocument.Parse(content);
        }
        catch (Exception)
        {
            return new IniDocument();
        }
    }
}
