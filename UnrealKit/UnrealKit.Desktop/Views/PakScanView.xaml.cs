using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using UnrealKit.Desktop.ViewModels;

namespace UnrealKit.Desktop.Views;

public partial class PakScanView : UserControl
{
    public PakScanView()
    {
        InitializeComponent();
    }

    private void BrowsePakDir_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择游戏包目录（含 .pak / .utoc / .ucas 文件）",
            Multiselect = false
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true && DataContext is ShellViewModel viewModel)
        {
            viewModel.PakScanInputPath = dialog.FolderName;
        }
    }
}
