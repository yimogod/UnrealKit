using System.Windows;
using Microsoft.Win32;
using UnrealKit.Core.Projects;

namespace UnrealKit.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new ShellViewModel(new ProjectService(), new DesktopAdbServiceFactory(), new WpfUserConfirmationService(this));
    }

    private void BrowseNewProjectDirectory_Click(object sender, RoutedEventArgs e)
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

    private void BrowseProjectFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开 UnrealKit 工程",
            Filter = "UnrealKit 工程 (*.ukit)|*.ukit|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true && DataContext is ShellViewModel viewModel)
        {
            viewModel.ProjectFilePath = dialog.FileName;
        }
    }

    private void BrowseExportInput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select input file for export",
            Filter = "Text files (*.txt)|*.txt|MemReport files (*.memreport)|*.memreport|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true && DataContext is ShellViewModel viewModel)
        {
            viewModel.ExportInputPath = dialog.FileName;
        }
    }

    private void BrowseExportOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save exported results",
            Filter = "CSV files (*.csv)|*.csv|TSV files (*.tsv)|*.tsv|XLSX files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
            DefaultExt = ".xlsx",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) == true && DataContext is ShellViewModel viewModel)
        {
            viewModel.ExportOutputPath = dialog.FileName;
        }
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

        if (dialog.ShowDialog(this) == true && DataContext is ShellViewModel viewModel)
        {
            viewModel.MemInfoInputPath = dialog.FileName;
        }
    }
}
