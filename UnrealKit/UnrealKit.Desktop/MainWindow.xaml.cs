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

    private void BrowseScpLog_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select static camera perf log",
            Filter = "Log files (*.log)|*.log|Text files (*.txt)|*.txt|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true && DataContext is ShellViewModel viewModel)
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

        if (dialog.ShowDialog(this) == true && DataContext is ShellViewModel viewModel)
        {
            viewModel.ScpScreenshotsDir = dialog.FolderName;
        }
    }

    private void BrowseDiffBaseline_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select baseline report",
            Filter = "All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true && DataContext is ShellViewModel viewModel)
        {
            viewModel.DiffBaselinePath = dialog.FileName;
        }
    }

    private void BrowseDiffCurrent_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select current report",
            Filter = "All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true && DataContext is ShellViewModel viewModel)
        {
            viewModel.DiffCurrentPath = dialog.FileName;
        }
    }

    private void BrowseRenderDocPython_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Python executable",
            Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true && DataContext is ShellViewModel viewModel)
        {
            viewModel.RenderDocPythonPath = dialog.FileName;
        }
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

        if (dialog.ShowDialog(this) == true && DataContext is ShellViewModel viewModel)
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

        if (dialog.ShowDialog(this) == true && DataContext is ShellViewModel viewModel)
        {
            viewModel.RenderDocOutputDir = dialog.FolderName;
        }
    }
}