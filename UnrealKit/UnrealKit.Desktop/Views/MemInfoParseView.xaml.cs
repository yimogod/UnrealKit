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

    private void BrowseExportInput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要导出的解析输入",
            Filter = "支持的输入 (*.txt;*.memreport)|*.txt;*.memreport|meminfo 文本 (*.txt)|*.txt|MemReport 文件 (*.memreport)|*.memreport|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true && DataContext is ShellViewModel viewModel)
        {
            viewModel.ExportInputPath = dialog.FileName;
        }
    }

    private void BrowseExportOutput_Click(object sender, RoutedEventArgs e)
    {
        // 导出格式由扩展名决定，因此这里用对话框限定候选，避免用户手打出
        // 与内容不符的扩展名（.xlsx 必须是真实工作簿，制表符文本用 .tsv）。
        var dialog = new SaveFileDialog
        {
            Title = "导出到",
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx|制表符文本 (*.tsv)|*.tsv",
            DefaultExt = ".xlsx",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true && DataContext is ShellViewModel viewModel)
        {
            viewModel.ExportOutputPath = dialog.FileName;
        }
    }
}
