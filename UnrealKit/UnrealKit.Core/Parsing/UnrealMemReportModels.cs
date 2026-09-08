using UnrealKit.Core.Diagnostics;

namespace UnrealKit.Core.Parsing;

public enum UnrealMemReportMetricStatus
{
    Parsed,
    Missing,
    Invalid
}

public sealed record UnrealMemReportMetric(
    string Group,
    string Name,
    long? ValueKb,
    string? RawValue,
    UnrealMemReportMetricStatus Status,
    int? LineNumber);

public sealed record UnrealMemReportSummary(IReadOnlyList<UnrealMemReportMetric> Metrics);

public sealed record UnrealMemReportTexture(
    string Name,
    int? Width,
    int? Height,
    string? Format,
    long? MemoryKb,
    string RawLine,
    int LineNumber);

public sealed record UnrealMemReportRenderTarget(
    string Name,
    int? Width,
    int? Height,
    string? Format,
    long? MemoryKb,
    string RawLine,
    int LineNumber);

public sealed record UnrealMemReportObject(
    string ClassName,
    long? Count,
    long? MemoryKb,
    string RawLine,
    int LineNumber);

// Full per-texture row from ListTextures command block.
public sealed record UnrealMemReportTextureDetail(
    string CookedWidth, string CookedHeight, string CookedSizeKb, string CookedBias,
    string InMemWidth, string InMemHeight, string InMemSizeKb,
    string Format, string LodGroup, string Name,
    string Streaming, string UnknownRef, string Vt,
    string UsageCount, string NumMips, string Uncompressed,
    string RawLine, int LineNumber);

// One "Total PF_* size" or "Total TEXTUREGROUP_* size" stats line.
public sealed record UnrealMemReportTextureStat(
    string Label, string InMemMb, string OnDiskMb, string RawLine, int LineNumber);

public sealed record UnrealMemReport(
    string Changelist,
    UnrealMemReportSummary Summary,
    IReadOnlyList<UnrealMemReportTexture> Textures,
    IReadOnlyList<UnrealMemReportRenderTarget> RenderTargets,
    IReadOnlyList<UnrealMemReportObject> Objects,
    IReadOnlyList<UnrealMemReportTextureDetail> TextureDetails,
    IReadOnlyList<UnrealMemReportTextureStat> TextureStats);

public sealed record UnrealMemReportParseResult(
    string InputPath,
    UnrealMemReport? Report,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsSuccess => Report is not null && Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}
