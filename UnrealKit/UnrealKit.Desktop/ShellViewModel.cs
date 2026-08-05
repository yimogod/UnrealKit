using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using UnrealKit.Core.Adb;
using UnrealKit.Core.Operations;
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
    private UkitProject? _project;
    private AdbDevice? _selectedDevice;
    private bool _isBusy;

    public ShellViewModel()
    {
        NavigationItems = ["工程", "设备", "启动参数", "采集", "解析", "结果", "日志与设置"];
        _selectedNavigationItem = NavigationItems[0];
        OpenProjectCommand = new AsyncDelegateCommand(OpenProjectAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(ProjectFilePath));
        RefreshDevicesCommand = new AsyncDelegateCommand(RefreshDevicesAsync, () => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<string> NavigationItems { get; }
    public ObservableCollection<AdbDevice> Devices { get; } = [];
    public ICommand OpenProjectCommand { get; }
    public ICommand RefreshDevicesCommand { get; }

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
        "工程" => "打开 .ukit 工程后，设备与启动参数页面将共享工程配置。",
        "设备" => "刷新 ADB 设备并明确选择目标设备；不会依赖默认第一台设备。",
        "启动参数" => "启动参数 Core 服务将在设备页面稳定后接入。",
        "采集" => "将采集数据归档到新的 Content Capture，避免覆盖历史数据。",
        "解析" => "明确选择输入文件，查看格式诊断和解析结果。",
        "结果" => "查看摘要、筛选表格并将派生结果导出到 Saved。",
        _ => "查看可复制日志与应用设置。"
    };

    public string StatusMessage { get => _statusMessage; private set => SetField(ref _statusMessage, value); }
    public string ProjectFilePath { get => _projectFilePath; set { if (SetField(ref _projectFilePath, value)) RaiseCommandStates(); } }
    public string ProjectTitle => _project is null ? "当前工程：未打开" : $"当前工程：{_project.Descriptor.ProjectName}";
    public bool IsBusy { get => _isBusy; private set { if (SetField(ref _isBusy, value)) RaiseCommandStates(); } }

    public AdbDevice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (!SetField(ref _selectedDevice, value)) return;
            OnPropertyChanged(nameof(SelectedDeviceDescription));
        }
    }

    public string SelectedDeviceDescription => SelectedDevice is null
        ? "尚未选择设备。"
        : $"{SelectedDevice.SerialNumber} · {SelectedDevice.Status} · {SelectedDevice.Model ?? SelectedDevice.DeviceName ?? "未知型号"}";

    private Task OpenProjectAsync() => RunAsync("正在打开工程…", async progress =>
    {
        _project = await _projectService.OpenProjectAsync(ProjectFilePath, progress);
        Devices.Clear();
        SelectedDevice = null;
        OnPropertyChanged(nameof(ProjectTitle));
        StatusMessage = $"已打开工程：{_project.ProjectFilePath}";
    });

    private Task RefreshDevicesAsync() => RunAsync("正在刷新 ADB 设备…", async progress =>
    {
        var adbPath = _adbPathResolver.ResolveRequired(null, _project?.Settings.AdbPath);
        var service = new AdbService(new ProcessRunner(), adbPath);
        var devices = await service.ListDevicesAsync(progress);
        Devices.Clear();
        foreach (var device in devices) Devices.Add(device);
        SelectedDevice = devices.Count(device => device.IsAvailable) == 1 ? devices.Single(device => device.IsAvailable) : null;
        StatusMessage = devices.Count == 0 ? "未发现 ADB 设备。" : $"已发现 {devices.Count} 台设备，请明确选择可用设备。";
    });

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
        foreach (var command in new[] { OpenProjectCommand, RefreshDevicesCommand }.OfType<AsyncDelegateCommand>())
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

public sealed class AsyncDelegateCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute();
    public async void Execute(object? parameter) => await execute();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
