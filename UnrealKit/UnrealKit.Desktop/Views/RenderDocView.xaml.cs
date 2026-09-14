using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using UnrealKit.Desktop.ViewModels;

namespace UnrealKit.Desktop.Views;

public partial class RenderDocView : UserControl
{
    public RenderDocView()
    {
        InitializeComponent();
    }

    private void BrowseRenderDocScript_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select RenderDoc Python script",
            Filter = "Python scripts (*.py)|*.py|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true && DataContext is ShellViewModel viewModel)
        {
            viewModel.RenderDocScriptPath = dialog.FileName;
        }
    }

    private void BrowseRenderDocOutputDir_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select output directory",
            Multiselect = false
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true && DataContext is ShellViewModel viewModel)
        {
            viewModel.RenderDocOutputDir = dialog.FolderName;
        }
    }

    private void BrowseRenderDocWorkingDir_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select working directory",
            Multiselect = false
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true && DataContext is ShellViewModel viewModel)
        {
            viewModel.RenderDocWorkingDir = dialog.FolderName;
        }
    }
}
