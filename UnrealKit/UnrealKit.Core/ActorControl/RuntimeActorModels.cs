using UnrealKit.Core.Diagnostics;
using UnrealKit.Core.Projects;
using UnrealKit.Core.Unreal;

namespace UnrealKit.Core.ActorControl;

/// <summary>从 <c>obj list class=Actor</c> 输出中解析出的运行时 Actor。</summary>
public sealed record RuntimeActorEntry(
    string ClassName,
    string ObjectPath,
    decimal NumKb,
    decimal MaxKb,
    decimal ResExcKb,
    decimal ResExcDedSysKb,
    decimal ResExcDedVidKb,
    decimal ResExcUnkKb,
    int LineNumber);

/// <summary>一次 Actor 日志区段解析的结果。</summary>
public sealed record RuntimeActorLogParseResult(
    string InputPath,
    IReadOnlyList<RuntimeActorEntry> Actors,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsSuccess => !Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}

/// <summary>刷新 Actor 列表的请求。日志下载始终落到工程的 <c>Saved/Device</c>，不修改原始设备文件。</summary>
public sealed record RuntimeActorRefreshRequest(UkitProject Project, Devices.IDevice Device);

/// <summary>刷新 Actor 列表的结果，同时保留本次下载目录和所用日志，便于排查解析结果。</summary>
public sealed record RuntimeActorRefreshResult(
    UnrealSavedPullResult LogDownload,
    string CurrentLogPath,
    RuntimeActorLogParseResult ParseResult);

/// <summary><c>ACT</c> 域诊断码。新增编号只能向后追加。</summary>
public static class RuntimeActorDiagnosticCodes
{
    public const string ActorListSectionMissing = "ACT101";
    public const string ActorListTerminatorMissing = "ACT102";
    public const string ActorListRowInvalid = "ACT103";
}
