using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace UnrealKit.Desktop;

public sealed class ShellViewModel : INotifyPropertyChanged
{
    private string _selectedNavigationItem;
    private string _statusMessage = "未打开工程。";

    public ShellViewModel()
    {
        NavigationItems = ["工程", "设备", "启动参数", "采集", "解析", "结果", "日志与设置"];
        _selectedNavigationItem = NavigationItems[0];
        CreateProjectCommand = new DelegateCommand(() => StatusMessage = "工程创建对话框将在下一批 UI 工作中接入 ProjectService。");
        OpenProjectCommand = new DelegateCommand(() => StatusMessage = "工程打开对话框将在下一批 UI 工作中接入 ProjectService。");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<string> NavigationItems { get; }
    public ICommand CreateProjectCommand { get; }
    public ICommand OpenProjectCommand { get; }

    public string SelectedNavigationItem
    {
        get => _selectedNavigationItem;
        set
        {
            if (_selectedNavigationItem == value) return;
            _selectedNavigationItem = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PageDescription));
        }
    }

    public string PageDescription => SelectedNavigationItem switch
    {
        "工程" => "创建、打开、校验 UnrealKit 工程，并显示配置来源。",
        "设备" => "选择明确的 ADB 设备；不会依赖默认第一台设备。",
        "启动参数" => "编辑预设并预览将写入设备的启动参数。",
        "采集" => "将采集数据归档到新的 Content Capture，避免覆盖历史数据。",
        "解析" => "明确选择输入文件，查看格式诊断和解析结果。",
        "结果" => "查看摘要、筛选表格并将派生结果导出到 Saved。",
        _ => "查看可复制日志与应用设置。"
    };

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value) return;
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class DelegateCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}
