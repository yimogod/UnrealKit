namespace UnrealKit.Core.PakScan;

public sealed record PakScanConfig
{
    public static readonly PakScanConfig Default = new();

    public string GameVersion { get; init; } = "GAME_UE5_6";

    public string AesKey { get; init; } = string.Empty;

    public string OodleDllPath { get; init; } = string.Empty;

    public bool ExcludeEnginePaths { get; init; } = true;

    public long LargeSizeWarningBytes { get; init; } = 4L * 1024 * 1024;

    /// <summary>并行解析的最大线程数，0 = 使用 CPU 核心数。</summary>
    public int MaxDegreeOfParallelism { get; init; } = 0;

    /// <summary>
    /// 从可执行文件所在目录向上查找 ThirdParty/Oodle/oo2core_9_win64.dll，
    /// 未找到时返回空字符串。
    /// </summary>
    public static string ResolveDefaultOodlePath()
    {
        const string relativePath = "ThirdParty/Oodle/oo2core_9_win64.dll";

        var start = AppContext.BaseDirectory;
        var dir = new System.IO.DirectoryInfo(start);

        // 向上最多查 6 层（exe → bin/Debug/net10.0 → bin/Debug → bin → 项目 → 解决方案）
        for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, relativePath);
            if (System.IO.File.Exists(candidate))
                return candidate;
        }

        return string.Empty;
    }
}
