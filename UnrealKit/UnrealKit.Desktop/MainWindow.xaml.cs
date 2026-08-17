using System.Windows;
using UnrealKit.Core.Projects;
using UnrealKit.Desktop.Services;
using UnrealKit.Desktop.ViewModels;
using UnrealKit.Desktop.Views;

namespace UnrealKit.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new ShellViewModel(new ProjectService(), new DesktopAdbServiceFactory(), new WpfUserConfirmationService(this));
    }

    private void OpenOperationLog_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel viewModel)
        {
            OperationLogWindow.Show(this, viewModel);
        }
    }
}
