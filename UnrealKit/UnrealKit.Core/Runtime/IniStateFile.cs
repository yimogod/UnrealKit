using UnrealKit.Core.Projects;

namespace UnrealKit.Core.Runtime;

/// <summary>
/// 单文件 INI 状态的读写。<c>EditorSetting.ini</c> 与 <c>UserSetting.ini</c> 共用同一套规则：
/// 读不出来就当作没有记录，写入时保留文件里的其它节。两处各写一份实现会让「状态文件损坏
/// 不阻塞启动」这条只在其中一处成立。
/// </summary>
internal static class IniStateFile
{
    /// <summary>
    /// 读取一项状态。文件不存在或读不出来都返回 <c>null</c>——状态文件是便利设施，
    /// 手工改坏它不该让应用起不来。
    /// </summary>
    public static async Task<string?> TryReadValueAsync(
        string path, string section, string key, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return IniDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken)).GetValue(section, key);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// 写入一项状态，保留文件中已有的其它节与键。读取现有内容时不吞异常：
    /// 读不出来就整份覆盖会连带抹掉用户手写的其它配置，宁可让调用方看到失败。
    /// </summary>
    public static async Task WriteValueAsync(
        string path, string section, string key, string value, CancellationToken cancellationToken)
    {
        var document = File.Exists(path)
            ? IniDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken))
            : new IniDocument();

        document.SetValue(section, key, value);

        var directory = Path.GetDirectoryName(path);
        if (directory is { Length: > 0 })
        {
            Directory.CreateDirectory(directory);
        }

        await document.SaveAsync(path, cancellationToken);
    }
}
