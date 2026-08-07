using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using UnrealKit.Core.Adb;
using UnrealKit.Core.Capture;
using UnrealKit.Core.Launch;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Parsing;
using UnrealKit.Core.Processes;
using UnrealKit.Core.Projects;

namespace UnrealKit.Desktop;

public sealed class ShellViewModel : INotifyPropertyChanged
{
    private readonly IProjectService _projectService = new ProjectService();
    private readonly AdbPathResolver _adbPathResolver = new();
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
    private string _launchParameterPreview = "请先打开工程以加载启动参数预设。";
    private UkitProject? _project;
    private AdbDevice? _selectedDevice;
    private bool _isBusy;

    public ShellViewModel()
    {
        NavigationItems = ["工程", "设备", "启动参数", "采集", "解析", "结果", "日志与设置"];
        _selectedNavigationItem = NavigationItems[0];
        CreateProjectCommand = new AsyncDelegateCommand(CreateProjectAsync, CanCreateProject);
        OpenProjectCommand = new AsyncDelegateCommand(OpenProjectAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(ProjectFilePath));
        RefreshDevicesCommand = new AsyncDelegateCommand(RefreshDevicesAsync, () => !IsBusy);
        ConnectWirelessDeviceCommand = new AsyncDelegateCommand(ConnectWirelessDeviceAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(WirelessEndpoint));
        PushLaunchParametersCommand = new AsyncDelegateCommand(PushLaunchParametersAsync, CanOperateOnSelectedDevice);
        DeleteLaunchParametersCommand = new AsyncDelegateCommand(DeleteLaunchParametersAsync, CanOperateOnSelectedDevice);
        StartApplicationCommand = new AsyncDelegateCommand(StartApplicationAsync, CanOperateOnSelectedDevice);
        RunCaptureCommand = new AsyncDelegateCommand(RunCaptureAsync, CanOperateOnSelectedDevice);
        SaveProjectSettingsCommand = new AsyncDelegateCommand(SaveProjectSettingsAsync, () => !IsBusy && _project is not null);
        ParseMemInfoCommand = new AsyncDelegateCommand(ParseMemInfoAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(MemInfoInputPath));
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
    public ICommand CreateProjectCommand { get; }
    public ICommand OpenProjectCommand { get; }
    public ICommand RefreshDevicesCommand { get; }
    public ICommand ConnectWirelessDeviceCommand { get; }
    public ICommand PushLaunchParametersCommand { get; }
    public ICommand DeleteLaunchParametersCommand { get; }
    public ICommand StartApplicationCommand { get; }
    public ICommand RunCaptureCommand { get; }
    public ICommand SaveProjectSettingsCommand { get; }
    public ICommand ParseMemInfoCommand { get; }

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
    public string LaunchParameterPreview { get => _launchParameterPreview; private set => SetField(ref _launchParameterPreview, value); }
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
        var result = await _projectService.CreateProjectAsync(new CreateProjectRequest(NewProjectDirectory, NewProjectName), progress);
        SetCurrentProject(result.Project);
        StatusMessage = $"已创建工程：{result.Project.ProjectFilePath}";
    });

    private Task OpenProjectAsync() => RunAsync("正在打开工程…", async progress =>
    {
        var project = await _projectService.OpenProjectAsync(ProjectFilePath, progress);
        SetCurrentProject(project);
        StatusMessage = $"已打开工程：{project.ProjectFilePath}";
    });

    private Task RefreshDevicesAsync() => RunAsync("正在刷新 ADB 设备…", async progress =>
    {
        var devices = await ListDevicesAsync(progress);
        UpdateDevices(devices);
    });

    private Task ConnectWirelessDeviceAsync() => RunAsync("正在连接 Wi-Fi ADB 设备…", async progress =>
    {
        var service = CreateAdbService();
        await service.ConnectAsync(WirelessEndpoint.Trim(), progress);
        var devices = await ListDevicesAsync(progress);
        UpdateDevices(devices);
        StatusMessage = $"已连接 {WirelessEndpoint.Trim()}，请从列表中明确选择目标设备。";
    });

    private async Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(IProgress<OperationProgress> progress) =>
        await CreateAdbService().ListDevicesAsync(progress);

    private AdbService CreateAdbService()
    {
        var adbPath = _adbPathResolver.ResolveRequired(null, _project?.Settings.AdbPath);
        return new AdbService(new ProcessRunner(), adbPath);
    }

    private void UpdateDevices(IReadOnlyList<AdbDevice> devices)
    {
        Devices.Clear();
        foreach (var device in devices) Devices.Add(device);
        SelectedDevice = devices.Count(device => device.IsAvailable) == 1 ? devices.Single(device => device.IsAvailable) : null;
        StatusMessage = devices.Count == 0 ? "未发现 ADB 设备。" : $"已发现 {devices.Count} 台设备，请明确选择可用设备。";
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
        SetCurrentProject(await _projectService.UpdateSettingsAsync(_project, settings, progress));
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
        }
        catch (Exception exception)
        {
            LaunchParameterPreview = $"无法生成启动参数：{exception.Message}";
        }
    }

    private IReadOnlyList<string> GetSelectedPresetNames() => LaunchParameterPresets.Where(option => option.IsSelected).Select(option => option.Name).ToArray();

    private Task PushLaunchParametersAsync() => RunAsync("正在推送 uecommandline.txt…", async progress =>
    {
        var result = await new LaunchParameterService(CreateAdbService()).PushAsync(
            _project!,
            new LaunchParameterRequest(SelectedDevice!.SerialNumber, GetSelectedPresetNames(), CustomLaunchArguments, RemoteCommandLinePath),
            progress);
        StatusMessage = $"已推送启动参数到：{result.RemotePath}";
        UpdateLaunchParameterPreview();
    });

    private Task DeleteLaunchParametersAsync() => RunAsync("正在删除 uecommandline.txt…", async progress =>
    {
        var remotePath = new LaunchParameterService(CreateAdbService()).GetRemotePath(_project!.Settings, RemoteCommandLinePath);
        await new LaunchParameterService(CreateAdbService()).DeleteAsync(_project, SelectedDevice!.SerialNumber, RemoteCommandLinePath, progress);
        StatusMessage = $"已删除设备上的启动参数：{remotePath}";
    });

    private Task StartApplicationAsync() => RunAsync("正在启动应用…", async progress =>
    {
        await new LaunchParameterService(CreateAdbService()).StartApplicationAsync(_project!, SelectedDevice!.SerialNumber, progress);
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
        var result = await new CaptureService(CreateAdbService()).CaptureAsync(request, progress);
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

        var parseResult = await new AndroidMemInfoParser().ParseFileAsync(inputPath);
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
        IsBusy = true;
        StatusMessage = initialMessage;
        try
        {
            await operation(new Progress<OperationProgress>(item => StatusMessage = item.Message));
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RaiseCommandStates()
    {
        foreach (var command in new[] { CreateProjectCommand, OpenProjectCommand, RefreshDevicesCommand, ConnectWirelessDeviceCommand, PushLaunchParametersCommand, DeleteLaunchParametersCommand, StartApplicationCommand, RunCaptureCommand, SaveProjectSettingsCommand, ParseMemInfoCommand }.OfType<AsyncDelegateCommand>())
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

public sealed record MemInfoMetricOption(string Name, string Value);

public sealed record MemInfoPssOption(string Name, string TotalPss, string PrivateDirty, string PrivateClean, string SwapPss, string Rss, string HeapSize, string HeapAlloc, string HeapFree, string Line);

public sealed record MemInfoNamedEntryOption(string Name, string Value, string Line);

public sealed record MemInfoDiagnosticOption(string Severity, string Code, string Line, string Message);

public sealed class AsyncDelegateCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute();
    public async void Execute(object? parameter) => await execute();
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
