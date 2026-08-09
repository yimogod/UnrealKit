using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using UnrealKit.Core.Adb;
using UnrealKit.Core.Capture;
using UnrealKit.Core.Launch;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Export;
using UnrealKit.Core.Parsing;
using UnrealKit.Core.Processes;
using System.Linq;
using UnrealKit.Core.Projects;
using UnrealKit.Core.Analysis;
using System.Text;
using UnrealKit.Core.RenderDoc;

namespace UnrealKit.Desktop;

public sealed class ShellViewModel : INotifyPropertyChanged
{
    private readonly IProjectService _projectService;
    private readonly IDesktopAdbServiceFactory _adbServiceFactory;
    private readonly IUserConfirmationService _confirmationService;
    private string _selectedNavigationItem;
    private string _statusMessage = "未打开工程。";
    private string _projectFilePath = string.Empty;
    private string _newProjectDirectory = string.Empty;
    private string _newProjectName = string.Empty;
    private string _wirelessEndpoint = string.Empty;
    private string _customLaunchArguments = string.Empty;
    private string _remoteCommandLinePath = string.Empty;
    private string _captureTag = string.Empty;
    private string _captureArchivePreview = "请先打开工程并选择状态为 device 的设备。";
    private string _packageName = string.Empty;
    private string _unrealProjectName = string.Empty;
    private string _activity = string.Empty;
    private string _deviceSavedRootTemplate = string.Empty;
    private string _adbPath = string.Empty;
    private string _memInfoInputPath = string.Empty;
    private string _memInfoProcessDescription = "Select a meminfo text file to begin offline parsing.";
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
    private UkitProject? _project;
    private AdbDevice? _selectedDevice;
    private CaptureFileInfo? _selectedCaptureResultFile;
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
        IUserConfirmationService confirmationService)
    {
        _projectService = projectService ?? throw new ArgumentNullException(nameof(projectService));
        _adbServiceFactory = adbServiceFactory ?? throw new ArgumentNullException(nameof(adbServiceFactory));
        _confirmationService = confirmationService ?? throw new ArgumentNullException(nameof(confirmationService));
        NavigationItems = ["工程", "设备", "启动参数", "采集", "解析", "结果", "导出", "静态相机", "基线差分", "历史趋势", "RenderDoc", "日志与设置"];
        _selectedNavigationItem = NavigationItems[0];
        CreateProjectCommand = new AsyncDelegateCommand(CreateProjectAsync, CanCreateProject);
        OpenProjectCommand = new AsyncDelegateCommand(OpenProjectAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(ProjectFilePath));
        RefreshDevicesCommand = new AsyncDelegateCommand(RefreshDevicesAsync, () => !IsBusy);
        ConnectWirelessDeviceCommand = new AsyncDelegateCommand(ConnectWirelessDeviceAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(WirelessEndpoint));
        PushLaunchParametersCommand = new AsyncDelegateCommand(PushLaunchParametersAsync, CanOperateOnSelectedDevice);
        DeleteLaunchParametersCommand = new AsyncDelegateCommand(DeleteLaunchParametersAsync, CanOperateOnSelectedDevice);
        StartApplicationCommand = new AsyncDelegateCommand(StartApplicationAsync, CanOperateOnSelectedDevice);
        RunCaptureCommand = new AsyncDelegateCommand(RunCaptureAsync, CanOperateOnSelectedDevice);
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
        ExportCaptureDataCommand = new AsyncDelegateCommand(ExportCaptureDataAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(ExportInputPath) && !string.IsNullOrWhiteSpace(ExportOutputPath));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<string> NavigationItems { get; }
    public ObservableCollection<AdbDevice> Devices { get; } = [];
    public ObservableCollection<LaunchParameterPresetOption> LaunchParameterPresets { get; } = [];
    public ObservableCollection<MemInfoMetricOption> MemInfoMetrics { get; } = [];
    public ObservableCollection<MemInfoPssOption> MemInfoPssEntries { get; } = [];
    public ObservableCollection<MemInfoNamedEntryOption> MemInfoDalvikEntries { get; } = [];
    public ObservableCollection<MemInfoNamedEntryOption> MemInfoObjectEntries { get; } = [];
    public ObservableCollection<MemInfoDiagnosticOption> MemInfoDiagnostics { get; } = [];
    public ObservableCollection<CaptureDirectoryInfo> CaptureResults { get; } = [];
    public ObservableCollection<CaptureFileInfo> CaptureResultFiles { get; } = [];
    public ObservableCollection<MemInfoMetricOption> CaptureResultMetrics { get; } = [];
    public ObservableCollection<MemReportMetricOption> MemReportMetrics { get; } = [];
    public ObservableCollection<MemReportSummaryOption> MemReportSummaries { get; } = [];
    public ObservableCollection<string> OperationLogs { get; } = [];
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
    public ICommand CreateProjectCommand { get; }
    public ICommand OpenProjectCommand { get; }
    public ICommand RefreshDevicesCommand { get; }
    public ICommand ConnectWirelessDeviceCommand { get; }
    public ICommand PushLaunchParametersCommand { get; }
    public ICommand DeleteLaunchParametersCommand { get; }
    public ICommand StartApplicationCommand { get; }
    public ICommand RunCaptureCommand { get; }
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
        "工程" => "创建或打开 .ukit 工程后，设备与启动参数页面将共享工程配置。",
        "设备" => "刷新 ADB 设备并明确选择目标设备；不会依赖默认第一台设备。",
        "启动参数" => "选择预设并预览 uecommandline.txt，然后推送到已明确选择的设备。",
        "采集" => "将采集数据归档到新的 Content Capture，避免覆盖历史数据。",
        "解析" => "明确选择输入文件，查看格式诊断和解析结果。",
        "结果" => "查看摘要、筛选表格并将派生结果导出到 Saved。",
        "导出" => "选择解析结果，指定输出格式和路径，导出 CSV/TSV/XLSX。",
        _ => "查看可复制日志与应用设置。"
    };

    public string StatusMessage { get => _statusMessage; private set => SetField(ref _statusMessage, value); }
    public string ProjectFilePath { get => _projectFilePath; set { if (SetField(ref _projectFilePath, value)) RaiseCommandStates(); } }
    public string NewProjectDirectory { get => _newProjectDirectory; set { if (SetField(ref _newProjectDirectory, value)) RaiseCommandStates(); } }
    public string NewProjectName { get => _newProjectName; set { if (SetField(ref _newProjectName, value)) RaiseCommandStates(); } }
    public string WirelessEndpoint { get => _wirelessEndpoint; set { if (SetField(ref _wirelessEndpoint, value)) RaiseCommandStates(); } }
    public string CustomLaunchArguments { get => _customLaunchArguments; set { if (SetField(ref _customLaunchArguments, value)) UpdateLaunchParameterPreview(); } }
    public string RemoteCommandLinePath { get => _remoteCommandLinePath; set { if (SetField(ref _remoteCommandLinePath, value)) UpdateLaunchParameterPreview(); } }
    public string CaptureTag { get => _captureTag; set { if (SetField(ref _captureTag, value)) UpdateCaptureArchivePreview(); } }
    public string CaptureArchivePreview { get => _captureArchivePreview; private set => SetField(ref _captureArchivePreview, value); }
    public string PackageName { get => _packageName; set => SetField(ref _packageName, value); }
    public string UnrealProjectName { get => _unrealProjectName; set => SetField(ref _unrealProjectName, value); }
    public string Activity { get => _activity; set => SetField(ref _activity, value); }
    public string DeviceSavedRootTemplate { get => _deviceSavedRootTemplate; set => SetField(ref _deviceSavedRootTemplate, value); }
    public string AdbPath { get => _adbPath; set => SetField(ref _adbPath, value); }
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
    public string LaunchParameterPreview { get => _launchParameterPreview; private set => SetField(ref _launchParameterPreview, value); }
    public string LaunchOperationSummary { get => _launchOperationSummary; private set => SetField(ref _launchOperationSummary, value); }
    public string OperationStage { get => _operationStage; private set => SetField(ref _operationStage, value); }
    public string ProjectTitle => _project is null ? "当前工程：未打开" : $"当前工程：{_project.Descriptor.ProjectName}";
    public bool IsBusy { get => _isBusy; private set { if (SetField(ref _isBusy, value)) RaiseCommandStates(); } }

    public AdbDevice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (!SetField(ref _selectedDevice, value)) return;
            OnPropertyChanged(nameof(SelectedDeviceDescription));
            UpdateCaptureArchivePreview();
            UpdateLaunchOperationSummary();
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

    public string SelectedDeviceDescription => SelectedDevice is null
        ? "尚未选择设备。"
        : $"{SelectedDevice.SerialNumber} · {SelectedDevice.Status} · {SelectedDevice.Model ?? SelectedDevice.DeviceName ?? "未知型号"}";

    private bool CanCreateProject() =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(NewProjectDirectory) &&
        !string.IsNullOrWhiteSpace(NewProjectName);

    private bool CanOperateOnSelectedDevice() => !IsBusy && _project is not null && SelectedDevice?.IsAvailable == true;

    private Task CreateProjectAsync() => RunAsync("正在创建工程…", async progress =>
    {
        var result = await _projectService.CreateProjectAsync(new CreateProjectRequest(NewProjectDirectory, NewProjectName), progress, OperationCancellationToken);
        SetCurrentProject(result.Project);
        StatusMessage = $"已创建工程：{result.Project.ProjectFilePath}";
    });

    private Task OpenProjectAsync() => RunAsync("正在打开工程…", async progress =>
    {
        var project = await _projectService.OpenProjectAsync(ProjectFilePath, progress, OperationCancellationToken);
        SetCurrentProject(project);
        StatusMessage = $"已打开工程：{project.ProjectFilePath}";
    });

    private Task RefreshDevicesAsync() => RunAsync("正在刷新 ADB 设备…", async progress =>
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

    private async Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(IProgress<OperationProgress> progress, CancellationToken cancellationToken) =>
        await CreateAdbService().ListDevicesAsync(progress, cancellationToken);

    private IAdbService CreateAdbService()
    {
        return _adbServiceFactory.Create(_project?.Settings, new Progress<ProcessOutput>(output =>
            AddOperationLog($"{output.Timestamp:HH:mm:ss} [{output.Stream}] {output.Text}")));
    }

        private void UpdateDevices(IReadOnlyList<AdbDevice> devices)
    {
        Devices.Clear();
        foreach (var device in devices) Devices.Add(device);
        var available = devices.Where(device => device.IsAvailable).ToArray();
        if (available.Length == 1)
        {
            SelectedDevice = available[0];
            StatusMessage = $"????????????{available[0].SerialNumber} ({available[0].Model ?? "unknown model"})?";
        }
        else
        {
            SelectedDevice = null;
            StatusMessage = devices.Count == 0 ? "??? ADB ???" : $"??? {devices.Count} ??????????????";
        }
    }
    private void SetCurrentProject(UkitProject project)
    {
        _project = project;
        ProjectFilePath = project.ProjectFilePath;
        Devices.Clear();
        SelectedDevice = null;
        LaunchParameterPresets.Clear();
        foreach (var preset in project.Settings.LaunchParameterPresets)
        {
            var option = new LaunchParameterPresetOption(preset);
            option.PropertyChanged += (_, _) => UpdateLaunchParameterPreview();
            LaunchParameterPresets.Add(option);
        }

        RemoteCommandLinePath = new LaunchParameterService(CreateAdbService()).GetRemotePath(project.Settings);
        CaptureTag = project.Settings.DefaultCaptureTag;
        PackageName = project.Settings.PackageName;
        UnrealProjectName = project.Settings.UnrealProjectName;
        Activity = project.Settings.Activity;
        DeviceSavedRootTemplate = project.Settings.DeviceSavedRootTemplate;
        AdbPath = project.Settings.AdbPath;
        OnPropertyChanged(nameof(ProjectTitle));
        UpdateLaunchParameterPreview();
        UpdateLaunchOperationSummary();
        UpdateCaptureArchivePreview();
        RaiseCommandStates();
    }

    private Task SaveProjectSettingsAsync() => RunAsync("正在保存项目默认配置…", async progress =>
    {
        var settings = _project!.Settings with
        {
            PackageName = PackageName.Trim(),
            UnrealProjectName = UnrealProjectName.Trim(),
            Activity = Activity.Trim(),
            DeviceSavedRootTemplate = DeviceSavedRootTemplate.Trim(),
            AdbPath = AdbPath.Trim(),
            DefaultCaptureTag = CaptureTag.Trim()
        };
        SetCurrentProject(await _projectService.UpdateSettingsAsync(_project, settings, progress, OperationCancellationToken));
        StatusMessage = "项目默认配置已保存。";
    });

    private void UpdateLaunchParameterPreview()
    {
        if (_project is null)
        {
            LaunchParameterPreview = "请先打开工程以加载启动参数预设。";
            return;
        }

        try
        {
            var service = new LaunchParameterService(CreateAdbService());
            var content = service.BuildContent(_project.Settings, GetSelectedPresetNames(), CustomLaunchArguments);
            var remotePath = service.GetRemotePath(_project.Settings, RemoteCommandLinePath);
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
        var result = await new LaunchParameterService(CreateAdbService()).PushAsync(
            _project!,
            new LaunchParameterRequest(SelectedDevice!.SerialNumber, GetSelectedPresetNames(), CustomLaunchArguments, RemoteCommandLinePath),
            progress,
            OperationCancellationToken);
        StatusMessage = $"已推送启动参数到：{result.RemotePath}";
        UpdateLaunchParameterPreview();
    });

    private Task DeleteLaunchParametersAsync() => RunAsync("正在删除 uecommandline.txt…", async progress =>
    {
        var remotePath = new LaunchParameterService(CreateAdbService()).GetRemotePath(_project!.Settings, RemoteCommandLinePath);
        var target = new LaunchOperationTarget(SelectedDevice!.SerialNumber, _project.Settings.PackageName, _project.Settings.Activity, remotePath);
        if (!await _confirmationService.ConfirmDeleteLaunchParametersAsync(target))
        {
            StatusMessage = "已取消删除设备启动参数。";
            return;
        }
        await new LaunchParameterService(CreateAdbService()).DeleteAsync(_project, SelectedDevice!.SerialNumber, RemoteCommandLinePath, progress, OperationCancellationToken);
        StatusMessage = $"已删除设备上的启动参数：{remotePath}";
    });

    private Task StartApplicationAsync() => RunAsync("正在启动应用…", async progress =>
    {
        await new LaunchParameterService(CreateAdbService()).StartApplicationAsync(_project!, SelectedDevice!.SerialNumber, progress, OperationCancellationToken);
        StatusMessage = $"已发送应用启动请求：{_project!.Settings.PackageName}/{_project.Settings.Activity}";
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
            var plan = new CaptureService(CreateAdbService()).CreatePlan(new CaptureRequest(_project, SelectedDevice, CaptureTag));
            CaptureArchivePreview = $"归档目录：{plan.CaptureDirectory}{Environment.NewLine}设备 Saved：{plan.DeviceSavedDirectory}";
        }
        catch (Exception exception)
        {
            CaptureArchivePreview = $"无法生成归档预览：{exception.Message}";
        }
    }

    private Task RunCaptureAsync() => RunAsync("正在采集并归档原始数据…", async progress =>
    {
        var request = new CaptureRequest(_project!, SelectedDevice!, CaptureTag);
        var result = await new CaptureService(CreateAdbService()).CaptureAsync(request, progress, OperationCancellationToken);
        CaptureArchivePreview = $"归档目录：{result.Plan.CaptureDirectory}{Environment.NewLine}清单：{result.ManifestPath}";
        StatusMessage = $"采集完成：{result.Plan.CaptureDirectory}";
    });

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
        AddOperationLog($"{DateTimeOffset.Now:HH:mm:ss} [Info] {initialMessage}");
        try
        {
            await operation(new Progress<OperationProgress>(item =>
            {
                OperationStage = item.Stage;
                StatusMessage = item.Message;
                AddOperationLog($"{DateTimeOffset.Now:HH:mm:ss} [{item.Stage}] {item.Message}");
            }));
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
            OperationStage = "Error";
            AddOperationLog($"{DateTimeOffset.Now:HH:mm:ss} [Error] {exception.Message}");
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
            var resolvedPath = remotePath ?? new LaunchParameterService(CreateAdbService()).GetRemotePath(_project.Settings, RemoteCommandLinePath);
            LaunchOperationSummary = $"设备：{SelectedDevice.SerialNumber}{Environment.NewLine}" +
                                   $"包名：{_project.Settings.PackageName}{Environment.NewLine}" +
                                   $"Activity：{_project.Settings.Activity}{Environment.NewLine}" +
                                   $"远端路径：{resolvedPath}";
        }
        catch (Exception exception)
        {
            LaunchOperationSummary = $"无法生成操作目标：{exception.Message}";
        }
    }

    private void AddOperationLog(string message)
    {
        OperationLogs.Add(message);
        const int maximumLogEntries = 300;
        if (OperationLogs.Count > maximumLogEntries)
        {
            OperationLogs.RemoveAt(0);
        }
    }

    private async Task RefreshCaptureResultsAsync()
    {
        if (_project is null) return;
        IsBusy = true;
        OperationStage = "Listing captures";
        try
        {
            var service = new CaptureAnalysisService();
            var captures = await service.ListCaptureDirectoriesAsync(_project);
            CaptureResults.Clear();
            foreach (var capture in captures.Take(200))
            {
                CaptureResults.Add(capture);
            }

            CaptureResultsCount = $"{CaptureResults.Count} capture(s) found.";
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

                StatusMessage = "Parsed meminfo: " + filePath;
            }
            else
            {
                StatusMessage = "File category '" + category + "' not supported for inline viewing. Use the Parse page for memreport files.";
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
                MemReportParseDescription = "Parse failed.";
                MemReportParsedAt = string.Empty;
                StatusMessage = "MemReport parse failed.";
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

        var request = new TrendRequest(_project, source,
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
        AddOperationLog(log.ToString());
    });
    public async Task GenerateScpHtmlReportAsync(string outputPath)
    {
        if (_lastScpParseResult?.Report is null) return;
        await new StaticCameraHtmlReportService().GenerateAsync(
            new StaticCameraHtmlReportRequest(_lastScpParseResult, outputPath));
        AddOperationLog($"HTML report saved: {outputPath}");
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

    private void RaiseCommandStates()
    {
        foreach (var command in new[] { CreateProjectCommand, OpenProjectCommand, RefreshDevicesCommand, ConnectWirelessDeviceCommand, PushLaunchParametersCommand, DeleteLaunchParametersCommand, StartApplicationCommand, RunCaptureCommand, SaveProjectSettingsCommand, ParseMemInfoCommand, RefreshCaptureResultsCommand, ViewCaptureResultFileCommand, ParseMemReportCommand, ExportCaptureDataCommand, ParseStaticCameraCommand, RunDiffCommand, RunTrendCommand, RunRenderDocCommand }.OfType<AsyncDelegateCommand>())
        {
            command.RaiseCanExecuteChanged();
        }
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
}

public sealed record MemInfoMetricOption(string Name, string Value);

public sealed record MemInfoPssOption(string Name, string TotalPss, string PrivateDirty, string PrivateClean, string SwapPss, string Rss, string HeapSize, string HeapAlloc, string HeapFree, string Line);

public sealed record MemInfoNamedEntryOption(string Name, string Value, string Line);

public sealed record MemInfoDiagnosticOption(string Severity, string Code, string Line, string Message);

public sealed class AsyncDelegateCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute();
    public async void Execute(object? parameter) => await ExecuteAsync();
    public Task ExecuteAsync() => execute();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed record MemReportMetricOption(string Group, string Name, string Value, string Status);

public sealed record MemReportSummaryOption(string Category, string Count, string Details);

public sealed class DelegateCommand(Action execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute();
    public void Execute(object? parameter) => execute();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class LaunchParameterPresetOption(LaunchParameterPreset preset) : INotifyPropertyChanged
{
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Name => preset.Name;
    public string Arguments => preset.Arguments;
    public string Description => preset.Description;
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
