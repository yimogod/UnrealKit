using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using UnrealKit.Desktop;

namespace UnrealKit.Desktop.Views;

public partial class StaticCameraView : UserControl
{
    public StaticCameraView()
    {
        InitializeComponent();
    }

    private void BrowseScpLog_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select static camera perf log",
            Filter = "Log files (*.log)|*.log|Text files (*.txt)|*.txt|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true && DataContext is ShellViewModel viewModel)
        {
            viewModel.ScpLogPath = dialog.FileName;
        }
    }

    private void BrowseScpScreenshots_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select screenshots directory",
            Multiselect = false
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true && DataContext is ShellViewModel viewModel)
        {
            viewModel.ScpScreenshotsDir = dialog.FolderName;
        }
    }
}
