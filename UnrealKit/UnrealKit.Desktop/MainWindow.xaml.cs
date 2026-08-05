using System.Windows;
using Microsoft.Win32;

namespace UnrealKit.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new ShellViewModel();
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
}
