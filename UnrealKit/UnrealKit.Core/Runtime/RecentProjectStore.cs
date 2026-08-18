using UnrealKit.Core.Projects;

namespace UnrealKit.Core.Runtime;

/// <summary>
/// 记录「上次打开的工程」。这是用户级状态而非工程配置，因此不写进 <c>.ukit</c> 或
/// <c>Config/DefaultGame.ini</c>，落在用户目录下，换工程不会互相覆盖。
/// </summary>
public interface IRecentProjectStore
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
/// 基于 INI 的用户状态实现，默认写入 <c>%LOCALAPPDATA%\UnrealKit\UserState.ini</c>。
/// </summary>
public sealed class RecentProjectStore : IRecentProjectStore
{
    private const string Section = "UnrealKit.RecentProject";
    private const string LastProjectKey = "LastProjectFilePath";

    private readonly string _stateFilePath;

    public RecentProjectStore(string? stateFilePath = null)
    {
        _stateFilePath = stateFilePath is { Length: > 0 } path
            ? Path.GetFullPath(path)
            : DefaultStateFilePath;
    }

    public static string DefaultStateFilePath => Path.Combine(ApplicationPaths.UserStateDir, "UserState.ini");

    public string StateFilePath => _stateFilePath;

    public async Task<string?> TryGetLastProjectFilePathAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_stateFilePath))
        {
            return null;
        }

        // 状态文件损坏不应阻止应用启动：读不出记录就当作没有记录，
        // 用户仍可从菜单新建或打开工程。
        try
        {
            var content = await File.ReadAllTextAsync(_stateFilePath, cancellationToken);
            var value = IniDocument.Parse(content).GetValue(Section, LastProjectKey);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task SaveLastProjectFilePathAsync(string projectFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);

        // 保留状态文件里的其它键，未来新增用户级状态时不会被这次写入抹掉。
        var document = File.Exists(_stateFilePath)
            ? ParseOrEmpty(await File.ReadAllTextAsync(_stateFilePath, cancellationToken))
            : new IniDocument();

        document.SetValue(Section, LastProjectKey, Path.GetFullPath(projectFilePath.Trim()));
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
