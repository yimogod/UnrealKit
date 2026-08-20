using UnrealKit.Core.Projects;

namespace UnrealKit.Core.Download;

/// <summary>
/// 本地已下载的构建包。Android 为单个 .apk，Win64 为整包目录。
/// <see cref="LocalApkPath"/> 为 null 表示该包不可安装到设备，
/// <see cref="InstallBlockReason"/> 说明不可安装的具体原因，供界面如实呈现。
/// </summary>
public sealed record DownloadedPackage(
    string FolderName,
    string? LocalApkPath,
    string? InstallBlockReason)
{
    public bool IsInstallable => LocalApkPath is not null;
}

/// <summary>
/// 列出本地下载根目录下已下载的构建包。落地结构是
/// <c>Intermediate/Download/&lt;Platform&gt;/&lt;版本目录&gt;</c>，
/// 每个版本目录对应一次 FTP 下载。该目录是构建缓存、可重新获取，这里只读列出、绝不修改。
///
/// 与 FTP 端一致，目录按自然排序升序返回，调用方据此取「最新」（最后一个）。
/// </summary>
public static class LocalDownloadCatalog
{
    public static IReadOnlyList<DownloadedPackage> List(string localBaseDirectory, TargetPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(localBaseDirectory);

        if (!Directory.Exists(localBaseDirectory))
        {
            return [];
        }

        var packages = new List<DownloadedPackage>();
        foreach (var directory in Directory.EnumerateDirectories(localBaseDirectory))
        {
            var folderName = Path.GetFileName(directory);
            if (string.IsNullOrWhiteSpace(folderName))
            {
                continue;
            }

            packages.Add(platform == TargetPlatform.Android
                ? InspectAndroidPackage(folderName, directory)
                : new DownloadedPackage(folderName, null, "Win64 为整包目录，不支持安装到设备。"));
        }

        return packages
            .OrderBy(package => package.FolderName, NaturalSortComparer.Instance)
            .ToArray();
    }

    private static DownloadedPackage InspectAndroidPackage(string folderName, string directory)
    {
        var apks = Directory.EnumerateFiles(directory, "*.apk", SearchOption.TopDirectoryOnly).ToArray();
        return apks.Length switch
        {
            1 => new DownloadedPackage(folderName, apks[0], null),
            0 => new DownloadedPackage(folderName, null, "目录中没有 .apk 文件。"),
            _ => new DownloadedPackage(folderName, null, $"目录中有 {apks.Length} 个 .apk，无法确定安装哪一个。")
        };
    }
}
