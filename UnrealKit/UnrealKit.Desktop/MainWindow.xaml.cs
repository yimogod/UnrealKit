using System.Windows;
using Microsoft.Win32;
using UnrealKit.Core.Projects;
using UnrealKit.Desktop.Services;
using UnrealKit.Desktop.ViewModels;
using UnrealKit.Desktop.Views;

namespace UnrealKit.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new ShellViewModel(new ProjectService(), new DesktopAdbServiceFactory(), new WpfUserConfirmationService(this));
        Loaded += MainWindow_Loaded;
    }

    /// <summary>
    /// 启动时恢复上次的界面状态。放在 Loaded 而不是构造函数：
    /// 工程不可用时要弹提示框，此时主窗口必须已经显示，否则提示框没有可靠的父窗口。
    /// </summary>
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // 只在首次显示时恢复，之后用户切换的工程与作用域不该被再次覆盖。
        Loaded -= MainWindow_Loaded;
        if (DataContext is ShellViewModel viewModel)
        {
            // 先恢复作用域再打开工程：反过来会让工程打开后的设备刷新按「全部」执行一遍，
            // 随后作用域到位又刷一遍，用户看到列表闪一下。
            await viewModel.RestorePlatformScopeAsync();
            await viewModel.RestoreLastProjectAsync();
        }
    }

    /// <summary>
    /// 打开工程：选定 .ukit 后直接执行打开，不再有独立的「工程」页面承载路径输入。
    /// </summary>
    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel viewModel) return;

        var dialog = new OpenFileDialog
        {
            Title = "打开 UnrealKit 工程",
            Filter = "UnrealKit 工程 (*.ukit)|*.ukit|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true) return;

        viewModel.ProjectFilePath = dialog.FileName;
        await viewModel.OpenProjectAsync();
    }

    private void CreateProject_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel viewModel)
        {
            CreateProjectWindow.ShowDialog(this, viewModel);
        }
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel viewModel)
        {
            SettingsWindow.Show(this, viewModel);
        }
    }

    private void OpenOperationLog_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel viewModel)
        {
            OperationLogWindow.Show(this, viewModel);
        }
    }
}
