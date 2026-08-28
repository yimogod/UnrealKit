using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using UnrealKit.Core.Adb;
using UnrealKit.Core.Analysis;
using UnrealKit.Core.Capture;
using UnrealKit.Core.Console;
using UnrealKit.Core.Devices;
using UnrealKit.Core.Download;
using UnrealKit.Core.Export;
using UnrealKit.Core.Launch;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Parsing;
using UnrealKit.Core.Processes;
using UnrealKit.Core.Projects;
using UnrealKit.Core.RenderDoc;
using UnrealKit.Core.Runtime;
using UnrealKit.Core.Unreal;
using UnrealKit.Desktop.Models;
using UnrealKit.Desktop.Services;

namespace UnrealKit.Desktop.ViewModels;

public sealed class ShellViewModel : INotifyPropertyChanged
{
    private readonly IProjectService _projectService;
    private readonly IDesktopAdbServiceFactory _adbServiceFactory;
    private readonly IUserConfirmationService _confirmationService;
    private readonly IEditorSettingStore _editorSettingStore;
    private readonly IUserSettingStore _userSettingStore;
    // 工程与工程配置已移到菜单栏，导航首项因此是「设备」。
    private string _selectedNavigationItem = "设备";
    private string _statusMessage = "未打开工程。请从菜单栏「工程」打开或创建工程。";
    private string _projectFilePath = string.Empty;
    private string _newProjectDirectory = string.Empty;
    private string _newProjectName = string.Empty;
    private string _wirelessEndpoint = string.Empty;
    private string _customLaunchArguments = string.Empty;
    private string _captureTag = string.Empty;
    private string _captureArchivePreview = "请先打开工程并选择状态为 device 的设备。";
    private string _deviceSavedDownloadSummary = "尚未下载设备 Saved 目录。下载后这里显示落地路径与文件数。";
    private string _packageName = string.Empty;
    private string _unrealProjectName = string.Empty;
    private string _activity = string.Empty;
    private string _deviceGameRootTemplate = string.Empty;
    private string _adbPath = string.Empty;
    private string _memInfoInputPath = string.Empty;
    private string _memInfoProcessDescription = "Select a meminfo text file to begin offline parsing.";
    private bool _androidEnabled;
    private bool _win64Enabled;
    private string _win64Executable = string.Empty;
    private string _win64WorkingDirectory = string.Empty;
    private string _memInfoParsedAt = string.Empty;
    private string _captureResultsCount = "Select a project then browse capture entries.";
    private string _exportInputPath = string.Empty;
    private string _exportOutputPath = string.Empty;
    private bool _exportIncludeDetails;
    private string _exportProgress = "Select an input file and output path, then choose a format.";
    private string _memReportInputPath = string.Empty;
    private string _memReportParseDescription = "Select a .memreport text file to begin offline parsing.";
    private string _memReportParsedAt = string.Empty;
    private string _launchParameterPreview = "请先打开工程以加载启动参数预设。";
    private string _launchOperationSummary = "请先打开工程并选择状态为 device 的设备。";
    private string _deviceLaunchParameterContent = "尚未读取。点击「读取设备参数」查看手机中已有的 uecommandline.txt 内容。";
    private string _operationStage = "Idle";
    private string _scpLogPath = string.Empty;
    private string _scpScreenshotsDir = string.Empty;
    private string _scpParseDescription = "Select a static camera perf log and optional screenshots directory.";
    private StaticCameraPerfParseResult? _lastScpParseResult;
    private string _diffBaselinePath = string.Empty;
    private string _diffCurrentPath = string.Empty;
    private string _diffSource = "StaticCamera";
    private string _diffMetricFilter = string.Empty;
    private string _diffSummary = "Select a source type and two input files, then click Diff.";
    private string _trendTag = string.Empty;
    private string _trendFrom = string.Empty;
    private string _trendTo = string.Empty;
    private string _trendSource = "StaticCamera";
    private string _trendMetricFilter = string.Empty;
    private string _trendSummary = "Open a project, then click Build Trend.";
    private TrendResult? _lastTrendResult;
    private string _renderDocPythonPath = string.Empty;
    private string _renderDocScriptPath = string.Empty;
    private string _renderDocArguments = string.Empty;
    private string _renderDocOutputDir = string.Empty;
    private string _renderDocTimeout = string.Empty;
    private string _renderDocWorkingDir = string.Empty;
    private string _renderDocStandardOutput = string.Empty;
    private string _renderDocStandardError = string.Empty;
    private string _renderDocSummary = "Configure Python and RenderDoc script paths, then execute.";
    private string _consoleCommandText = string.Empty;
    private string _consoleOutput = "Send a console command to the selected device.";
    private bool _consoleIsSending;
    private string _consoleSequenceName = string.Empty;
    private string _consoleSequenceInlineCmds = string.Empty;
    private string _consoleSequenceOutput = "Run a sequence to see results here.";
    private bool _isConsoleSequenceRunning;
    private string _ftpHost = string.Empty;
    private string _ftpPort = FtpSettings.DefaultPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private string _ftpUsername = string.Empty;
    private string _ftpPassword = string.Empty;
    private string _androidFtpPath = string.Empty;
    private string _win64FtpPath = string.Empty;
    private string _downloadPlatform = PlatformNames.ToName(TargetPlatform.Android);
    private string _downloadSummary = "请先打开工程并配置 FTP 下载，然后选择平台下载最新构建。";
    private string _downloadedApkPath = string.Empty;
    private string _downloadFolderSummary = "打开工程后显示本地已下载的构建包。";
    private UkitProject? _project;
    private PlatformScope _platformScope = PlatformScope.All;
    private DeviceDisplayInfo? _selectedDevice;
    private string _selectedDeviceIpSummary = "点击「获取 IP」查询所选设备的地址。";
    private CaptureFileInfo? _selectedCaptureResultFile;
    private CaptureDirectoryInfo? _selectedCaptureResult;
    private LaunchParameterPresetOption? _selectedLaunchParameterPreset;
    private bool _isBusy;
    private CancellationTokenSource? _operationCancellation;
    private CancellationToken OperationCancellationToken => _operationCancellation?.Token ?? CancellationToken.None;

    public ShellViewModel()
        : this(new ProjectService(), new DesktopAdbServiceFactory(), new RejectingConfirmationService())
    {
    }

    public ShellViewModel(
        IProjectService projectService,
        IDesktopAdbServiceFactory adbServiceFactory,
        IUserConfirmationService confirmationService,
        IEditorSettingStore? editorSettingStore = null,
        IUserSettingStore? userSettingStore = null)
    {
        _projectService = projectService ?? throw new ArgumentNullException(nameof(projectService));
        _adbServiceFactory = adbServiceFactory ?? throw new ArgumentNullException(nameof(adbServiceFactory));
        _confirmationService = confirmationService ?? throw new ArgumentNullException(nameof(confirmationService));
        _editorSettingStore = editorSettingStore ?? new EditorSettingStore();
        _userSettingStore = userSettingStore ?? new UserSettingStore();
        CreateProjectCommand = new AsyncDelegateCommand(CreateProjectAsync, CanCreateProject);
        OpenProjectCommand = new AsyncDelegateCommand(OpenProjectAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(ProjectFilePath));
        RefreshDevicesCommand = new AsyncDelegateCommand(RefreshDevicesAsync, () => !IsBusy);
        ConnectWirelessDeviceCommand = new AsyncDelegateCommand(ConnectWirelessDeviceAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(WirelessEndpoint));
        ShowDeviceIpAddressesCommand = new AsyncDelegateCommand(ShowDeviceIpAddressesAsync, CanQuerySelectedDeviceIp);
        PushLaunchParametersCommand = new AsyncDelegateCommand(PushLaunchParametersAsync, CanOperateOnSelectedDevice);
        DeleteLaunchParametersCommand = new AsyncDelegateCommand(DeleteLaunchParametersAsync, CanOperateOnSelectedDevice);
        ReadLaunchParametersCommand = new AsyncDelegateCommand(ReadLaunchParametersAsync, CanOperateOnSelectedDevice);
        StartApplicationCommand = new AsyncDelegateCommand(StartApplicationAsync, CanOperateOnSelectedDevice);
        RunCaptureCommand = new AsyncDelegateCommand(RunCaptureAsync, CanOperateOnSelectedDevice);
        DownloadDeviceSavedCommand = new AsyncDelegateCommand(DownloadDeviceSavedAsync, CanOperateOnSelectedDevice);
        DownloadDeviceLogsCommand = new AsyncDelegateCommand(DownloadDeviceLogsAsync, CanOperateOnSelectedDevice);
        CancelOperationCommand = new DelegateCommand(CancelCurrentOperation, () => IsBusy);
        SaveProjectSettingsCommand = new AsyncDelegateCommand(SaveProjectSettingsAsync, () => !IsBusy && _project is not null);
        ParseMemInfoCommand = new AsyncDelegateCommand(ParseMemInfoAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(MemInfoInputPath));
        RefreshCaptureResultsCommand = new AsyncDelegateCommand(RefreshCaptureResultsAsync, () => !IsBusy && _project is not null);
        ViewCaptureResultFileCommand = new AsyncDelegateCommand(ViewCaptureResultFileAsync, () => !IsBusy && SelectedCaptureResultFile is not null);
        ParseMemReportCommand = new AsyncDelegateCommand(ParseMemReportAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(MemReportInputPath));
        ParseStaticCameraCommand = new AsyncDelegateCommand(ParseStaticCameraAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(ScpLogPath));
        RunDiffCommand = new AsyncDelegateCommand(RunDiffAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(DiffBaselinePath) && !string.IsNullOrWhiteSpace(DiffCurrentPath));
        RunTrendCommand = new AsyncDelegateCommand(RunTrendAsync, () => !IsBusy && _project is not null);
        RunRenderDocCommand = new AsyncDelegateCommand(RunRenderDocAsync, () => !IsBusy
            && !string.IsNullOrWhiteSpace(_renderDocPythonPath)
            && !string.IsNullOrWhiteSpace(_renderDocScriptPath));
        OpenRenderDocOutputDirCommand = new DelegateCommand(OpenRenderDocOutputDir, () => !string.IsNullOrWhiteSpace(_renderDocOutputDir) && Directory.Exists(_renderDocOutputDir));
        _sendConsoleCommandCommand = new AsyncDelegateCommand(SendConsoleCommandAsync, () => !IsBusy && _selectedDevice is not null && !string.IsNullOrWhiteSpace(_consoleCommandText));
        _runConsoleSequenceCommand = new AsyncDelegateCommand(RunConsoleSequenceAsync, () => !IsBusy && _selectedDevice is not null);
        _applyConsoleCommandPresetCommand = new ParameterizedAsyncDelegateCommand<ConsoleCommandPresetOption>(
            ApplyConsoleCommandPresetAsync, () => !IsBusy && _selectedDevice is not null);
        _refreshConsoleCommandPresetValuesCommand = new AsyncDelegateCommand(
            RefreshConsoleCommandPresetValuesAsync, () => !IsBusy && _selectedDevice is not null);
        ExportCaptureDataCommand = new AsyncDelegateCommand(ExportCaptureDataAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(ExportInputPath) && !string.IsNullOrWhiteSpace(ExportOutputPath));
        _clearOperationLogsCommand = new DelegateCommand(ClearOperationLogs, () => OperationLogs.Count > 0);
        DownloadCommand = new AsyncDelegateCommand(DownloadLatestAsync, CanDownloadLatest);
        InstallDownloadedApkCommand = new AsyncDelegateCommand(InstallDownloadedApkAsync, CanInstallDownloadedApk);
        OpenDownloadedDirectoryCommand = new AsyncDelegateCommand(
            OpenDownloadedDirectoryAsync,
            () => DownloadRootDirectory is { Length: > 0 } root && Directory.Exists(root));
        RefreshDownloadedPackagesCommand = new AsyncDelegateCommand(RefreshDownloadedPackagesAsync, () => !IsBusy && _project is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>
    /// 枚举到的全部设备，不受平台作用域影响。元素是带工程别名的展示投影；
    /// 需要对设备执行操作时取 <see cref="DeviceDisplayInfo.Device"/>，
    /// 不要把投影当设备传下去。
    ///
    /// 界面绑定 <see cref="ScopedDevices"/>；这里保留未过滤的全量，
    /// 才能区分「该平台没有设备」与「有设备但被作用域挡住了」。
    /// </summary>
    public ObservableCollection<DeviceDisplayInfo> Devices { get; } = [];

    /// <summary>当前平台作用域内的设备，供设备列表绑定。</summary>
    public ObservableCollection<DeviceDisplayInfo> ScopedDevices { get; } = [];
    public ObservableCollection<ConsoleSequencePreset> ConsoleSequencePresets { get; } = [];

    /// <summary>
    /// 控制台预设指令。界面用 <c>CollectionViewSource</c> 按 <c>Group</c> 分组显示，
    /// 因此这里是单一平铺集合而不是按组嵌套。
    /// </summary>
    public ObservableCollection<ConsoleCommandPresetOption> ConsoleCommandPresets { get; } = [];
    public ObservableCollection<LaunchParameterPresetOption> LaunchParameterPresets { get; } = [];
    public ObservableCollection<MemInfoMetricOption> MemInfoMetrics { get; } = [];
    public ObservableCollection<MemInfoPssOption> MemInfoPssEntries { get; } = [];
    public ObservableCollection<MemInfoNamedEntryOption> MemInfoDalvikEntries { get; } = [];
    public ObservableCollection<MemInfoNamedEntryOption> MemInfoObjectEntries { get; } = [];
    public ObservableCollection<MemInfoDiagnosticOption> MemInfoDiagnostics { get; } = [];
    /// <summary>平台作用域下拉的可选项，「全部」在最前。</summary>
    public IReadOnlyList<PlatformScope> PlatformScopeOptions { get; } = PlatformScope.AllOptions;
    public ObservableCollection<CaptureDirectoryInfo> CaptureResults { get; } = [];
    public ObservableCollection<CaptureFileInfo> CaptureResultFiles { get; } = [];
    public ObservableCollection<MemInfoMetricOption> CaptureResultMetrics { get; } = [];
    public ObservableCollection<MemReportMetricOption> MemReportMetrics { get; } = [];
    public ObservableCollection<MemReportSummaryOption> MemReportSummaries { get; } = [];
    public ObservableCollection<OperationLogEntry> OperationLogs { get; } = [];
    public ObservableCollection<ScpFrameOption> ScpFrames { get; } = [];
    public ObservableCollection<ScpAverageOption> ScpAverages { get; } = [];
    public ObservableCollection<ScpDiagnosticOption> ScpDiagnostics { get; } = [];
    public ObservableCollection<DiffResultOption> DiffResults { get; } = [];
    public ObservableCollection<DiffDiagnosticOption> DiffDiagnostics { get; } = [];
    public ObservableCollection<TrendCaptureOption> TrendCaptures { get; } = [];
    public ObservableCollection<TrendSeriesOption> TrendSeries { get; } = [];
    public ObservableCollection<string> TrendChartSeriesNames { get; } = [];
    public ObservableCollection<System.Windows.Point> TrendChartPoints { get; } = [];
    public ObservableCollection<TrendChartAxisLabel> TrendChartXLabels { get; } = [];
    private string _selectedTrendChartSeries = "";
    public string SelectedTrendChartSeries { get => _selectedTrendChartSeries; set { if (SetField(ref _selectedTrendChartSeries, value)) UpdateTrendChart(); } }
    public ObservableCollection<TrendDiagnosticOption> TrendDiagnostics { get; } = [];
    public ObservableCollection<RenderDocDiagnosticOption> RenderDocDiagnostics { get; } = [];
    public ObservableCollection<DownloadedPackageOption> DownloadedPackages { get; } = [];
    public ICommand CreateProjectCommand { get; }
    public ICommand OpenProjectCommand { get; }
    public ICommand RefreshDevicesCommand { get; }
    public ICommand ConnectWirelessDeviceCommand { get; }
    public ICommand ShowDeviceIpAddressesCommand { get; }
    public ICommand PushLaunchParametersCommand { get; }
    public ICommand DeleteLaunchParametersCommand { get; }
    public ICommand ReadLaunchParametersCommand { get; }
    public ICommand StartApplicationCommand { get; }
    public ICommand RunCaptureCommand { get; }
    public ICommand DownloadDeviceSavedCommand { get; }
    public ICommand DownloadDeviceLogsCommand { get; }
    public ICommand CancelOperationCommand { get; }
    public ICommand SaveProjectSettingsCommand { get; }
    public ICommand ParseMemInfoCommand { get; }
    public ICommand RefreshCaptureResultsCommand { get; }
    public ICommand ViewCaptureResultFileCommand { get; }
    public ICommand ParseMemReportCommand { get; }
    public ICommand ExportCaptureDataCommand { get; }
    public ICommand ParseStaticCameraCommand { get; }
    public ICommand RunDiffCommand { get; }
    public ICommand RunTrendCommand { get; }
    public ICommand RunRenderDocCommand { get; }
    public ICommand OpenRenderDocOutputDirCommand { get; }
    public ICommand DownloadCommand { get; }
    public ICommand InstallDownloadedApkCommand { get; }
    public ICommand OpenDownloadedDirectoryCommand { get; }
    public ICommand RefreshDownloadedPackagesCommand { get; }

    public string SelectedNavigationItem
    {
        get => _selectedNavigationItem;
        set
        {
            if (!SetField(ref _selectedNavigationItem, value)) return;
            OnPropertyChanged(nameof(PageDescription));
        }
    }

    public string PageDescription => SelectedNavigationItem switch
    {
        "设备" => "刷新设备列表（Win64 本机与 ADB 设备）并明确选择目标设备；不会依赖默认第一台设备。",
        "启动参数" => "选择预设并预览 uecommandline.txt，然后推送到已明确选择的设备。",
        "控制台" => "向运行中的 UE 应用发送控制台指令。预设指令按分组列出，开关与数值型可读回游戏中的当前值。",
        "指令序列" => "按顺序执行指令序列（指令 → 等待 → 标记），支持工程预设和内联输入。",
        "采集归档" => "将采集数据归档到新的 Content Capture，避免覆盖历史数据。",
        "RenderDoc" => "调用独立的 RenderDoc Python 脚本，查看退出码与输出目录。",
        "内存解析" => "离线解析 meminfo 与 memreport，导出结果，或浏览工程内已归档的 Capture。",
        "静态相机" => "解析静态相机性能日志，查看逐相机指标并生成 HTML 报告。",
        "基线差分" => "明确选择基线与当前两份输入，比较指标回退与改善。",
        "历史趋势" => "按标签和时间范围汇总工程内的历史 Capture，查看指标走势。",
        "安装包" => "从 FTP 下载最新构建，或从本地已下载的构建包中选择一个安装到设备；未选择时安装最新包。",
        _ => string.Empty
    };

    public string StatusMessage { get => _statusMessage; private set => SetField(ref _statusMessage, value); }
    public string ProjectFilePath { get => _projectFilePath; set { if (SetField(ref _projectFilePath, value)) RaiseCommandStates(); } }
    public string NewProjectDirectory { get => _newProjectDirectory; set { if (SetField(ref _newProjectDirectory, value)) RaiseCommandStates(); } }
    public string NewProjectName { get => _newProjectName; set { if (SetField(ref _newProjectName, value)) RaiseCommandStates(); } }
    public string WirelessEndpoint { get => _wirelessEndpoint; set { if (SetField(ref _wirelessEndpoint, value)) RaiseCommandStates(); } }
    public string CustomLaunchArguments { get => _customLaunchArguments; set { if (SetField(ref _customLaunchArguments, value)) UpdateLaunchParameterPreview(); } }
    public string CaptureTag { get => _captureTag; set { if (SetField(ref _captureTag, value)) UpdateCaptureArchivePreview(); } }
    public string CaptureArchivePreview { get => _captureArchivePreview; private set => SetField(ref _captureArchivePreview, value); }

    /// <summary>最近一次「下载设备 Saved」的落地路径与规模。与归档预览分开显示：
    /// 两者是不同的操作，共用一个文本框会让用户以为下载也写进了 Content 归档。</summary>
    public string DeviceSavedDownloadSummary { get => _deviceSavedDownloadSummary; private set => SetField(ref _deviceSavedDownloadSummary, value); }
    public string UnrealProjectName { get => _unrealProjectName; set => SetField(ref _unrealProjectName, value); }

    // 各平台配置并列可编辑，不按「当前平台」切换可见性：多平台工程需要一次填完全部平台。
    // AndroidEnabled / Win64Enabled 表示该平台是否在本工程启用，取消勾选即清除该平台配置。
    public bool AndroidEnabled { get => _androidEnabled; set => SetField(ref _androidEnabled, value); }
    public string PackageName { get => _packageName; set => SetField(ref _packageName, value); }
    public string Activity { get => _activity; set => SetField(ref _activity, value); }
    /// <summary>
    /// 设备端游戏根目录模板。Saved 目录不单独配置，由该目录 + <c>Saved</c> 派生，
    /// 见 <see cref="DeviceSavedRootPreview"/>。
    /// </summary>
    public string DeviceGameRootTemplate
    {
        get => _deviceGameRootTemplate;
        set { if (SetField(ref _deviceGameRootTemplate, value)) OnPropertyChanged(nameof(DeviceSavedRootPreview)); }
    }

    /// <summary>由 Game 目录派生的 Saved 目录，只读展示，让用户看到实际采集位置。</summary>
    public string DeviceSavedRootPreview =>
        DeviceGameRootTemplate.Trim() is { Length: > 0 } template
            ? $"{template.TrimEnd('/')}/{PlatformProfile.SavedDirectoryName}"
            : string.Empty;

    public string AdbPath { get => _adbPath; set => SetField(ref _adbPath, value); }

    public bool Win64Enabled { get => _win64Enabled; set => SetField(ref _win64Enabled, value); }
    public string Win64Executable { get => _win64Executable; set => SetField(ref _win64Executable, value); }
    public string Win64WorkingDirectory { get => _win64WorkingDirectory; set => SetField(ref _win64WorkingDirectory, value); }

    /// <summary>
    /// FTP 主机。跨平台共享，与各平台的 <see cref="AndroidFtpPath"/>/<see cref="Win64FtpPath"/>
    /// 分开：主机 / 端口 / 凭据是「连哪个服务器」，父目录是「这个平台的构建在哪」。
    /// </summary>
    public string FtpHost { get => _ftpHost; set => SetField(ref _ftpHost, value); }
    public string FtpPort { get => _ftpPort; set => SetField(ref _ftpPort, value); }
    public string FtpUsername { get => _ftpUsername; set => SetField(ref _ftpUsername, value); }

    /// <summary>
    /// FTP 密码。敏感信息：仅在密码框展示，日志与状态消息不得打印明文。
    /// </summary>
    public string FtpPassword { get => _ftpPassword; set => SetField(ref _ftpPassword, value); }

    /// <summary>Android 平台在 FTP 服务器上的下载父目录。</summary>
    public string AndroidFtpPath { get => _androidFtpPath; set => SetField(ref _androidFtpPath, value); }

    /// <summary>Win64 平台在 FTP 服务器上的下载父目录。</summary>
    public string Win64FtpPath { get => _win64FtpPath; set => SetField(ref _win64FtpPath, value); }

    /// <summary>「安装包」页下载目标平台，默认 Android。</summary>
    public string DownloadPlatform
    {
        get => _downloadPlatform;
        set
        {
            if (!SetField(ref _downloadPlatform, value))
            {
                return;
            }

            RaiseCommandStates();
            // 平台决定本地下载根目录，换平台即换列表；立即刷新而不是等用户点刷新。
            _ = RefreshDownloadedPackagesAsync();
        }
    }

    public string DownloadSummary { get => _downloadSummary; private set => SetField(ref _downloadSummary, value); }

    /// <summary>本地已下载构建包列表的现状说明（数量、有无、当前平台）。</summary>
    public string DownloadFolderSummary { get => _downloadFolderSummary; private set => SetField(ref _downloadFolderSummary, value); }

    /// <summary>
    /// 用户在列表中选择的本地构建包。为 null 表示「未选择」，此时安装最新包。
    /// </summary>
    public DownloadedPackageOption? SelectedDownloadedPackage
    {
        get => _selectedDownloadedPackage;
        set { if (SetField(ref _selectedDownloadedPackage, value)) RaiseCommandStates(); }
    }

    private DownloadedPackageOption? _selectedDownloadedPackage;

    /// <summary>最近一次下载得到的 APK 本地路径（仅 Android）。</summary>
    public string DownloadedApkPath { get => _downloadedApkPath; private set { if (SetField(ref _downloadedApkPath, value)) RaiseCommandStates(); } }

    /// <summary>
    /// 当前平台的本地下载根目录（<c>Intermediate/Download/&lt;Platform&gt;</c>）。
    /// 未打开工程或平台无法解析时为 null；目录本身可能尚未创建。
    /// </summary>
    private string? DownloadRootDirectory =>
        _project is null
            ? null
            : PlatformNames.TryParse(DownloadPlatform, out var platform)
                ? Path.Combine(_project.IntermediateDir, "Download", PlatformNames.ToName(platform))
                : null;

    /// <summary>
    /// 当前操作的平台，由所选设备决定。没有选中设备时为空——
    /// 平台不再是一项配置，因此没有「默认平台」可以显示。
    /// </summary>
    public string Platform => SelectedDevice?.Platform ?? string.Empty;
    public string MemInfoInputPath { get => _memInfoInputPath; set { if (SetField(ref _memInfoInputPath, value)) RaiseCommandStates(); } }
    public string MemInfoProcessDescription { get => _memInfoProcessDescription; private set => SetField(ref _memInfoProcessDescription, value); }
    public string MemInfoParsedAt { get => _memInfoParsedAt; private set => SetField(ref _memInfoParsedAt, value); }
    public string CaptureResultsCount { get => _captureResultsCount; private set => SetField(ref _captureResultsCount, value); }
    public string ExportInputPath { get => _exportInputPath; set { if (SetField(ref _exportInputPath, value)) RaiseCommandStates(); } }
    public string ExportOutputPath { get => _exportOutputPath; set { if (SetField(ref _exportOutputPath, value)) RaiseCommandStates(); } }
    public bool ExportIncludeDetails { get => _exportIncludeDetails; set => SetField(ref _exportIncludeDetails, value); }
    public string ExportProgress { get => _exportProgress; private set => SetField(ref _exportProgress, value); }
    public string MemReportInputPath { get => _memReportInputPath; set { if (SetField(ref _memReportInputPath, value)) RaiseCommandStates(); } }
    public string MemReportParseDescription { get => _memReportParseDescription; private set => SetField(ref _memReportParseDescription, value); }
    public string MemReportParsedAt { get => _memReportParsedAt; private set => SetField(ref _memReportParsedAt, value); }
    public string ScpLogPath { get => _scpLogPath; set { if (SetField(ref _scpLogPath, value)) RaiseCommandStates(); } }
    public string ScpScreenshotsDir { get => _scpScreenshotsDir; set { if (SetField(ref _scpScreenshotsDir, value)) RaiseCommandStates(); } }
    public string ScpParseDescription { get => _scpParseDescription; private set => SetField(ref _scpParseDescription, value); }
    public string DiffBaselinePath { get => _diffBaselinePath; set { if (SetField(ref _diffBaselinePath, value)) RaiseCommandStates(); } }
    public string DiffCurrentPath { get => _diffCurrentPath; set { if (SetField(ref _diffCurrentPath, value)) RaiseCommandStates(); } }
    public string DiffSource { get => _diffSource; set { if (SetField(ref _diffSource, value)) RaiseCommandStates(); } }
    public string DiffMetricFilter { get => _diffMetricFilter; set { if (SetField(ref _diffMetricFilter, value)) RaiseCommandStates(); } }
    public string DiffSummary { get => _diffSummary; private set => SetField(ref _diffSummary, value); }
    public string TrendTag { get => _trendTag; set { if (SetField(ref _trendTag, value)) RaiseCommandStates(); } }
    public string TrendFrom { get => _trendFrom; set { if (SetField(ref _trendFrom, value)) RaiseCommandStates(); } }
    public string TrendTo { get => _trendTo; set { if (SetField(ref _trendTo, value)) RaiseCommandStates(); } }
    public string TrendSource { get => _trendSource; set { if (SetField(ref _trendSource, value)) RaiseCommandStates(); } }
    public string TrendMetricFilter { get => _trendMetricFilter; set { if (SetField(ref _trendMetricFilter, value)) RaiseCommandStates(); } }
    public string TrendSummary { get => _trendSummary; private set => SetField(ref _trendSummary, value); }

    public string RenderDocPythonPath { get => _renderDocPythonPath; set { if (SetField(ref _renderDocPythonPath, value)) RaiseCommandStates(); } }
    public string RenderDocScriptPath { get => _renderDocScriptPath; set { if (SetField(ref _renderDocScriptPath, value)) RaiseCommandStates(); } }
    public string RenderDocArguments { get => _renderDocArguments; set { if (SetField(ref _renderDocArguments, value)) RaiseCommandStates(); } }
    public string RenderDocOutputDir { get => _renderDocOutputDir; set { if (SetField(ref _renderDocOutputDir, value)) { RaiseCommandStates(); (OpenRenderDocOutputDirCommand as DelegateCommand)?.RaiseCanExecuteChanged(); } } }
    public string RenderDocTimeout { get => _renderDocTimeout; set => SetField(ref _renderDocTimeout, value); }
    public string RenderDocWorkingDir { get => _renderDocWorkingDir; set => SetField(ref _renderDocWorkingDir, value); }
    public string RenderDocStandardOutput { get => _renderDocStandardOutput; private set => SetField(ref _renderDocStandardOutput, value); }
    public string RenderDocStandardError { get => _renderDocStandardError; private set => SetField(ref _renderDocStandardError, value); }
    public string RenderDocSummary { get => _renderDocSummary; private set => SetField(ref _renderDocSummary, value); }

    public IReadOnlyList<string> DiffSourceOptions { get; } = ["StaticCamera", "MemInfo", "MemReport"];
    public IReadOnlyList<string> TrendSourceOptions { get; } = ["StaticCamera", "MemInfo", "MemReport"];
    public IReadOnlyList<string> DownloadPlatformOptions { get; } = [PlatformNames.ToName(TargetPlatform.Android), PlatformNames.ToName(TargetPlatform.Win64)];
    public string LaunchParameterPreview { get => _launchParameterPreview; private set => SetField(ref _launchParameterPreview, value); }
    public string LaunchOperationSummary { get => _launchOperationSummary; private set => SetField(ref _launchOperationSummary, value); }
    public string DeviceLaunchParameterContent { get => _deviceLaunchParameterContent; private set => SetField(ref _deviceLaunchParameterContent, value); }
    public string OperationStage { get => _operationStage; private set => SetField(ref _operationStage, value); }
    public string ProjectTitle => _project is null ? "当前工程：未打开" : $"当前工程：{_project.Descriptor.ProjectName}";
    public bool IsBusy { get => _isBusy; private set { if (SetField(ref _isBusy, value)) RaiseCommandStates(); } }

    /// <summary>
    /// 本次分析聚焦的平台。这是视图过滤器，决定设备列表与归档列表显示什么；
    /// 操作用哪个平台仍由 <see cref="SelectedDevice"/> 派生，作用域不参与该判定。
    /// 详见 <see cref="Core.Projects.PlatformScope"/>。
    /// </summary>
    public PlatformScope PlatformScope
    {
        get => _platformScope;
        set
        {
            // 下拉框在初始化阶段可能推来 null；回落到「全部」而不是留下 null，
            // 后者会让 Includes 判断处处需要判空。
            var scope = value ?? PlatformScope.All;
            if (!SetField(ref _platformScope, scope)) return;

            OnPropertyChanged(nameof(PlatformScopeDescription));
            ApplyPlatformScope();
            _ = RememberPlatformScopeAsync(scope);
        }
    }

    /// <summary>
    /// 作用域现状说明。过滤掉了设备就说明数量，让「列表短了」有可见的原因，
    /// 而不是看起来像设备掉线了。
    /// </summary>
    public string PlatformScopeDescription
    {
        get
        {
            if (PlatformScope.IsAll)
            {
                return "显示全部平台。";
            }

            var hidden = Devices.Count - ScopedDevices.Count;
            return hidden > 0
                ? $"仅显示 {PlatformScope.Name}，已隐藏其他平台的 {hidden} 台设备。"
                : $"仅显示 {PlatformScope.Name}。";
        }
    }

    public DeviceDisplayInfo? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (!SetField(ref _selectedDevice, value)) return;
            OnPropertyChanged(nameof(SelectedDeviceDescription));
            // 当前平台由所选设备派生，换设备就可能换平台。
            OnPropertyChanged(nameof(Platform));
            // IP 属于具体某台设备，换设备后旧值不再成立。
            SelectedDeviceIpSummary = "点击「获取 IP」查询所选设备的地址。";
            // 设备内容同样属于具体某台设备，换设备后旧内容不再成立。
            DeviceLaunchParameterContent = "尚未读取。点击「读取设备参数」查看手机中已有的 uecommandline.txt 内容。";
            UpdateCaptureArchivePreview();
            UpdateLaunchOperationSummary();
            UpdateLaunchParameterPreview();
            RaiseCommandStates();
        }
    }

    public CaptureFileInfo? SelectedCaptureResultFile
    {
        get => _selectedCaptureResultFile;
        set
        {
            if (SetField(ref _selectedCaptureResultFile, value))
                RaiseCommandStates();
        }
    }

    /// <summary>
    /// 当前选中的 Capture 目录。选中后立即列出其中的文件，
    /// 否则 <see cref="CaptureResultFiles"/> 永远为空、查看命令无法启用。
    /// </summary>
    public CaptureDirectoryInfo? SelectedCaptureResult
    {
        get => _selectedCaptureResult;
        set
        {
            if (!SetField(ref _selectedCaptureResult, value)) return;
            RaiseCommandStates();
            _ = LoadCaptureResultFilesAsync(value);
        }
    }

    private async Task LoadCaptureResultFilesAsync(CaptureDirectoryInfo? capture)
    {
        CaptureResultFiles.Clear();
        SelectedCaptureResultFile = null;
        CaptureResultMetrics.Clear();
        if (capture is null) return;

        try
        {
            var files = await new CaptureAnalysisService().ListCaptureFilesAsync(capture.FullPath);
            foreach (var file in files)
            {
                CaptureResultFiles.Add(file);
            }
            CaptureResultsCount = $"{capture.CaptureId}：{CaptureResultFiles.Count} 个文件。";
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
            AddOperationLog("Error", $"列出 Capture 文件失败：{exception.Message}");
        }
    }

    /// <summary>
    /// 当前在预设列表中被选中的预设（点击条目，而非勾选复选框）。选中后
    /// <see cref="SelectedLaunchParameterPresetArguments"/> 显示其完整参数。
    /// </summary>
    public LaunchParameterPresetOption? SelectedLaunchParameterPreset
    {
        get => _selectedLaunchParameterPreset;
        set
        {
            if (!SetField(ref _selectedLaunchParameterPreset, value)) return;
            OnPropertyChanged(nameof(SelectedLaunchParameterPresetArguments));
        }
    }

    /// <summary>选中预设的完整参数；未选中时给出提示，而不是留一块空白。</summary>
    public string SelectedLaunchParameterPresetArguments =>
        SelectedLaunchParameterPreset is null
            ? "点击预设条目查看参数。"
            : SelectedLaunchParameterPreset.Arguments;

    /// <summary>
    /// 所选设备摘要。设备标识始终在最前：后续所有操作以它为准，
    /// 别名只是便于人辨认，把别名放在标识位置会让日志与界面对不上。
    /// 带上平台：同一份列表里 Win64 本机与 Android 设备并存，
    /// 摘要里不写平台就无法确认「接下来的操作走本机进程还是 ADB」。
    /// </summary>
    public string SelectedDeviceDescription => SelectedDevice is null
        ? "尚未选择设备。"
        : $"{SelectedDevice.Id} · {SelectedDevice.Platform} · {SelectedDevice.StatusText} · {SelectedDevice.Name}"
          + (SelectedDevice.HasAlias ? $" · 别名：{SelectedDevice.Alias}" : string.Empty);

    /// <summary>
    /// 上次查询到的所选设备 IP。换设备时清空——留着上一台的地址会被读成当前设备的。
    /// </summary>
    public string SelectedDeviceIpSummary
    {
        get => _selectedDeviceIpSummary;
        private set => SetField(ref _selectedDeviceIpSummary, value);
    }

    private bool CanCreateProject() =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(NewProjectDirectory) &&
        !string.IsNullOrWhiteSpace(NewProjectName);

    private bool CanOperateOnSelectedDevice() => !IsBusy && _project is not null && SelectedDevice?.IsAvailable == true;

    private Task CreateProjectAsync() => RunAsync("正在创建工程…", async progress =>
    {
        var result = await _projectService.CreateProjectAsync(new CreateProjectRequest(NewProjectDirectory, NewProjectName), progress, OperationCancellationToken);
        SetCurrentProject(result.Project);
        await RestorePlatformScopeAsync();
        await RememberLastProjectAsync(result.Project.ProjectFilePath);
        StatusMessage = $"已创建工程：{result.Project.ProjectFilePath}";
    });

    /// <summary>
    /// 打开 <see cref="ProjectFilePath"/> 指向的工程。菜单栏「打开工程」在文件对话框
    /// 选定路径后直接调用，无需经由命令，因此设为 public。
    /// </summary>
    public Task OpenProjectAsync() => RunAsync("正在打开工程…", async progress =>
    {
        var project = await _projectService.OpenProjectAsync(ProjectFilePath, progress, OperationCancellationToken);
        SetCurrentProject(project);

        // 作用域是工程内的用户设置，只有工程打开后才有记录可读，因此在此恢复而不是在启动时。
        await RestorePlatformScopeAsync();
        await RememberLastProjectAsync(project.ProjectFilePath);
        StatusMessage = $"已打开工程：{project.ProjectFilePath}";
    });

    /// <summary>
    /// 启动时恢复上次打开的工程。没有记录就保持「未打开工程」状态；
    /// 记录指向的工程已不存在或打不开时通知用户，由用户自行新建或手动打开，
    /// 不静默清空记录也不退到别的工程。
    /// </summary>
    public async Task RestoreLastProjectAsync()
    {
        string? lastPath;
        try
        {
            lastPath = await _editorSettingStore.TryGetLastProjectFilePathAsync();
        }
        catch (Exception exception)
        {
            AddOperationLog("Error", $"读取上次打开的工程记录失败：{exception.Message}");
            return;
        }

        if (string.IsNullOrWhiteSpace(lastPath))
        {
            return;
        }

        if (!File.Exists(lastPath))
        {
            await ReportLastProjectUnavailableAsync(lastPath, "工程文件已不存在，可能被移动、重命名或删除。");
            return;
        }

        ProjectFilePath = lastPath;
        await OpenProjectAsync();

        // RunAsync 把失败转成状态消息，因此用「工程是否真的加载出来」判断结果，
        // 而不是假定 OpenProjectAsync 返回即成功。
        if (_project is null)
        {
            await ReportLastProjectUnavailableAsync(lastPath, StatusMessage);
        }
    }

    private async Task ReportLastProjectUnavailableAsync(string projectFilePath, string reason)
    {
        ProjectFilePath = string.Empty;
        StatusMessage = $"无法打开上次的工程：{projectFilePath}。{reason}";
        AddOperationLog("Warning", StatusMessage);
        await _confirmationService.NotifyLastProjectUnavailableAsync(projectFilePath, reason);
    }

    /// <summary>
    /// 记录当前工程为「上次打开的工程」。写入失败只降级为一条日志：
    /// 工程本身已经打开，不该因为记不住而报成打开失败。
    /// </summary>
    private async Task RememberLastProjectAsync(string projectFilePath)
    {
        try
        {
            await _editorSettingStore.SaveLastProjectFilePathAsync(projectFilePath, OperationCancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AddOperationLog("Error", $"记录上次打开的工程失败：{exception.Message}");
        }
    }

    private Task RefreshDevicesAsync() => RunAsync("正在刷新设备列表…", async progress =>
    {
        var devices = await ListDevicesAsync(progress, OperationCancellationToken);
        UpdateDevices(devices);
    });

    private Task ConnectWirelessDeviceAsync() => RunAsync("正在连接 Wi-Fi ADB 设备…", async progress =>
    {
        var service = CreateAdbService();
        await service.ConnectAsync(WirelessEndpoint.Trim(), progress, OperationCancellationToken);
        var devices = await ListDevicesAsync(progress, OperationCancellationToken);
        UpdateDevices(devices);
        StatusMessage = $"已连接 {WirelessEndpoint.Trim()}，请从列表中明确选择目标设备。";
    });

    /// <summary>
    /// 仅 Android 设备可查 IP：这条能力由 ADB shell 提供，Win64 本机地址不经此路径。
    /// 要求设备状态为 device——离线或未授权设备上 shell 调用必然失败。
    /// </summary>
    private bool CanQuerySelectedDeviceIp() =>
        !IsBusy
        && SelectedDevice?.IsAvailable == true
        && PlatformNames.TryParse(SelectedDevice.Platform, out var platform)
        && platform == TargetPlatform.Android;

    private Task ShowDeviceIpAddressesAsync() => RunAsync("正在查询设备 IP 地址…", async progress =>
    {
        var device = SelectedDevice;
        if (device is null)
        {
            return;
        }

        try
        {
            var addresses = await CreateAdbService().GetIpAddressesAsync(device.Id, progress, OperationCancellationToken);
            foreach (var address in addresses)
            {
                AddOperationLog("DeviceIp", $"{device.Id} · {address.InterfaceName} · {address.Kind} · {address}");
            }

            // 摘要只列 WiFi 地址，那是「同网段连这台手机」时要用的；其它接口在日志里完整可见。
            // 没有 WiFi 时退到全部接口，不假装设备没有地址。
            var wifi = addresses.Where(address => address.Kind == DeviceNetworkInterfaceKind.WiFi).ToArray();
            var shown = wifi.Length > 0 ? wifi : addresses;
            SelectedDeviceIpSummary = string.Join("　", shown.Select(address => address.ToString()));
            StatusMessage = $"{device.Id} 共 {addresses.Count} 个地址：{SelectedDeviceIpSummary}";
        }
        catch (AdbDeviceAddressUnavailableException exception)
        {
            // 「设备未联网」不是操作失败，作为结果如实呈现，不冒充地址。
            SelectedDeviceIpSummary = "未查到 IPv4 地址。";
            StatusMessage = exception.Message;
            AddOperationLog("DeviceIp", exception.Message);
        }
    });

    private async Task<IReadOnlyList<DeviceDisplayInfo>> ListDevicesAsync(IProgress<OperationProgress> progress, CancellationToken cancellationToken)
    {
        // 作用域限定了平台就只枚举该平台，与 CLI 的 --platform 一致：跨平台枚举会为一个
        // 用不到的平台去起 adb，把「adb 未安装」变成 Win64 操作的失败原因。
        var providers = new List<IDeviceProvider>();
        if (PlatformScope.Includes(PlatformNames.Win64))
        {
            providers.Add(new Win64DeviceService());
        }

        if (PlatformScope.Includes(PlatformNames.Android))
        {
            try
            {
                providers.Add(new AdbDeviceService(CreateAdbService()));
            }
            catch (AdbPathResolutionException exception)
            {
                providers.Add(new UnavailableDeviceProvider(TargetPlatform.Android, exception.Message));
            }
        }

        var result = await new AggregateDeviceProvider(providers).ListDevicesAsync(progress, cancellationToken);
        foreach (var failure in result.Failures)
        {
            AddOperationLog("Error", $"{failure.Platform} device enumeration failed: {failure.Message}");
        }

        // 未打开工程时别名为空，设备列表照常可用：别名是附加信息，不是列出设备的前提。
        return DeviceDisplayInfo.CreateAll(result.Devices, _project?.Settings);
    }

    private AdbService CreateAdbService()
    {
        return _adbServiceFactory.Create(_project?.Settings, new Progress<ProcessOutput>(output =>
            AddOperationLog(output.Stream.ToString(), output.Text)));
    }

    /// <summary>
    /// 按设备缓存的设备服务。
    ///
    /// AdbDeviceService 内部记着「这台设备已 forward 过端口」，每次操作都新建服务会把那份记录
    /// 一起丢掉，于是每条控制台指令都要多起一个 adb forward 进程，并把它的输出混进指令结果——
    /// 正是那份缓存想避免的事。缓存服务实例让 forward-once 真正成立。
    ///
    /// 失效条件见 <see cref="InvalidateDeviceServices"/> 与 <see cref="DropStaleDeviceServices"/>。
    /// </summary>
    private readonly Dictionary<(string Platform, string Id), IDeviceService> _deviceServices = [];

    /// <summary>
    /// 取该设备的设备服务，没有则新建并缓存。
    ///
    /// 平台取自设备本身，不取自工程配置：固定用 AdbDeviceService 会让 Win64 设备的操作走 adb。
    /// </summary>
    private IDeviceService ResolveDeviceServiceForDevice(IDevice device)
    {
        var key = (device.Platform, device.Id);
        if (_deviceServices.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var created = new DeviceServiceFactory(
            adbService: PlatformNames.Parse(device.Platform, nameof(device)) == TargetPlatform.Android ? CreateAdbService() : null,
            processRunner: new ProcessRunner())
            .CreateForDevice(device, _project?.Settings);
        _deviceServices[key] = created;
        return created;
    }

    /// <summary>
    /// 清空全部缓存的设备服务。工程或工程配置变化后必须调用：指令通道端口、adb 路径都来自配置，
    /// 留着旧实例会让保存后的配置对已经用过的设备不生效。
    /// </summary>
    private void InvalidateDeviceServices() => _deviceServices.Clear();

    /// <summary>
    /// 丢掉当前不可用设备的缓存服务。
    ///
    /// adb forward 随设备断开一起消失，而缓存里的「已 forward 过」标记不会自己失效；
    /// 留着它会让重连后的指令跳过 forward，打到一个已经不存在的转发上。
    /// 因此每次刷新设备列表时，凡是不在「当前可用」之列的设备一律丢掉，重连后重新 forward。
    /// </summary>
    private void DropStaleDeviceServices(IReadOnlyList<DeviceDisplayInfo> devices)
    {
        var available = devices
            .Where(device => device.IsAvailable)
            .Select(device => (device.Platform, device.Id))
            .ToHashSet();

        foreach (var key in _deviceServices.Keys.Where(key => !available.Contains(key)).ToArray())
        {
            _deviceServices.Remove(key);
        }
    }

    /// <summary>
    /// 为指定设备构造启动参数服务。必须按设备平台构造设备服务——
    /// 固定用 AdbDeviceService 会让 Win64 设备的操作走 adb 并按 Android 路径规则解析。
    /// </summary>
    private LaunchParameterService CreateLaunchParameterService(IDevice device) =>
        new(ResolveDeviceServiceForDevice(device));

    /// <summary>
    /// 当前所选设备平台的落地值。未选设备或该平台未配置时返回 null，
    /// 供预览类逻辑显示原因而不是抛出。
    /// </summary>
    private PlatformTarget? TryResolveSelectedTarget(out string? error)
    {
        error = null;
        if (_project is null || SelectedDevice is null)
        {
            error = "请先打开工程并选择设备。";
            return null;
        }

        try
        {
            var platform = PlatformNames.Parse(SelectedDevice.Platform, nameof(SelectedDevice));
            return _project.Settings.ResolveTarget(platform, $"设备 '{SelectedDevice.Id}' 属于 {SelectedDevice.Platform} 平台。");
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            error = exception.Message;
            return null;
        }
    }

    /// <summary>
    /// 构造带控制台服务的 CaptureService，使配置的采集前后指令序列真正执行。
    /// 省略控制台服务会让序列被静默跳过，导致 GUI 与 CLI 行为不一致。
    /// </summary>
    private CaptureService CreateCaptureService(IDevice device)
    {
        var deviceService = ResolveDeviceServiceForDevice(device);

        // 按能力而非平台判断：新增平台时无需回到这里改分支。
        var consoleService = deviceService.Supports(DeviceCapability.SendConsoleCommand)
            ? new ConsoleCommandService(deviceService)
            : null;
        return new CaptureService(deviceService, consoleService);
    }

    private void UpdateDevices(IReadOnlyList<DeviceDisplayInfo> devices)
    {
        DropStaleDeviceServices(devices);
        Devices.Clear();
        foreach (var device in devices) Devices.Add(device);
        ApplyPlatformScope();
    }

    /// <summary>
    /// 按当前作用域重建 <see cref="ScopedDevices"/> 并重选设备。
    ///
    /// 「唯一可用设备自动选中」只在作用域内成立：作用域是用户的显式选择，
    /// 在其中唯一的设备不构成不变式 #4 所指的隐式选择。跨作用域时若已选设备
    /// 落在作用域外必须清空——留着它会让后续操作打到用户以为已经排除的平台。
    /// </summary>
    private void ApplyPlatformScope()
    {
        var scoped = Devices.Where(device => PlatformScope.Includes(device.Platform)).ToArray();
        ScopedDevices.Clear();
        foreach (var device in scoped) ScopedDevices.Add(device);

        var previous = SelectedDevice;
        var available = scoped.Where(device => device.IsAvailable).ToArray();

        // 已选设备仍在作用域内时保持选中：换作用域不该打断正在进行的工作。
        if (previous is not null && scoped.Contains(previous))
        {
            OnPropertyChanged(nameof(PlatformScopeDescription));
            return;
        }

        if (available.Length == 1)
        {
            SelectedDevice = available[0];
            StatusMessage = $"已自动选择唯一可用设备：{available[0].Id}（{available[0].DisplayLabel}）。";
        }
        else
        {
            SelectedDevice = null;
            StatusMessage = DescribeDeviceSelectionState(scoped.Length);
        }

        OnPropertyChanged(nameof(PlatformScopeDescription));
    }

    /// <summary>
    /// 设备列表现状说明。作用域挡住设备时明说，不让它看起来像设备掉线。
    /// </summary>
    private string DescribeDeviceSelectionState(int scopedCount)
    {
        if (scopedCount > 0)
        {
            return $"发现 {scopedCount} 台设备，请从列表中明确选择目标设备。";
        }

        if (Devices.Count > 0)
        {
            return $"作用域 {PlatformScope.Name} 内没有设备；共枚举到 {Devices.Count} 台其他平台的设备。" +
                   "如需操作它们，请把顶部平台切到「全部」或对应平台。";
        }

        return "未发现任何设备。请检查 ADB 连接。";
    }

    /// <summary>
    /// 恢复当前工程记录的平台作用域，在工程打开后调用。作用域是工程内的用户设置，
    /// 没有打开的工程就没有可读的记录，因此不在启动时独立恢复。
    ///
    /// 没有记录时保留当前作用域：把「记录缺失」当成「记录为全部」会在打开工程时
    /// 重置用户刚选的平台。读取失败只降级为一条日志——记不住上次的选择不该让界面起不来。
    /// </summary>
    public async Task RestorePlatformScopeAsync()
    {
        var project = _project;
        if (project is null)
        {
            return;
        }

        try
        {
            if (await _userSettingStore.TryGetPlatformScopeAsync(project, OperationCancellationToken) is not { } scope)
            {
                return;
            }

            // 直接写字段并手工触发刷新，绕开属性 setter 的保存分支：
            // 恢复读到的值再写回去是一次无意义的磁盘写入。
            if (SetField(ref _platformScope, scope, nameof(PlatformScope)))
            {
                OnPropertyChanged(nameof(PlatformScopeDescription));
                ApplyPlatformScope();
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AddOperationLog("Error", $"读取工程的平台作用域失败：{exception.Message}");
        }
    }

    /// <summary>
    /// 把当前平台作用域记进当前工程。未打开工程时无处可记，直接跳过——
    /// 作用域此时只是本次会话的临时选择。写入失败只降级为一条日志：
    /// 作用域已在界面上生效，不该因为记不住而报成切换失败。
    /// </summary>
    private async Task RememberPlatformScopeAsync(PlatformScope scope)
    {
        var project = _project;
        if (project is null)
        {
            return;
        }

        try
        {
            await _userSettingStore.SavePlatformScopeAsync(project, scope, OperationCancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AddOperationLog("Error", $"记录平台作用域失败：{exception.Message}");
        }
    }
    private void SetCurrentProject(UkitProject project)
    {
        _project = project;

        // 指令通道端口、adb 路径都来自工程配置，换工程或保存配置后旧实例就是过期的。
        InvalidateDeviceServices();
        ProjectFilePath = project.ProjectFilePath;
        Devices.Clear();
        ScopedDevices.Clear();
        SelectedDevice = null;
        LaunchParameterPresets.Clear();
        foreach (var preset in project.Settings.LaunchParameterPresets)
        {
            var group = project.Settings.LaunchParameterGroups
                .FirstOrDefault(candidate => candidate.Members.Contains(preset.Name, StringComparer.OrdinalIgnoreCase));
            var option = new LaunchParameterPresetOption(preset, group);
            option.PropertyChanged += (_, _) => UpdateLaunchParameterPreview();
            LaunchParameterPresets.Add(option);
        }
        SelectedLaunchParameterPreset = LaunchParameterPresets.Count > 0 ? LaunchParameterPresets[0] : null;

        CaptureTag = project.Settings.DefaultCaptureTag;
        ConsoleSequencePresets.Clear();
        foreach (var preset in project.Settings.ConsoleSequences) ConsoleSequencePresets.Add(preset);
        ConsoleCommandPresets.Clear();
        foreach (var preset in project.Settings.ConsoleCommandPresets)
        {
            ConsoleCommandPresets.Add(new ConsoleCommandPresetOption(preset));
        }
        ConsoleSequenceName = ConsoleSequencePresets.Count > 0 ? ConsoleSequencePresets[0].Name : string.Empty;
        UnrealProjectName = project.Settings.UnrealProjectName;

        // 未启用的平台仍展示其默认值，让用户勾选后就能直接编辑，
        // 而不是先勾选、保存、再重开才看到字段。
        var android = project.Settings.Android;
        AndroidEnabled = android is not null;
        var androidValues = android ?? AndroidPlatformProfile.CreateDefaults();
        PackageName = androidValues.PackageName;
        Activity = androidValues.Activity;
        DeviceGameRootTemplate = androidValues.GameRootTemplate;
        AdbPath = androidValues.AdbPath;
        AndroidFtpPath = androidValues.FtpPath;

        var win64 = project.Settings.Win64;
        Win64Enabled = win64 is not null;
        var win64Values = win64 ?? Win64PlatformProfile.CreateDefaults();
        Win64Executable = win64Values.Executable;
        Win64WorkingDirectory = win64Values.WorkingDirectory;
        Win64FtpPath = win64Values.FtpPath;

        var ftp = project.Settings.FtpSettings;
        FtpHost = ftp.Host;
        FtpPort = ftp.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        FtpUsername = ftp.Username;
        FtpPassword = ftp.Password;

        // 切换工程后，上一个工程的下载结果与 APK 路径不再成立。
        DownloadSummary = "请选择平台并点击「下载最新」。";
        DownloadedApkPath = string.Empty;
        DownloadedPackages.Clear();
        SelectedDownloadedPackage = null;
        DownloadFolderSummary = "点击「刷新本地包」列出本工程已下载的构建包。";

        OnPropertyChanged(nameof(ProjectTitle));
        UpdateLaunchParameterPreview();
        UpdateLaunchOperationSummary();
        UpdateCaptureArchivePreview();
        RaiseCommandStates();
    }

    private Task SaveProjectSettingsAsync() => RunAsync("正在保存项目默认配置…", async progress =>
    {
        // 未启用的平台写 null，即从工程配置中移除该平台，而不是留一份空值配置——
        // 空值配置会让「未配置该平台」的报错永远不触发，改为在采集时报路径错误。
        var settings = _project!.Settings with
        {
            UnrealProjectName = UnrealProjectName.Trim(),
            DefaultCaptureTag = CaptureTag.Trim(),
            Android = AndroidEnabled
                ? new AndroidPlatformProfile(
                    PackageName: PackageName.Trim(),
                    Activity: Activity.Trim(),
                    GameRootTemplate: DeviceGameRootTemplate.Trim(),
                    AdbPath: AdbPath.Trim(),
                    FtpPath: AndroidFtpPath.Trim())
                : null,
            Win64 = Win64Enabled
                ? new Win64PlatformProfile(
                    Executable: Win64Executable.Trim(),
                    WorkingDirectory: Win64WorkingDirectory.Trim(),
                    FtpPath: Win64FtpPath.Trim())
                : null,
            Ftp = new FtpSettings(
                Host: FtpHost.Trim(),
                Port: ParseFtpPortText(FtpPort),
                Username: FtpUsername.Trim(),
                Password: FtpPassword)
        };
        SetCurrentProject(await _projectService.UpdateSettingsAsync(_project, settings, progress, OperationCancellationToken));
        StatusMessage = "项目默认配置已保存。";
    });

    /// <summary>
    /// 把界面上的 FTP 端口文本解析为整数。空文本回退默认端口 21；
    /// 填了但非法必须报错，不让「端口 2121」悄悄回退到 21。
    /// </summary>
    private static int ParseFtpPortText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return FtpSettings.DefaultPort;
        }

        if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var port)
            || port is < 1 or > 65535)
        {
            throw new InvalidDataException($"FTP 端口无效: {value}。必须是 1 到 65535 之间的整数。");
        }

        return port;
    }

    private void UpdateLaunchParameterPreview()
    {
        if (_project is null)
        {
            LaunchParameterPreview = "请先打开工程以加载启动参数预设。";
            return;
        }

        try
        {
            // 参数内容与平台无关，先算出来：即使还没选设备也能预览将要写入的内容。
            var content = new LaunchParameterService(new AdbDeviceService(CreateAdbService()))
                .BuildContent(_project.Settings, GetSelectedPresetNames(), CustomLaunchArguments);
            if (SelectedDevice is null)
            {
                LaunchParameterPreview = $"目标路径：选择设备后确定{Environment.NewLine}{Environment.NewLine}{content}";
                UpdateLaunchOperationSummary();
                return;
            }

            var remotePath = CreateLaunchParameterService(SelectedDevice.Device)
                .GetRemotePath(_project.Settings);
            LaunchParameterPreview = $"目标路径：{remotePath}{Environment.NewLine}{Environment.NewLine}{content}";
            UpdateLaunchOperationSummary(remotePath);
        }
        catch (Exception exception)
        {
            LaunchParameterPreview = $"无法生成启动参数：{exception.Message}";
            UpdateLaunchOperationSummary();
        }
    }

    private IReadOnlyList<string> GetSelectedPresetNames() => LaunchParameterPresets.Where(option => option.IsSelected).Select(option => option.Name).ToArray();

    private Task PushLaunchParametersAsync() => RunAsync("正在推送 uecommandline.txt…", async progress =>
    {
        var result = await CreateLaunchParameterService(SelectedDevice!.Device).PushAsync(
            _project!,
            new LaunchParameterRequest(SelectedDevice!.Id, GetSelectedPresetNames(), CustomLaunchArguments),
            progress,
            OperationCancellationToken);
        StatusMessage = $"已推送启动参数到：{result.RemotePath}";
        UpdateLaunchParameterPreview();
    });

    private Task DeleteLaunchParametersAsync() => RunAsync("正在删除 uecommandline.txt…", async progress =>
    {
        var service = CreateLaunchParameterService(SelectedDevice!.Device);
        var remotePath = service.GetRemotePath(_project!.Settings);
        var platformTarget = _project.Settings.ResolveTarget(
            PlatformNames.Parse(SelectedDevice!.Platform, nameof(SelectedDevice)));
        var target = new LaunchOperationTarget(
            SelectedDevice!.Id, platformTarget.LaunchTarget, platformTarget.LaunchActivity ?? "-", remotePath);
        if (!await _confirmationService.ConfirmDeleteLaunchParametersAsync(target))
        {
            StatusMessage = "已取消删除设备启动参数。";
            return;
        }
        await service.DeleteAsync(_project, SelectedDevice!.Id, progress, OperationCancellationToken);
        StatusMessage = $"已删除设备上的启动参数：{remotePath}";
    });

    /// <summary>
    /// 读取设备上已有的 uecommandline.txt 内容，用于核对设备端实际生效的参数
    /// （例如 Remote Control 开关是否真的落地）。读取是查询语义：文件不存在不抛异常，
    /// 而是把结果如实显示在「设备内容」框里。
    /// </summary>
    private Task ReadLaunchParametersAsync() => RunAsync("正在读取设备上的 uecommandline.txt…", async progress =>
    {
        var result = await CreateLaunchParameterService(SelectedDevice!.Device).ReadAsync(
            _project!, SelectedDevice!.Id, progress, OperationCancellationToken);
        if (result.ReadResult.Succeeded)
        {
            DeviceLaunchParameterContent = result.ReadResult.StandardOutput;
            StatusMessage = $"已读取设备启动参数：{result.RemotePath}";
        }
        else
        {
            DeviceLaunchParameterContent = $"设备上暂无该文件（{result.ReadResult.StandardError}）。";
            StatusMessage = $"设备上无启动参数：{result.RemotePath}";
            AddOperationLog("Warning", $"设备上无启动参数：{result.RemotePath} · {result.ReadResult.StandardError}");
        }
    });

    private Task StartApplicationAsync() => RunAsync("正在启动应用…", async progress =>
    {
        var service = CreateLaunchParameterService(SelectedDevice!.Device);
        var target = _project!.Settings.ResolveTarget(
            PlatformNames.Parse(SelectedDevice!.Platform, nameof(SelectedDevice)));

        // 启动前先尝试停止旧实例，避免进程已在前台运行导致启动无效。
        // 停止是尽力而为：应用可能本来就未运行（Win64 force-stop 或 Android am force-stop
        // 对未运行目标会失败），此时不把「没有可停的进程」当成启动失败。
        try
        {
            await service.StopApplicationAsync(_project, SelectedDevice!.Id, progress, OperationCancellationToken);
        }
        catch (DeviceCommandException exception)
        {
            AddOperationLog("Warning", $"停止旧实例失败（忽略，继续启动）：{exception.Message}");
        }

        await service.StartApplicationAsync(_project, SelectedDevice!.Id, progress, OperationCancellationToken);
        StatusMessage = $"已发送应用启动请求：{target.LaunchTarget}{(target.LaunchActivity is { Length: > 0 } activity ? $"/{activity}" : string.Empty)}";
    });

    private void UpdateCaptureArchivePreview()
    {
        if (_project is null || SelectedDevice?.IsAvailable != true)
        {
            CaptureArchivePreview = "请先打开工程并选择状态为 device 的设备。";
            return;
        }

        try
        {
            var plan = CreateCaptureService(SelectedDevice.Device).CreatePlan(new CaptureRequest(_project, SelectedDevice.Device, CaptureTag));
            CaptureArchivePreview = $"归档目录：{plan.CaptureDirectory}{Environment.NewLine}设备 Saved：{plan.DeviceSavedDirectory}";
        }
        catch (Exception exception)
        {
            CaptureArchivePreview = $"无法生成归档预览：{exception.Message}";
        }
    }

    private Task RunCaptureAsync() => RunAsync("正在采集并归档原始数据…", async progress =>
    {
        var request = new CaptureRequest(_project!, SelectedDevice!.Device, CaptureTag);
        var result = await CreateCaptureService(SelectedDevice.Device).CaptureAsync(request, progress, OperationCancellationToken);
        CaptureArchivePreview = $"归档目录：{result.Plan.CaptureDirectory}{Environment.NewLine}清单：{result.ManifestPath}";
        StatusMessage = $"采集完成：{result.Plan.CaptureDirectory}";
    });

    /// <summary>
    /// 把所选设备上的 UE Saved 整目录取回本地并打开该目录。
    ///
    /// 与「采集并归档」分开是刻意的：这里不写 Content/、不生成 CaptureManifest.json，
    /// 只是一次「拿下来看看」，落地在 Saved/DeviceSaved 这类可再生的派生数据下。
    /// 每次取回都进新的带时间戳目录，不覆盖上一次的结果。
    /// </summary>
    private Task DownloadDeviceSavedAsync() => DownloadDeviceSavedAsync(UnealSavedScope.Common, "Saved 常用子目录");

    /// <summary>
    /// 只把所选设备上的 <c>Saved/Logs</c> 取回本地并打开该目录。日志通常是排查问题时唯一要看的部分，
    /// 而完整 Saved 可能很大（含 Profiling、截图），因此单独给一个入口。
    /// </summary>
    private Task DownloadDeviceLogsAsync() => DownloadDeviceSavedAsync(UnealSavedScope.Logs, "Logs");

    /// <summary>
    /// 取回设备 Saved 数据的共用流程。范围不同只影响设备端源目录与提示文字，
    /// 落地、打开目录、汇总展示完全一致，因此两个按钮共用这一份实现——
    /// 各写一份会让「不覆盖上一次」这类规则在两处分别演化。
    /// </summary>
    private Task DownloadDeviceSavedAsync(UnealSavedScope scope, string scopeLabel) =>
        RunAsync($"正在下载设备 {scopeLabel} 目录…", async progress =>
        {
            var request = new UnrealSavedPullRequest(_project!, SelectedDevice!.Device, scope);
            var service = new UnrealSavedService(ResolveDeviceServiceForDevice(SelectedDevice.Device));
            var result = await service.DownloadAsync(request, progress, OperationCancellationToken);

            DeviceSavedDownloadSummary =
                $"已下载到：{result.Plan.LocalDirectory}{Environment.NewLine}" +
                $"设备 {scopeLabel}：{result.Plan.DeviceDirectory}{Environment.NewLine}" +
                $"共 {result.FileCount} 个文件、{FormatByteSize(result.TotalBytes)}。";
            StatusMessage = $"设备 {scopeLabel} 已下载：{result.Plan.LocalDirectory}";
            AddOperationLog("Info", DeviceSavedDownloadSummary);

            // 打开目录是下载的一部分（用户就是为了翻看文件），但打不开不算下载失败：
            // 文件已经在本地了，路径也已显示，因此只记一条警告。
            OpenLocalDirectory(result.Plan.LocalDirectory);
        });

    /// <summary>
    /// 在资源管理器中打开本地目录。失败按警告处理，不抛出——调用方的主操作已经成功，
    /// 把「打不开窗口」升级成操作失败会让用户以为文件没下载下来。
    /// </summary>
    private void OpenLocalDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            AddOperationLog("Warning", $"目录不存在，无法打开：{directory}");
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AddOperationLog("Warning", $"无法打开目录 {directory}：{exception.Message}");
        }
    }

    private static string FormatByteSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };

    private Task ParseMemInfoAsync() => RunAsync("Parsing Android meminfo...", async _ =>
    {
        var inputPath = Path.GetFullPath(MemInfoInputPath);
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Meminfo input file not found.", inputPath);
        }

        var parseResult = await new AndroidMemInfoParser().ParseFileAsync(inputPath, OperationCancellationToken);
        MemInfoMetrics.Clear();
        MemInfoPssEntries.Clear();
        MemInfoDalvikEntries.Clear();
        MemInfoObjectEntries.Clear();
        MemInfoDiagnostics.Clear();

        if (parseResult.Report is { } report)
        {
            MemInfoProcessDescription = $"{report.ProcessName} (PID {report.ProcessId})";
            AddMemInfoMetric("Java Heap", report.Summary.JavaHeapKb);
            AddMemInfoMetric("Native Heap", report.Summary.NativeHeapKb);
            AddMemInfoMetric("Code", report.Summary.CodeKb);
            AddMemInfoMetric("Stack", report.Summary.StackKb);
            AddMemInfoMetric("Graphics", report.Summary.GraphicsKb);
            AddMemInfoMetric("Private Other", report.Summary.PrivateOtherKb);
            AddMemInfoMetric("System", report.Summary.SystemKb);
            AddMemInfoMetric("TOTAL", report.Summary.TotalPssKb);
            foreach (var entry in report.DetailedPssEntries)
            {
                MemInfoPssEntries.Add(new MemInfoPssOption(entry.Name, FormatMemInfoValue(entry.TotalPssKb), FormatMemInfoValue(entry.PrivateDirtyKb), FormatMemInfoValue(entry.PrivateCleanKb), FormatMemInfoValue(entry.SwapPssKb), FormatMemInfoValue(entry.RssKb), FormatMemInfoValue(entry.HeapSizeKb), FormatMemInfoValue(entry.HeapAllocKb), FormatMemInfoValue(entry.HeapFreeKb), entry.LineNumber.ToString()));
            }

            foreach (var entry in report.DalvikEntries)
            {
                MemInfoDalvikEntries.Add(new MemInfoNamedEntryOption(entry.Name, FormatMemInfoValue(entry.PssKb), entry.LineNumber.ToString()));
            }

            foreach (var entry in report.ObjectEntries)
            {
                MemInfoObjectEntries.Add(new MemInfoNamedEntryOption(entry.Name, entry.Count.ToString("N0"), entry.LineNumber.ToString()));
            }
        }
        else
        {
            MemInfoProcessDescription = "No valid Android meminfo report was produced.";
        }

        foreach (var diagnostic in parseResult.Diagnostics)
        {
            MemInfoDiagnostics.Add(new MemInfoDiagnosticOption(
                diagnostic.Severity.ToString(),
                diagnostic.Code,
                diagnostic.LineNumber is null ? "-" : diagnostic.LineNumber.Value.ToString(),
                diagnostic.Message));
        }

        MemInfoParsedAt = $"Parsed {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} · {parseResult.Diagnostics.Count} diagnostic(s)";
        StatusMessage = parseResult.IsSuccess
            ? "Meminfo parsing completed."
            : "Meminfo parsing completed with errors. Review the diagnostics.";
    });

    private void AddMemInfoMetric(string name, long? value) => MemInfoMetrics.Add(new MemInfoMetricOption(name, FormatMemInfoValue(value)));

    private static string FormatMemInfoValue(long? value) => value is null ? "Not found" : $"{value:N0} KB";

    private async Task RunAsync(string initialMessage, Func<IProgress<OperationProgress>, Task> operation)
    {
        using var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        IsBusy = true;
        StatusMessage = initialMessage;
        OperationStage = initialMessage;
        AddOperationLog("Info", initialMessage);
        try
        {
            await operation(new Progress<OperationProgress>(item =>
            {
                OperationStage = item.Stage;
                StatusMessage = item.Message;
                AddOperationLog(item.Stage, item.Message);
            }));
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
            OperationStage = "Error";
            AddOperationLog("Error", exception.Message);
        }
        finally
        {
            _operationCancellation = null;
            IsBusy = false;
        }
    }

    private void CancelCurrentOperation() => _operationCancellation?.Cancel();

    private void UpdateLaunchOperationSummary(string? remotePath = null)
    {
        if (_project is null || SelectedDevice?.IsAvailable != true)
        {
            LaunchOperationSummary = "请先打开工程并选择状态为 device 的设备。";
            return;
        }

        try
        {
            var target = TryResolveSelectedTarget(out var error);
            if (target is null)
            {
                LaunchOperationSummary = error ?? "无法确定操作目标。";
                return;
            }

            var resolvedPath = remotePath
                ?? CreateLaunchParameterService(SelectedDevice.Device).GetRemotePath(_project.Settings);
            LaunchOperationSummary = $"设备：{SelectedDevice.Id}{(SelectedDevice.HasAlias ? $"（{SelectedDevice.Alias}）" : string.Empty)}（{target.PlatformName}）{Environment.NewLine}" +
                                   $"启动目标：{target.LaunchTarget}{Environment.NewLine}" +
                                   (target.LaunchActivity is { Length: > 0 } activity ? $"Activity：{activity}{Environment.NewLine}" : string.Empty) +
                                   $"远端路径：{resolvedPath}";
        }
        catch (Exception exception)
        {
            LaunchOperationSummary = $"无法生成操作目标：{exception.Message}";
        }
    }

    /// <summary>
    /// 追加一条操作日志。时间戳在此统一生成，调用方不要自带时间前缀。
    /// 上限满时丢弃最旧一条，保证长时间运行不会无限增长。
    /// </summary>
    private void AddOperationLog(string category, string message)
    {
        OperationLogs.Add(new OperationLogEntry(DateTimeOffset.Now, category, message));
        const int maximumLogEntries = 2000;
        while (OperationLogs.Count > maximumLogEntries)
        {
            OperationLogs.RemoveAt(0);
        }
        RaiseOperationLogStates();
    }

    public string OperationLogCount => OperationLogs.Count == 0
        ? "暂无日志。"
        : $"{OperationLogs.Count} 条日志。";

    public bool HasOperationLogs => OperationLogs.Count > 0;

    public ICommand ClearOperationLogsCommand => _clearOperationLogsCommand;
    private readonly DelegateCommand _clearOperationLogsCommand;

    private void RaiseOperationLogStates()
    {
        OnPropertyChanged(nameof(OperationLogCount));
        OnPropertyChanged(nameof(HasOperationLogs));
        _clearOperationLogsCommand.RaiseCanExecuteChanged();
    }

    private void ClearOperationLogs()
    {
        OperationLogs.Clear();
        RaiseOperationLogStates();
        StatusMessage = "已清空操作日志。";
    }

    /// <summary>
    /// 将当前日志写入文本文件。目标路径由调用方（视图的保存对话框）给出，
    /// 覆盖确认交由对话框本身完成。
    /// </summary>
    public async Task SaveOperationLogsAsync(string outputPath)
    {
        var lines = OperationLogs.Select(entry => entry.ToString()).ToArray();
        await File.WriteAllLinesAsync(outputPath, lines);
        StatusMessage = $"已保存 {lines.Length} 条日志到 {outputPath}";
    }

    private async Task RefreshCaptureResultsAsync()
    {
        if (_project is null) return;
        IsBusy = true;
        OperationStage = "Listing captures";
        try
        {
            var service = new CaptureAnalysisService();

            // 作用域为「全部」时传 null，由服务列出全部平台目录。
            var captures = await service.ListCaptureDirectoriesAsync(
                _project, PlatformScope.IsAll ? null : PlatformScope.Name);
            CaptureResults.Clear();

            // 截断必须明说：静默只显示前 200 条会让「归档不在列表里」被读成「没采过」。
            const int displayLimit = 200;
            foreach (var capture in captures.Take(displayLimit))
            {
                CaptureResults.Add(capture);
            }

            SelectedCaptureResult = null;
            var scopeNote = PlatformScope.IsAll ? string.Empty : $"（仅 {PlatformScope.Name}）";
            var truncatedNote = captures.Count > displayLimit
                ? $"，共 {captures.Count} 个，仅显示最近 {displayLimit} 个"
                : string.Empty;
            CaptureResultsCount = $"找到 {CaptureResults.Count} 个 Capture{scopeNote}{truncatedNote}。";
            StatusMessage = CaptureResultsCount;
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
            CaptureResultsCount = $"Error: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
            OperationStage = "Idle";
        }
    }

    private async Task ViewCaptureResultFileAsync()
    {
        if (SelectedCaptureResultFile is null) return;
        IsBusy = true;
        OperationStage = "Parsing capture file";
        try
        {
            var filePath = SelectedCaptureResultFile.FullPath;
            var category = SelectedCaptureResultFile.Category;

            CaptureResultMetrics.Clear();
            MemReportMetrics.Clear();

            if (string.Equals(category, "MemInfo", StringComparison.OrdinalIgnoreCase))
            {
                var result = await new AndroidMemInfoParser().ParseFileAsync(filePath);
                if (result.IsSuccess && result.Report is not null)
                {
                    var summary = result.Report.Summary;
                    CaptureResultMetrics.Add(new MemInfoMetricOption("Process Name", result.Report.ProcessName ?? "-"));
                    CaptureResultMetrics.Add(new MemInfoMetricOption("Process ID", result.Report.ProcessId.ToString()));
                    CaptureResultMetrics.Add(new MemInfoMetricOption("Java Heap", (summary.JavaHeapKb?.ToString() ?? "N/A") + " KB"));
                    CaptureResultMetrics.Add(new MemInfoMetricOption("Native Heap", (summary.NativeHeapKb?.ToString() ?? "N/A") + " KB"));
                    CaptureResultMetrics.Add(new MemInfoMetricOption("Code", (summary.CodeKb?.ToString() ?? "N/A") + " KB"));
                    CaptureResultMetrics.Add(new MemInfoMetricOption("Stack", (summary.StackKb?.ToString() ?? "N/A") + " KB"));
                    CaptureResultMetrics.Add(new MemInfoMetricOption("Graphics", (summary.GraphicsKb?.ToString() ?? "N/A") + " KB"));
                    CaptureResultMetrics.Add(new MemInfoMetricOption("Private Other", (summary.PrivateOtherKb?.ToString() ?? "N/A") + " KB"));
                    CaptureResultMetrics.Add(new MemInfoMetricOption("System", (summary.SystemKb?.ToString() ?? "N/A") + " KB"));
                    CaptureResultMetrics.Add(new MemInfoMetricOption("TOTAL PSS", (summary.TotalPssKb?.ToString() ?? "N/A") + " KB"));
                }

                StatusMessage = "已解析 meminfo：" + filePath;
            }
            else if (string.Equals(category, "MemReport", StringComparison.OrdinalIgnoreCase))
            {
                // memreport 直接填进本页的 memreport 输入框并解析，
                // 不再把用户推到另一个页面重新选一次文件。
                MemReportInputPath = filePath;
                await ParseMemReportCoreAsync();
            }
            else
            {
                StatusMessage = $"文件类别「{category}」暂不支持内联查看；仅支持 MemInfo 与 MemReport。";
            }
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
            OperationStage = "Idle";
        }
    }

    private async Task ParseMemReportAsync()
    {
        if (string.IsNullOrWhiteSpace(MemReportInputPath)) return;
        IsBusy = true;
        OperationStage = "Parsing memreport";
        try
        {
            await ParseMemReportCoreAsync();
        }
        finally
        {
            IsBusy = false;
            OperationStage = "Idle";
        }
    }

    /// <summary>
    /// memreport 解析主体，不触碰 <see cref="IsBusy"/>／<see cref="OperationStage"/>。
    /// 供外层已经持有忙碌状态的流程（如从 Capture 文件内联查看）复用，
    /// 避免嵌套调用提前把忙碌状态清掉。
    /// </summary>
    private async Task ParseMemReportCoreAsync()
    {
        try
        {
            var result = await new UnrealMemReportParser().ParseFileAsync(MemReportInputPath);
            MemReportMetrics.Clear();
            MemReportSummaries.Clear();

            if (result.Report is not null)
            {
                MemReportParseDescription = "Changelist: " + result.Report.Changelist + " | Source: " + result.InputPath;
                MemReportParsedAt = "Parsed: " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");
                foreach (var metric in result.Report.Summary.Metrics)
                {
                    var value = metric.Status == UnrealMemReportMetricStatus.Parsed ? metric.ValueKb + " KB" :
                                metric.Status == UnrealMemReportMetricStatus.Missing ? "MISSING" : "INVALID (" + metric.RawValue + ")";
                    MemReportMetrics.Add(new MemReportMetricOption(metric.Group, metric.Name, value, metric.Status.ToString()));
                }

                if (result.Report.Textures.Count > 0)
                    MemReportSummaries.Add(new MemReportSummaryOption("Textures", result.Report.Textures.Count.ToString(), string.Empty));
                if (result.Report.RenderTargets.Count > 0)
                    MemReportSummaries.Add(new MemReportSummaryOption("Render Targets", result.Report.RenderTargets.Count.ToString(), string.Empty));
                if (result.Report.Objects.Count > 0)
                    MemReportSummaries.Add(new MemReportSummaryOption("Objects", result.Report.Objects.Count.ToString(), string.Empty));

                StatusMessage = "Parsed memreport: " + result.Report.Summary.Metrics.Count + " metrics, " + result.Report.Textures.Count + " textures";
            }
            else
            {
                MemReportParseDescription = "解析失败。";
                MemReportParsedAt = string.Empty;
                StatusMessage = "MemReport 解析失败。";
            }
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
            AddOperationLog("Error", $"MemReport 解析失败：{exception.Message}");
        }
    }

    private async Task ExportCaptureDataAsync()
    {
        if (string.IsNullOrWhiteSpace(ExportInputPath) || string.IsNullOrWhiteSpace(ExportOutputPath)) return;
        IsBusy = true;
        OperationStage = "Exporting";
        ExportProgress = "Exporting...";
        try
        {
            var isXlsx = ExportOutputPath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);
            var isMemReport = ExportInputPath.EndsWith(".memreport", StringComparison.OrdinalIgnoreCase);

            if (isMemReport)
            {
                var parseResult = await new UnrealMemReportParser().ParseFileAsync(ExportInputPath);
                if (!parseResult.IsSuccess) { StatusMessage = "MemReport parse failed; cannot export."; ExportProgress = "Failed."; return; }

                if (isXlsx)
                {
                    var result = await new XlsxMemReportExportService().ExportAsync(new MemReportExportRequest(parseResult, ExportOutputPath, DateTimeOffset.UtcNow, ExportIncludeDetails));
                    ExportProgress = "Exported to: " + result.OutputFilePath;
                }
                else
                {
                    var result = await new MemReportExportService().ExportAsync(new MemReportExportRequest(parseResult, ExportOutputPath, DateTimeOffset.UtcNow, ExportIncludeDetails));
                    ExportProgress = "Exported to: " + result.OutputFilePath;
                }
            }
            else
            {
                var parseResult = await new AndroidMemInfoParser().ParseFileAsync(ExportInputPath);
                if (!parseResult.IsSuccess) { StatusMessage = "MemInfo parse failed; cannot export."; ExportProgress = "Failed."; return; }

                if (isXlsx)
                {
                    var result = await new XlsxMemInfoExportService().ExportAsync(new MemInfoExportRequest(parseResult, ExportOutputPath, DateTimeOffset.UtcNow, ExportIncludeDetails));
                    ExportProgress = "Exported to: " + result.OutputFilePath;
                }
                else
                {
                    var result = await new MemInfoExportService().ExportAsync(new MemInfoExportRequest(parseResult, ExportOutputPath, DateTimeOffset.UtcNow, ExportIncludeDetails));
                    ExportProgress = "Exported to: " + result.OutputFilePath;
                }
            }

            StatusMessage = ExportProgress;
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
            ExportProgress = "Error: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
            OperationStage = "Idle";
        }
    }

    private async Task ParseStaticCameraAsync() => await RunAsync("Parsing static camera perf log...", async _ =>
    {
        var inputPath = Path.GetFullPath(ScpLogPath);
        if (!File.Exists(inputPath)) throw new FileNotFoundException("Static camera perf log not found.", inputPath);

        var parser = new StaticCameraPerfParser();
        StaticCameraPerfParseResult result;
        if (!string.IsNullOrWhiteSpace(ScpScreenshotsDir) && Directory.Exists(ScpScreenshotsDir))
            result = await parser.ParseFileAsync(inputPath, ScpScreenshotsDir, OperationCancellationToken);
        else
            result = await parser.ParseFileAsync(inputPath, OperationCancellationToken);

        _lastScpParseResult = result;
        ScpFrames.Clear();
        ScpAverages.Clear();
        ScpDiagnostics.Clear();

        if (result.Report is not null)
        {
            ScpParseDescription = $"Cameras: {result.Report.CameraCount} | Parsed: {result.Report.ParseCameraCount} | Status: {result.Report.Completeness}";
            foreach (var frame in result.Report.Frames)
            {
                ScpFrames.Add(new ScpFrameOption(
                    frame.Index, frame.CameraName,
                    frame.FrameTimeMs.ToString("F2"), frame.GameTimeMs.ToString("F2"),
                    frame.DrawTimeMs.ToString("F2"), frame.RhiTimeMs.ToString("F2"),
                    frame.GpuTimeMs.ToString("F2"), frame.MemoryBytes.ToString(),
                    frame.DrawCalls.ToString(), frame.Triangles.ToString(),
                    frame.Screenshots.Count, frame.FirstLineNumber));
            }

            var avg = result.Report.Average;
            ScpAverages.Add(new ScpAverageOption(
                avg.FrameTimeMs.ToString("F2"), avg.GameTimeMs.ToString("F2"),
                avg.DrawTimeMs.ToString("F2"), avg.RhiTimeMs.ToString("F2"),
                avg.GpuTimeMs.ToString("F2"), avg.MemoryBytes.ToString(),
                avg.DrawCalls.ToString(), avg.Triangles.ToString()));
        }
        else
        {
            ScpParseDescription = "Parse failed — see diagnostics.";
        }

        foreach (var diag in result.Diagnostics)
            ScpDiagnostics.Add(new ScpDiagnosticOption(diag.Severity.ToString(), diag.Code, diag.LineNumber?.ToString() ?? "-", diag.Message));

        StatusMessage = result.IsSuccess ? $"Parsed {result.Report?.ParseCameraCount ?? 0} camera(s) from {inputPath}" : "Static camera parse completed with errors.";
    });

    private async Task RunDiffAsync() => await RunAsync("Running baseline diff...", async _ =>
    {
        var source = DiffSource switch
        {
            "MemInfo" => BaselineDiffSource.MemInfo,
            "MemReport" => BaselineDiffSource.MemReport,
            _ => BaselineDiffSource.StaticCamera
        };

        var metricFilter = string.IsNullOrWhiteSpace(DiffMetricFilter)
            ? null
            : (IReadOnlyList<string>)DiffMetricFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var request = new BaselineDiffRequest(source, DiffBaselinePath, DiffCurrentPath, metricFilter, "Baseline", "Current");
        var service = new BaselineService();
        var result = await service.DiffAsync(request, OperationCancellationToken);

        DiffResults.Clear();
        DiffDiagnostics.Clear();

        foreach (var diff in result.Metrics)
        {
            DiffResults.Add(new DiffResultOption(
                diff.Group, diff.Name, diff.Unit, diff.Direction.ToString(),
                diff.BaselineValue?.ToString("F2") ?? "-",
                diff.CurrentValue?.ToString("F2") ?? "-",
                diff.Delta?.ToString("F2") ?? "-",
                diff.DeltaPercent?.ToString("F1") ?? "-",
                diff.Status.ToString(), diff.Assessment.ToString()));
        }

        foreach (var diag in result.Diagnostics)
            DiffDiagnostics.Add(new DiffDiagnosticOption(diag.Severity.ToString(), diag.Code, diag.LineNumber?.ToString() ?? "-", diag.Message));

        DiffSummary = result.IsSuccess
            ? $"Regressed: {result.RegressedCount} | Improved: {result.ImprovedCount} | Unchanged: {result.UnchangedCount} | Missing: {result.MissingCount}"
            : "Diff completed with errors.";

        StatusMessage = $"Diff: {result.Metrics.Count} metric(s) compared.";
    });

    private async Task RunTrendAsync() => await RunAsync("Building trend...", async _ =>
    {
        if (_project is null) { StatusMessage = "No project open."; return; }

        var source = TrendSource switch
        {
            "MemInfo" => BaselineDiffSource.MemInfo,
            "MemReport" => BaselineDiffSource.MemReport,
            _ => BaselineDiffSource.StaticCamera
        };

        var metricFilter = string.IsNullOrWhiteSpace(TrendMetricFilter)
            ? null
            : (IReadOnlyList<string>)TrendMetricFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        DateTimeOffset? from = DateTimeOffset.TryParse(TrendFrom, out var f) ? f : null;
        DateTimeOffset? to = DateTimeOffset.TryParse(TrendTo, out var t) ? t : null;

        // 趋势跨平台没有意义：Android 与 Win64 的内存指标量级与口径都不同，
        // 混在一条序列里的走势不可解读。作用域为「全部」时仍不强行选平台，
        // 由结果里的 Platform 列呈现事实，让用户看到需要收窄作用域。
        var request = new TrendRequest(_project, source,
            Platform: PlatformScope.IsAll ? null : PlatformScope.Name,
            Tag: string.IsNullOrWhiteSpace(TrendTag) ? null : TrendTag.Trim(),
            From: from, To: to,
            MetricFilter: metricFilter);

        var service = new TrendService();
        var result = await service.BuildTrendAsync(request, OperationCancellationToken);

        TrendCaptures.Clear();
        TrendSeries.Clear();
        TrendDiagnostics.Clear();
        _lastTrendResult = result;

        foreach (var capture in result.Captures)
            TrendCaptures.Add(new TrendCaptureOption(capture.CaptureId, capture.CaptureDate.ToString("yyyy-MM-dd HH:mm"), capture.Platform, capture.Tag, capture.DeviceModel ?? "-"));

        foreach (var series in result.Series)
        {
            TrendSeries.Add(new TrendSeriesOption(
                series.Group, series.Name, series.Unit, series.Direction.ToString(),
                series.PointCount, series.PresentCount, series.MissingCount,
                series.Minimum?.ToString("F2") ?? "-",
                series.Maximum?.ToString("F2") ?? "-",
                series.Average?.ToString("F2") ?? "-",
                series.First?.ToString("F2") ?? "-",
                series.Last?.ToString("F2") ?? "-",
                series.TotalDelta?.ToString("F2") ?? "-",
                series.TotalDeltaPercent?.ToString("F1") ?? "-",
                series.OverallAssessment.ToString()));
        }

        foreach (var diag in result.Diagnostics)
            TrendDiagnostics.Add(new TrendDiagnosticOption(diag.Severity.ToString(), diag.Code, diag.LineNumber?.ToString() ?? "-", diag.Message));

        TrendSummary = result.IsSuccess
            ? $"{result.Captures.Count} capture(s) | {result.Series.Count} series | Regressed: {result.RegressedCount} | Improved: {result.ImprovedCount}"
            : "Trend build completed with errors.";

        StatusMessage = $"Trend: {result.Series.Count} series across {result.Captures.Count} capture(s).";
    });

    private void OpenRenderDocOutputDir()
    {
        if (!string.IsNullOrWhiteSpace(RenderDocOutputDir) && Directory.Exists(RenderDocOutputDir))
            System.Diagnostics.Process.Start("explorer.exe", RenderDocOutputDir);
    }

    private async Task RunRenderDocAsync() => await RunAsync("Running RenderDoc script...", async _ =>
    {
        var request = new RenderDocExecutionRequest(
            PythonExecutable: RenderDocPythonPath,
            ScriptPath: RenderDocScriptPath,
            ScriptArguments: string.IsNullOrWhiteSpace(RenderDocArguments)
                ? []
                : RenderDocArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            OutputDirectory: string.IsNullOrWhiteSpace(RenderDocOutputDir) ? null : RenderDocOutputDir,
            WorkingDirectory: string.IsNullOrWhiteSpace(RenderDocWorkingDir) ? null : RenderDocWorkingDir,
            Timeout: int.TryParse(RenderDocTimeout, out var t) && t > 0 ? TimeSpan.FromSeconds(t) : null);

        var service = new RenderDocService(new ProcessRunner());
        var result = await service.ExecuteAsync(request, OperationCancellationToken);

        RenderDocStandardError = result.StandardError;
        RenderDocStandardOutput = result.StandardOutput;
        RenderDocDiagnostics.Clear();

        foreach (var diag in result.Diagnostics)
            RenderDocDiagnostics.Add(new RenderDocDiagnosticOption(
                diag.Severity.ToString(), diag.Code, diag.LineNumber?.ToString() ?? "-", diag.Message));

        RenderDocSummary = result.Succeeded
            ? $"Exit code: {result.ExitCode} | Duration: {result.Duration.TotalSeconds:F1}s | Output dir: {result.OutputDirectory ?? "(none)"}"
            : $"Exit code: {result.ExitCode} | Duration: {result.Duration.TotalSeconds:F1}s | Error: {result.StandardError.TrimEnd().Split('\n').LastOrDefault()?.Trim() ?? "unknown"}";

        var log = new StringBuilder();
        log.AppendLine("=== RenderDoc Script Output ===");
        log.AppendLine(result.StandardOutput);
        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            log.AppendLine("=== Standard Error ===");
            log.AppendLine(result.StandardError);
        }
        StatusMessage = $"RenderDoc finished (exit {result.ExitCode}).";
        AddOperationLog("RenderDoc", log.ToString().TrimEnd());
    });
    public async Task GenerateScpHtmlReportAsync(string outputPath)
    {
        if (_lastScpParseResult?.Report is null) return;
        await new StaticCameraHtmlReportService().GenerateAsync(
            new StaticCameraHtmlReportRequest(_lastScpParseResult, outputPath));
        AddOperationLog("Info", $"HTML report saved: {outputPath}");
    }

    private bool CanDownloadLatest() =>
        !IsBusy && _project is not null && !string.IsNullOrWhiteSpace(DownloadPlatform);

    private bool CanInstallDownloadedApk() =>
        !IsBusy
        && SelectedDevice?.IsAvailable == true
        && PlatformNames.TryParse(DownloadPlatform, out var platform)
        && platform == TargetPlatform.Android
        && !string.IsNullOrWhiteSpace(InstallApkPath);

    /// <summary>
    /// 本次安装要用的本地 APK。已选包优先；未选包回落列表中最新的可安装包；
    /// 列表尚为空（例如下载刚完成尚未刷新）时回落到最近一次下载得到的 APK。
    /// 只在三者都缺失时返回空并禁用安装，不隐式挑一台设备或一个未声明的路径。
    /// </summary>
    private string InstallApkPath =>
        SelectedDownloadedPackage?.LocalApkPath
        ?? DownloadedPackages.FirstOrDefault(package => package.IsInstallable)?.LocalApkPath
        ?? (DownloadedApkPath is { Length: > 0 } apk ? apk : string.Empty);

    /// <summary>
    /// 在资源管理器中打开当前平台的本地下载根目录。目录不存在时按钮已禁用，
    /// 因此这里只需做一次防御性判断。
    /// </summary>
    private Task OpenDownloadedDirectoryAsync()
    {
        var root = DownloadRootDirectory;
        if (root is { Length: > 0 } && Directory.Exists(root))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = root,
                UseShellExecute = true
            });
            StatusMessage = $"已打开下载目录：{root}";
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 从 FTP 下载所选平台的最新构建。下载落地 <c>Intermediate/Download/&lt;Platform&gt;</c>，
    /// 不写 Content/——构建产物是可重新获取的缓存，不是采集归档。
    /// </summary>
    private Task DownloadLatestAsync() => RunAsync("正在下载最新构建…", async progress =>
    {
        var platform = PlatformNames.Parse(DownloadPlatform, nameof(DownloadPlatform));
        var profile = _project!.Settings.ProfileFor(platform);
        if (profile is null)
        {
            throw new InvalidOperationException(
                $"工程尚未配置 {PlatformNames.ToName(platform)} 平台。请先在「Setting」中勾选该平台。");
        }

        var request = new DownloadRequest(
            platform,
            _project.Settings.FtpSettings,
            profile.FtpPath,
            Path.Combine(_project.IntermediateDir, "Download", PlatformNames.ToName(platform)));

        var result = await new FtpDownloadService(new FluentFtpClientFactory())
            .DownloadAsync(request, progress, OperationCancellationToken);

        // 成功/失败明细统一写入操作日志窗口，页面不再另设诊断列表，避免两处重复。
        foreach (var diagnostic in result.Diagnostics)
        {
            var fix = diagnostic.SuggestedFix is { Length: > 0 } fixText ? $"（{fixText}）" : string.Empty;
            AddOperationLog(diagnostic.Severity.ToString(), $"[{diagnostic.Code}] {diagnostic.Message}{fix}");
        }

        // Android 下载的是 apk 文件，记录其本地路径供「安装到设备」使用；Win64 下载的是目录。
        DownloadedApkPath = platform == TargetPlatform.Android && result.Succeeded
            ? result.LocalPath ?? string.Empty
            : string.Empty;

        var alreadyUpToDate = result.Diagnostics.Any(
            diagnostic => diagnostic.Code == DownloadDiagnosticCodes.AlreadyUpToDate);
        if (alreadyUpToDate)
        {
            DownloadSummary = $"本地已有最新构建 {result.SourceSubdir}，未重新下载。";
            StatusMessage = DownloadSummary;
            AddOperationLog("Info", DownloadSummary);
            await RefreshDownloadedPackagesAsync();
            return;
        }

        DownloadSummary = result.Succeeded
            ? $"下载完成：{result.SourceSubdir}（{result.FileCount} 个文件）→ {result.LocalPath}"
            : $"下载失败：{result.SourceSubdir ?? "(无)"}。详见操作日志。";
        AddOperationLog(result.Succeeded ? "Info" : "Error", DownloadSummary);

        if (result.Succeeded)
        {
            StatusMessage = $"下载完成：{result.LocalPath}";
            // 新下载的包立即进入本地列表，供后续选择安装。
            await RefreshDownloadedPackagesAsync();
        }
    });

    /// <summary>
    /// 把最近下载到的 APK 安装到所选 Android 设备。安装是破坏性操作，
    /// 执行前经确认服务展示完整目标设备与本地包路径。
    /// </summary>
    private Task InstallDownloadedApkAsync() => RunAsync("正在安装应用…", async progress =>
    {
        var device = SelectedDevice!;
        var deviceService = ResolveDeviceServiceForDevice(device.Device);
        if (deviceService.Platform != TargetPlatform.Android
            || !deviceService.Supports(DeviceCapability.InstallApplication))
        {
            throw new InvalidOperationException($"设备 {device.Id} 不支持安装应用包。");
        }

        var apkPath = InstallApkPath;
        if (!await _confirmationService.ConfirmInstallApplicationAsync(device.Id, apkPath))
        {
            StatusMessage = "已取消安装。";
            return;
        }

        var result = await deviceService.InstallApplicationAsync(device.Device, apkPath, progress, OperationCancellationToken);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"安装失败（exit {result.ExitCode}）：{result.StandardError}");
        }

        StatusMessage = $"已安装 {apkPath} 到 {device.Id}。";
    });

    /// <summary>
    /// 列出本地下载根目录下已下载的构建包，供用户选择指定包安装。
    /// 未打开工程时列表为空并说明原因，不读一个不存在的目录。
    /// </summary>
    private async Task RefreshDownloadedPackagesAsync()
    {
        DownloadedPackages.Clear();
        SelectedDownloadedPackage = null;
        RaiseCommandStates();

        if (_project is null)
        {
            DownloadFolderSummary = "请先打开工程以浏览本地已下载的构建包。";
            return;
        }

        if (!PlatformNames.TryParse(DownloadPlatform, out var platform))
        {
            DownloadFolderSummary = "请选择平台以浏览本地已下载的构建包。";
            return;
        }

        // 此时工程与平台均已确认有效，下载根目录必然非空（目录本身可能尚未创建）。
        var baseDirectory = DownloadRootDirectory!;
        var packages = await Task.Run(() => LocalDownloadCatalog.List(baseDirectory, platform));

        // 自然排序升序即旧→新，列表倒序展示让最新的包排在最上。
        foreach (var package in packages.Reverse())
        {
            DownloadedPackages.Add(new DownloadedPackageOption(
                package.FolderName, package.LocalApkPath, package.InstallBlockReason));
        }

        DownloadFolderSummary = DownloadedPackages.Count == 0
            ? $"本地暂无已下载的 {PlatformNames.ToName(platform)} 构建包。点击「下载最新」获取。"
            : $"本地已下载 {DownloadedPackages.Count} 个 {PlatformNames.ToName(platform)} 构建包，选择其一可安装到设备；未选择则安装最新。";
        RaiseCommandStates();
    }

    private void UpdateTrendChart()
    {
        TrendChartPoints.Clear();
        TrendChartXLabels.Clear();
        TrendChartSeriesNames.Clear();

        if (_lastTrendResult is null || string.IsNullOrEmpty(SelectedTrendChartSeries)) return;

        var series = _lastTrendResult.Series.FirstOrDefault(s =>
            $"{s.Group}/{s.Name}" == SelectedTrendChartSeries || s.Name == SelectedTrendChartSeries);
        if (series is null) return;

        // Populate series names for dropdown
        foreach (var s in _lastTrendResult.Series)
            TrendChartSeriesNames.Add($"{s.Group}/{s.Name}");

        if (TrendChartSeriesNames.Count > 0 && string.IsNullOrEmpty(SelectedTrendChartSeries))
            SelectedTrendChartSeries = TrendChartSeriesNames[0];

        var presentPoints = series.Points.Where(p => p.Value.HasValue).ToList();
        if (presentPoints.Count == 0) return;

        double minVal = presentPoints.Min(p => p.Value!.Value);
        double maxVal = presentPoints.Max(p => p.Value!.Value);
        double range = maxVal - minVal;
        if (range < 1e-9) range = 1;

        double chartWidth = 600;
        double chartHeight = 300;
        double paddingLeft = 60;
        double paddingRight = 20;
        double paddingTop = 20;
        double paddingBottom = 40;

        double plotWidth = chartWidth - paddingLeft - paddingRight;
        double plotHeight = chartHeight - paddingTop - paddingBottom;

        if (presentPoints.Count == 1)
        {
            double x = paddingLeft + plotWidth / 2;
            double y = paddingTop + plotHeight / 2;
            TrendChartPoints.Add(new System.Windows.Point(x, y));
        }
        else
        {
            for (int i = 0; i < presentPoints.Count; i++)
            {
                double x = paddingLeft + (i / (double)(presentPoints.Count - 1)) * plotWidth;
                double y = paddingTop + (1 - (presentPoints[i].Value!.Value - minVal) / range) * plotHeight;
                TrendChartPoints.Add(new System.Windows.Point(x, y));
            }
        }

        // X-axis labels (dates)
        int labelStep = Math.Max(1, presentPoints.Count / 6);
        for (int i = 0; i < presentPoints.Count; i += labelStep)
        {
            TrendChartXLabels.Add(new TrendChartAxisLabel(
                paddingLeft + (presentPoints.Count > 1 ? (i / (double)(presentPoints.Count - 1)) * plotWidth : plotWidth / 2),
                paddingTop + plotHeight + 5,
                presentPoints[i].CaptureDate.ToString("MM-dd")));
        }
    }

        public string ConsoleCommandText
    {
        get => _consoleCommandText;
        set { if (SetField(ref _consoleCommandText, value)) _sendConsoleCommandCommand.RaiseCanExecuteChanged(); }
    }

    public string ConsoleOutput
    {
        get => _consoleOutput;
        set => SetField(ref _consoleOutput, value ?? "Send a console command to the selected device.");
    }

    public bool ConsoleIsSending
    {
        get => _consoleIsSending;
        set => SetField(ref _consoleIsSending, value);
    }

    public string ConsoleSequenceName
    {
        get => _consoleSequenceName;
        set { if (SetField(ref _consoleSequenceName, value ?? string.Empty)) _runConsoleSequenceCommand.RaiseCanExecuteChanged(); }
    }

    public string ConsoleSequenceInlineCmds
    {
        get => _consoleSequenceInlineCmds;
        set => SetField(ref _consoleSequenceInlineCmds, value ?? string.Empty);
    }

    public string ConsoleSequenceOutput
    {
        get => _consoleSequenceOutput;
        set => SetField(ref _consoleSequenceOutput, value ?? "Run a sequence to see results here.");
    }

    public bool IsConsoleSequenceRunning
    {
        get => _isConsoleSequenceRunning;
        set => SetField(ref _isConsoleSequenceRunning, value);
    }

    public ICommand RunConsoleSequenceCommand => _runConsoleSequenceCommand;
    private AsyncDelegateCommand _runConsoleSequenceCommand;

    public ICommand SendConsoleCommandCommand => _sendConsoleCommandCommand;
    private readonly AsyncDelegateCommand _sendConsoleCommandCommand;

    public ICommand ApplyConsoleCommandPresetCommand => _applyConsoleCommandPresetCommand;
    private readonly ParameterizedAsyncDelegateCommand<ConsoleCommandPresetOption> _applyConsoleCommandPresetCommand;

    public ICommand RefreshConsoleCommandPresetValuesCommand => _refreshConsoleCommandPresetValuesCommand;
    private readonly AsyncDelegateCommand _refreshConsoleCommandPresetValuesCommand;

    /// <summary>
    /// 应用一条预设指令：Bool/Value 把值写下去并读回确认，Action 只执行。
    /// 写完立刻读回一次而不是信任「发送成功即已生效」：cvar 可能被引擎钳制到合法区间
    /// （例如 r.ScreenPercentage 有下限），读回才能显示真正落地的值。
    /// </summary>
    private async Task ApplyConsoleCommandPresetAsync(ConsoleCommandPresetOption option)
    {
        if (option is null || _selectedDevice is null) return;

        if (option.Kind == ConsoleCommandKind.Value && !IsNumeric(option.Value))
        {
            var invalid = $"「{option.Value}」不是有效数值，未发送。";
            option.SetStatus(invalid);
            ConsoleOutput = $"[SKIP] {option.Name}: {invalid}";
            return;
        }

        string command;
        try
        {
            command = option.Preset.BuildCommand(option.IsChecked, option.Value);
        }
        catch (InvalidOperationException exception)
        {
            // 预设配置本身不完整（缺 Cvar / Command / 取值）。就地提示，不发一条残缺指令出去。
            option.SetStatus(exception.Message);
            ConsoleOutput = exception.Message;
            return;
        }

        ConsoleIsSending = true;
        try
        {
            var consoleService = new ConsoleCommandService(ResolveDeviceServiceForDevice(_selectedDevice.Device));
            if (!consoleService.IsSupported)
            {
                ConsoleOutput = $"[SKIP] {_selectedDevice.Platform} 平台暂不支持发送 UE 控制台指令。";
                return;
            }

            var result = await consoleService.SendAsync(
                _selectedDevice.Id,
                ConsoleCommand.Create(command),
                TryResolveSelectedTarget(out _)?.ProcessIdentity,
                cancellationToken: OperationCancellationToken);

            ConsoleOutput = result.Succeeded
                ? $"[OK] {command}"
                : $"[FAIL] {command}{Environment.NewLine}Exit: {result.ExitCode}{Environment.NewLine}{result.StandardError}";

            if (result.Succeeded && option.SupportsReadBack)
            {
                await ReadBackAsync(consoleService, option);
            }
        }
        catch (Exception exception)
        {
            // 带上指令文本：连接层失败时只报「连不上」，用户无从判断这一条究竟发的是什么。
            ConsoleOutput = $"[ERROR] {command}{Environment.NewLine}{exception.Message}";
            option.SetStatus(exception.Message);
        }
        finally
        {
            ConsoleIsSending = false;
        }
    }

    /// <summary>
    /// 批量读回全部 Bool/Value 预设的当前值。
    /// 单项失败只写进那一项的当前值栏，不中断其余项：一个读不到的 cvar 不该让整次刷新作废。
    /// </summary>
    private async Task RefreshConsoleCommandPresetValuesAsync()
    {
        if (_selectedDevice is null) return;

        var readable = ConsoleCommandPresets.Where(option => option.SupportsReadBack).ToArray();
        if (readable.Length == 0)
        {
            ConsoleOutput = "没有可读回当前值的预设指令（Action 型没有当前值）。";
            return;
        }

        ConsoleIsSending = true;
        try
        {
            var consoleService = new ConsoleCommandService(ResolveDeviceServiceForDevice(_selectedDevice.Device));
            if (!consoleService.IsSupported)
            {
                ConsoleOutput = $"[SKIP] {_selectedDevice.Platform} 平台暂不支持读取 UE 控制台变量。";
                return;
            }

            var failed = 0;
            foreach (var option in readable)
            {
                OperationCancellationToken.ThrowIfCancellationRequested();
                if (!await ReadBackAsync(consoleService, option))
                {
                    failed++;
                }
            }

            ConsoleOutput = failed == 0
                ? $"已读回 {readable.Length} 项当前值。"
                : $"已读回 {readable.Length - failed} / {readable.Length} 项，{failed} 项读取失败（见各项当前值栏）。";
        }
        catch (OperationCanceledException)
        {
            ConsoleOutput = "读回已取消。";
        }
        catch (Exception exception)
        {
            ConsoleOutput = $"Error: {exception.Message}";
        }
        finally
        {
            ConsoleIsSending = false;
        }
    }

    /// <summary>读回一项并写回界面。返回是否成功，供批量刷新统计。</summary>
    private async Task<bool> ReadBackAsync(ConsoleCommandService consoleService, ConsoleCommandPresetOption option)
    {
        var value = await consoleService.QueryVariableAsync(
            _selectedDevice!.Id,
            option.Preset.Cvar!,
            option.Preset.ResolveVariableType(),
            cancellationToken: OperationCancellationToken);
        option.ApplyReadBack(value);
        return value.Succeeded;
    }

    private static bool IsNumeric(string text) =>
        double.TryParse(text?.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _);

    private async Task SendConsoleCommandAsync()
    {
        if (string.IsNullOrWhiteSpace(_consoleCommandText) || _selectedDevice is null) return;

        ConsoleIsSending = true;
        ConsoleOutput = $"Sending: {_consoleCommandText}...";
        try
        {
            // 单一路径：ConsoleCommandService 依赖 IDeviceService，平台差异由能力探测表达。
            var consoleService = new ConsoleCommandService(ResolveDeviceServiceForDevice(_selectedDevice.Device));
            if (!consoleService.IsSupported)
            {
                ConsoleOutput = $"[SKIP] {_selectedDevice.Platform} 平台暂不支持发送 UE 控制台指令。";
                return;
            }

            var result = await consoleService.SendAsync(
                _selectedDevice.Id,
                ConsoleCommand.Create(_consoleCommandText),
                TryResolveSelectedTarget(out _)?.ProcessIdentity,
                cancellationToken: OperationCancellationToken);

            ConsoleOutput = result.Succeeded
                ? $"[OK] {_consoleCommandText}{Environment.NewLine}Exit: {result.ExitCode}{Environment.NewLine}{result.StandardOutput}"
                : $"[FAIL] {_consoleCommandText}{Environment.NewLine}Exit: {result.ExitCode}{Environment.NewLine}{result.StandardError}";
        }
        catch (Exception ex)
        {
            ConsoleOutput = $"Error: {ex.Message}";
        }
        finally
        {
            ConsoleIsSending = false;
        }
    }

    private async Task RunConsoleSequenceAsync()
    {
        if (_selectedDevice is null) return;

        var consoleService = new ConsoleCommandService(ResolveDeviceServiceForDevice(_selectedDevice.Device));
        if (!consoleService.IsSupported)
        {
            ConsoleSequenceOutput = $"[SKIP] {_selectedDevice.Platform} 平台暂不支持执行 UE 控制台指令序列。";
            return;
        }

        IsConsoleSequenceRunning = true;

        try
        {
            CommandSequenceDefinition sequence;
            if (!string.IsNullOrWhiteSpace(_consoleSequenceName))
            {
                var preset = ConsoleSequencePresets.FirstOrDefault(s => string.Equals(s.Name, _consoleSequenceName, StringComparison.OrdinalIgnoreCase));
                if (preset is null) { ConsoleSequenceOutput = $"Preset sequence not found: {_consoleSequenceName}"; return; }
                sequence = preset.ToSequenceDefinition();
            }
            else if (!string.IsNullOrWhiteSpace(_consoleSequenceInlineCmds))
            {
                var preset = new ConsoleSequencePreset("inline", _consoleSequenceInlineCmds, string.Empty);
                sequence = preset.ToSequenceDefinition();
            }
            else { ConsoleSequenceOutput = "No sequence selected and no inline commands provided."; return; }

            ConsoleSequenceOutput = $"Running sequence: {sequence.Name} ({sequence.Steps.Count} steps)...{Environment.NewLine}";
            var request = new SequenceExecutionRequest(sequence, _selectedDevice.Id, TryResolveSelectedTarget(out _)?.ProcessIdentity);
            var result = await consoleService.RunSequenceAsync(request);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Completed: {result.SuccessfulSteps}/{result.TotalSteps} OK, {result.FailedSteps} failed. Duration: {result.Duration.TotalSeconds:F1}s");
            sb.AppendLine();
            foreach (var stepResult in result.StepResults)
            {
                var status = stepResult.Succeeded ? "OK" : "FAIL";
                var desc = stepResult.Step is { } step
                    ? step.Type switch
                    {
                        SequenceStepType.Command => $"CMD: {step.Command?.Command}",
                        SequenceStepType.Wait => $"WAIT: {step.WaitDuration?.TotalSeconds ?? 0:F1}s",
                        SequenceStepType.Tag => $"TAG: {step.Marker}",
                        SequenceStepType.Group => $"GROUP: {step.Marker}",
                        _ => step.Type.ToString()
                    }
                    : "(timeout/cancelled)";
                sb.AppendLine($"[{status}] Step {stepResult.StepIndex + 1}: {desc}");
                if (stepResult.CommandResult is { } cmdResult)
                    sb.AppendLine($"  Exit: {cmdResult.ExitCode}, Duration: {cmdResult.Duration.TotalMilliseconds:F0}ms");
                if (!string.IsNullOrWhiteSpace(stepResult.Error))
                    sb.AppendLine($"  Error: {stepResult.Error}");
            }
            ConsoleSequenceOutput = sb.ToString();
        }
        catch (Exception ex)
        {
            ConsoleSequenceOutput = $"Error: {ex.Message}";
        }
        finally
        {
            IsConsoleSequenceRunning = false;
        }
    }

    private void RaiseCommandStates()
    {
        foreach (var command in new[] { CreateProjectCommand, OpenProjectCommand, RefreshDevicesCommand, ConnectWirelessDeviceCommand, ShowDeviceIpAddressesCommand, PushLaunchParametersCommand, DeleteLaunchParametersCommand, StartApplicationCommand, RunCaptureCommand, DownloadDeviceSavedCommand, DownloadDeviceLogsCommand, SaveProjectSettingsCommand, ParseMemInfoCommand, RefreshCaptureResultsCommand, ViewCaptureResultFileCommand, ParseMemReportCommand, ExportCaptureDataCommand, ParseStaticCameraCommand, RunDiffCommand, RunTrendCommand, RunRenderDocCommand, _sendConsoleCommandCommand, _runConsoleSequenceCommand, DownloadCommand, InstallDownloadedApkCommand, OpenDownloadedDirectoryCommand, RefreshDownloadedPackagesCommand, _refreshConsoleCommandPresetValuesCommand }.OfType<AsyncDelegateCommand>())
        {
            command.RaiseCanExecuteChanged();
        }

        // 参数化命令不是 AsyncDelegateCommand，上面的 OfType 过滤覆盖不到它。
        _applyConsoleCommandPresetCommand.RaiseCanExecuteChanged();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed class RejectingConfirmationService : IUserConfirmationService
{
    public Task<bool> ConfirmDeleteLaunchParametersAsync(LaunchOperationTarget target) => Task.FromResult(false);

    public Task NotifyLastProjectUnavailableAsync(string projectFilePath, string reason) => Task.CompletedTask;

    public Task<bool> ConfirmInstallApplicationAsync(string deviceId, string localApplicationPath) => Task.FromResult(false);
}

public sealed class AsyncDelegateCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute();
    public async void Execute(object? parameter) => await ExecuteAsync();
    public Task ExecuteAsync() => execute();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// 接受 <c>CommandParameter</c> 的异步命令。
///
/// 与 <see cref="AsyncDelegateCommand"/> 分开而不是给它加参数重载：那个类的调用点遍布全局，
/// 且都不需要参数，改签名会波及全部调用而无收益。列表项按钮用
/// <c>CommandParameter="{Binding}"</c> 把项自身传回来，故此处需要参数。
/// </summary>
public sealed class ParameterizedAsyncDelegateCommand<T>(Func<T, Task> execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute();

    public async void Execute(object? parameter)
    {
        // 参数类型不符时静默返回：XAML 绑定写错不该让界面崩，
        // 由「点了没反应」暴露绑定问题，而不是抛成未处理异常。
        if (parameter is T typed)
        {
            await execute(typed);
        }
    }

    public Task ExecuteAsync(T parameter) => execute(parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class DelegateCommand(Action execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute();
    public void Execute(object? parameter) => execute();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
