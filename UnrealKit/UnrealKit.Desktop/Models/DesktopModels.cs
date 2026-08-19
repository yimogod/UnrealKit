using System.ComponentModel;
using UnrealKit.Core.Projects;

namespace UnrealKit.Desktop.Models;

public sealed record LaunchOperationTarget(string SerialNumber, string PackageName, string Activity, string RemoteCommandLinePath);

public sealed record MemInfoMetricOption(string Name, string Value);

public sealed record MemInfoPssOption(string Name, string TotalPss, string PrivateDirty, string PrivateClean, string SwapPss, string Rss, string HeapSize, string HeapAlloc, string HeapFree, string Line);

public sealed record MemInfoNamedEntryOption(string Name, string Value, string Line);

public sealed record MemInfoDiagnosticOption(string Severity, string Code, string Line, string Message);

/// <summary>
/// 一条操作日志。时间戳由 <c>ShellViewModel.AddOperationLog</c> 统一打，
/// 调用方只提供分类与正文，避免各处各自格式化导致时间戳格式不一或重复。
/// </summary>
public sealed record OperationLogEntry(DateTimeOffset Timestamp, string Category, string Message)
{
    public string Time => Timestamp.ToString("HH:mm:ss");

    /// <summary>保存到文本文件时的单行格式。</summary>
    public override string ToString() => $"{Timestamp:yyyy-MM-dd HH:mm:ss} [{Category}] {Message}";
}

public sealed record MemReportMetricOption(string Group, string Name, string Value, string Status);

public sealed record MemReportSummaryOption(string Category, string Count, string Details);

public sealed class LaunchParameterPresetOption(LaunchParameterPreset preset) : INotifyPropertyChanged
{
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Name => preset.Name;
    public string Arguments => preset.Arguments;
    public string Description => preset.Description;
    public string DisplayText => preset.DisplayText;
    public bool IsComposable => preset.IsComposable;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}

public sealed record ScpFrameOption(int Index, string CameraName, string FrameTimeMs, string GameTimeMs, string DrawTimeMs, string RhiTimeMs, string GpuTimeMs, string MemoryBytes, string DrawCalls, string Triangles, int Screenshots, int Line);

public sealed record ScpAverageOption(string FrameTimeMs, string GameTimeMs, string DrawTimeMs, string RhiTimeMs, string GpuTimeMs, string MemoryBytes, string DrawCalls, string Triangles);

public sealed record ScpDiagnosticOption(string Severity, string Code, string Line, string Message);

public sealed record DiffResultOption(string Group, string Name, string Unit, string Direction, string BaselineValue, string CurrentValue, string Delta, string DeltaPercent, string Status, string Assessment);

public sealed record DiffDiagnosticOption(string Severity, string Code, string Line, string Message);

public sealed record TrendCaptureOption(string CaptureId, string CaptureDate, string Platform, string Tag, string DeviceModel);

public sealed record TrendSeriesOption(string Group, string Name, string Unit, string Direction, int Points, int Present, int Missing, string Min, string Max, string Avg, string First, string Last, string TotalDelta, string TotalDeltaPercent, string Assessment);

public sealed record TrendDiagnosticOption(string Severity, string Code, string Line, string Message);

public sealed record RenderDocDiagnosticOption(string Severity, string Code, string Line, string Message);

public sealed record TrendChartAxisLabel(double X, double Y, string Label);