using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using UnrealKit.Desktop.ViewModels;

namespace UnrealKit.Desktop.Views;

public partial class MemInfoParseView : UserControl
{
    public MemInfoParseView()
    {
        InitializeComponent();
    }

    private void BrowseMemInfoFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Android meminfo output",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true && DataContext is ShellViewModel viewModel)
        {
            viewModel.MemInfoInputPath = dialog.FileName;
        }
    }

    private void BrowseMemReportFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 UE memreport 文件",
            Filter = "MemReport 文件 (*.memreport)|*.memreport|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true && DataContext is ShellViewModel viewModel)
        {
            viewModel.MemReportInputPath = dialog.FileName;
        }
    }


}
