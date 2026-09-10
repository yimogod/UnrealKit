using UnrealKit.Core.Diagnostics;
using UnrealKit.Core.Projects;

namespace UnrealKit.Core.Download;

/// <summary>
/// FTP 目录中的一条条目。只有名字与「是否是目录」参与下载决策，
/// 其余元数据（大小、时间）不进入流程，保持抽象最小以便假客户端单测。
/// </summary>
public sealed record FtpEntry(string Name, bool IsDirectory);

/// <summary>
/// 控制 FTP 下载的文件策略：<see cref="Apk"/> 在子目录中精确找唯一 .apk，
/// <see cref="Directory"/> 递归下载整个子目录（适用于 Win64 构建包和 Pak 资产包）。
/// </summary>
public enum DownloadMode
{
    Apk,
    Directory,
}

/// <summary>
/// 一次 FTP 下载请求。<see cref="FtpPath"/> 是该平台在 FTP 服务器上的父目录，
/// <see cref="LocalBaseDirectory"/> 是本地落地根目录（<c>Intermediate/Download/&lt;Platform&gt;</c>）。
/// <see cref="Mode"/> 控制下载策略，默认 <see cref="DownloadMode.Apk"/>（向后兼容）。
/// </summary>
public sealed record DownloadRequest(
    TargetPlatform Platform,
    FtpSettings Settings,
    string FtpPath,
    string LocalBaseDirectory,
    DownloadMode Mode = DownloadMode.Apk);

/// <summary>
/// 下载结果。<see cref="LocalPath"/> 是落地的文件（Android APK）或目录（Win64 整包）。
/// 诊断含 <c>DWN*</c> 码；存在 Error 级诊断即视为失败。
/// </summary>
public sealed record DownloadResult(
    string? LocalPath,
    string? SourceSubdir,
    int FileCount,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Succeeded => Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

/// <summary>
/// 下载模块的诊断码（<c>DWN*</c> 前缀）。一经发布只向后追加，不复用、不改语义。
/// </summary>
public static class DownloadDiagnosticCodes
{
    /// <summary>FTP 父目录下没有可下载的子目录。</summary>
    public const string NoSubdirectory = "DWN001";

    /// <summary>连接 FTP 服务器失败。</summary>
    public const string ConnectFailed = "DWN002";

    /// <summary>列出 FTP 目录失败。</summary>
    public const string ListFailed = "DWN003";

    /// <summary>最新子目录中存在多个 .apk，无法确定安装哪一个。</summary>
    public const string AmbiguousApk = "DWN004";

    /// <summary>最新子目录中没有 .apk 文件。</summary>
    public const string ApkNotFound = "DWN005";

    /// <summary>下载文件或目录失败。</summary>
    public const string DownloadFailed = "DWN006";

    /// <summary>最新构建已在本地存在，本次未重新下载。</summary>
    public const string AlreadyUpToDate = "DWN007";
}
