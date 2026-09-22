using System.ComponentModel;
using System.IO;
using UnrealKit.Core.ActorControl;
using UnrealKit.Core.Console;
using UnrealKit.Core.Projects;

namespace UnrealKit.Desktop.Models;

public sealed record LaunchOperationTarget(string SerialNumber, string PackageName, string Activity, string RemoteCommandLinePath);

public sealed record MemInfoMetricOption(string Name, string Pss, string Rss);

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

public sealed record MemReportTextureOption(
    string CookedSize, int? DiskSizeKb, string InMemSize, int? MemSizeKb,
    string Format, string LodGroup, string Name,
    string Streaming, string UnknownRef, string Vt,
    string UsageCount, string NumMips, string Compressed);

public sealed record MemReportTextureStatOption(string Label, string InMemMb, string OnDiskMb);

public sealed class LaunchParameterPresetOption(LaunchParameterPreset preset, LaunchParameterPresetGroup? group = null) : INotifyPropertyChanged
{
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Name => preset.Name;
    public string Arguments => preset.Arguments;
    public string Description => preset.Description;

    /// <summary>预设所属分组；未分组为 null。</summary>
    public string? GroupName => group?.Name;

    /// <summary>是否处于互斥组（同组最多选一个）。</summary>
    public bool IsExclusive => group?.Mode == LaunchParameterGroupMode.Exclusive;

    /// <summary>分组说明；未分组为空，界面据此决定是否显示。</summary>
    public string GroupLabel => group is null
        ? string.Empty
        : group.Mode == LaunchParameterGroupMode.Exclusive
            ? $"互斥组：{group.Name}"
            : $"同组：{group.Name}";

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

/// <summary>
/// 一条控制台预设指令的界面投影。
///
/// 三种类型共用一个类而不是分三个：列表要按分组混排显示，分成三个类型就得三个集合，
/// 分组标签会被切碎。界面用 <see cref="Kind"/> 上的 DataTrigger 切换控件。
/// </summary>
public sealed class ConsoleCommandPresetOption(ConsoleCommandPreset preset) : INotifyPropertyChanged
{
    private bool _isChecked;
    private string _value = preset.DefaultValue ?? string.Empty;
    private string _currentValueDisplay = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>底层预设，命令处理时据此合成指令文本与读回参数。</summary>
    public ConsoleCommandPreset Preset => preset;

    public string Name => preset.Name;
    public ConsoleCommandKind Kind => preset.Kind;
    public string Group => preset.Group;
    public string Description => preset.Description;
    public bool SupportsReadBack => preset.SupportsReadBack;

    /// <summary>Bool 型的目标状态，界面上的复选框。</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set => Set(ref _isChecked, value, nameof(IsChecked));
    }

    /// <summary>Value 型的输入值，初值取预设的 DefaultValue。</summary>
    public string Value
    {
        get => _value;
        set => Set(ref _value, value ?? string.Empty, nameof(Value));
    }

    /// <summary>
    /// 从 UE 读回的当前值，或读取失败的原因。
    /// 与 <see cref="Value"/> 分开：输入框里是「要设成什么」，这里是「现在是什么」，
    /// 合成一个字段会让用户改了输入框就看不到改之前的值。
    /// </summary>
    public string CurrentValueDisplay
    {
        get => _currentValueDisplay;
        private set => Set(ref _currentValueDisplay, value, nameof(CurrentValueDisplay));
    }

    /// <summary>Bool/Value 型显示当前值一栏；Action 型没有当前值。</summary>
    public bool ShowsCurrentValue => preset.SupportsReadBack;

    /// <summary>按钮文案：Action 是执行一次，Bool/Value 是把值写下去。</summary>
    public string ActionLabel => preset.Kind == ConsoleCommandKind.Action ? "运行" : "应用";

    /// <summary>
    /// 写入读回结果。Bool 型同时把复选框同步到实际值，让界面显示的是引擎当前状态，
    /// 而不是上次点击留下的目标状态。
    /// </summary>
    public void ApplyReadBack(ConsoleVariableValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        CurrentValueDisplay = value.Display;

        if (value is { Succeeded: true, BoolValue: { } flag })
        {
            IsChecked = flag;
        }
    }

    /// <summary>写入一条读取失败或校验失败的说明。</summary>
    public void SetStatus(string message) => CurrentValueDisplay = message;

    private void Set<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record ScpFrameOption(int Index, string CameraName, string FrameTimeMs, string GameTimeMs, string DrawTimeMs, string RhiTimeMs, string GpuTimeMs, string MemoryBytes, string DrawCalls, string Triangles, int Screenshots, int Line);

public sealed record ScpAverageOption(string FrameTimeMs, string GameTimeMs, string DrawTimeMs, string RhiTimeMs, string GpuTimeMs, string MemoryBytes, string DrawCalls, string Triangles);

public sealed record ScpDiagnosticOption(string Severity, string Code, string Line, string Message);

public sealed record RenderDocDiagnosticOption(string Severity, string Code, string Line, string Message);

/// <summary>
/// 本地已下载构建包的展示投影。安装到设备前据此取 <see cref="LocalApkPath"/>；
/// <see cref="InstallBlockReason"/> 说明该包不可安装的具体原因。
/// </summary>
public sealed record DownloadedPackageOption(string FolderName, string? LocalApkPath, string? InstallBlockReason)
{
    /// <summary>该包是否可直接安装（有唯一本地 APK 且无阻塞原因）。</summary>
    public bool IsInstallable => LocalApkPath is not null;

    /// <summary>列表展示的说明：有 APK 时在目录名后追加文件名；不可安装的再追加原因。</summary>
    public string Display
    {
        get
        {
            var apkName = LocalApkPath is not null ? Path.GetFileName(LocalApkPath) : null;
            var label = apkName is not null ? $"{FolderName} / {apkName}" : FolderName;
            return InstallBlockReason is null ? label : $"{label}（{InstallBlockReason}）";
        }
    }
}

public sealed record PakScanTextureOption(
    string Name, string Path, string SizeX, string SizeY,
    string Format, string LodBias, string LodGroup,
    string NumMips, string EstimatedSizeMB, string PakChunkId);

public sealed record PakScanStaticMeshOption(
    string Name, string Path,
    string LodCount, string MaterialCount,
    string VertexCount, string TriangleCount, string PakChunkId);

public sealed record PakScanSkeletalMeshOption(
    string Name, string Path,
    string LodCount, string MaterialCount, string BoneCount,
    string VertexCount, string TriangleCount, string PakChunkId);

public sealed record PakScanMaterialOption(
    string Name, string Path,
    string BlendMode, string ShadingModel,
    string ReferencedTextureCount, string TwoSided,
    string PakChunkId);

public sealed record PakScanDiagnosticOption(string Severity, string Code, string Message);

/// <summary>
/// 本地已下载 Pak 包的展示投影。<see cref="FolderName"/> 是版本目录名，
/// 与同构建号的安装包目录名一致。
/// </summary>
public sealed record LocalPakPackageOption(string FolderName, string LocalDirectory)
{
    public string Display => FolderName;
}

public sealed record PakMapMeshUsageOption(
    string MapName, string MapPath, string MeshName, string MeshPath, string Count);

public sealed record PakMapMeshAggregateOption(
    string MeshName, string MeshPath, string TotalCount, string MapCount);

/// <summary>
/// 一条相机预设的界面投影。封装底层 <see cref="CameraPreset"/>，
/// 供 CameraView 的 ListBox 绑定。
/// </summary>
public sealed record CameraPresetOption(CameraPreset Preset)
{
    public string Name => Preset.Name;
    public string MapName => Preset.MapName;

    /// <summary>坐标摘要（只读展示）。</summary>
    public string PositionSummary =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"X={Preset.X:F0}  Y={Preset.Y:F0}  Z={Preset.Z:F0}");

    /// <summary>旋转摘要（只读展示）。</summary>
    public string RotationSummary =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"P={Preset.Pitch:F1}  Y={Preset.Yaw:F1}  R={Preset.Roll:F1}");
}

/// <summary>运行中 Actor 的界面投影。ObjectPath 是 Remote Control 调用的唯一标识。</summary>
public sealed class RuntimeActorOption : INotifyPropertyChanged
{
    private bool _isHidden;

    public RuntimeActorOption(RuntimeActorEntry actor)
    {
        ObjectPath = actor.ObjectPath;
        ClassName = actor.ClassName;
        Name = actor.ObjectPath[(actor.ObjectPath.LastIndexOf('.') + 1)..];
        NumKb = Format(actor.NumKb);
        MaxKb = Format(actor.MaxKb);
        ResExcKb = Format(actor.ResExcKb);
        ResExcDedSysKb = Format(actor.ResExcDedSysKb);
        ResExcDedVidKb = Format(actor.ResExcDedVidKb);
        ResExcUnkKb = Format(actor.ResExcUnkKb);
        LineNumber = actor.LineNumber;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ObjectPath { get; }
    public string Name { get; }
    public string ClassName { get; }
    public string NumKb { get; }
    public string MaxKb { get; }
    public string ResExcKb { get; }
    public string ResExcDedSysKb { get; }
    public string ResExcDedVidKb { get; }
    public string ResExcUnkKb { get; }
    public int LineNumber { get; }
    public string Visibility => IsHidden ? "已隐藏" : "显示";

    public bool IsHidden
    {
        get => _isHidden;
        set
        {
            if (_isHidden == value) return;
            _isHidden = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsHidden)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Visibility)));
        }
    }

    private static string Format(decimal value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
