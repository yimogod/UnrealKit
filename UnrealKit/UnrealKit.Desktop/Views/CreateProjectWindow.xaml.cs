using System.Windows;
using Microsoft.Win32;
using UnrealKit.Desktop.ViewModels;

namespace UnrealKit.Desktop.Views;

/// <summary>
/// 创建工程对话框。与主窗口共享同一个 <see cref="ShellViewModel"/>，
/// 因此输入与 <c>CreateProjectCommand</c> 的可用性判断和主窗口完全一致。
/// </summary>
public partial class CreateProjectWindow : Window
{
    private CreateProjectWindow(Window owner, ShellViewModel viewModel)
    {
        InitializeComponent();
        Owner = owner;
        DataContext = viewModel;
    }

    /// <summary>以模态方式打开创建工程对话框。</summary>
    public static void ShowDialog(Window owner, ShellViewModel viewModel)
    {
        new CreateProjectWindow(owner, viewModel).ShowDialog();
    }

    private void BrowseDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择空目录或新工程目录",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true && DataContext is ShellViewModel viewModel)
        {
            viewModel.NewProjectDirectory = dialog.FolderName;
        }
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        // 只关窗：创建动作由按钮上的 CreateProjectCommand 执行，进度和结果落在主窗口状态栏与操作日志。
        Close();
    }
}
