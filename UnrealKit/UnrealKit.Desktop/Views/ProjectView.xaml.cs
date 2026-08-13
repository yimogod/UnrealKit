using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using UnrealKit.Desktop.ViewModels;

namespace UnrealKit.Desktop.Views;

public partial class ProjectView : UserControl
{
    public ProjectView()
    {
        InitializeComponent();
    }

    private void BrowseNewProjectDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择空目录或新工程目录",
            Multiselect = false
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true && DataContext is ShellViewModel viewModel)
        {
            viewModel.NewProjectDirectory = dialog.FolderName;
        }
    }

    private void BrowseProjectFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开 UnrealKit 工程",
            Filter = "UnrealKit 工程 (*.ukit)|*.ukit|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true && DataContext is ShellViewModel viewModel)
        {
            viewModel.ProjectFilePath = dialog.FileName;
        }
    }
}
