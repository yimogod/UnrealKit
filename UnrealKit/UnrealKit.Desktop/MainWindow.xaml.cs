using System.Windows;
using UnrealKit.Core.Projects;
using UnrealKit.Desktop.Services;
using UnrealKit.Desktop.ViewModels;

namespace UnrealKit.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new ShellViewModel(new ProjectService(), new DesktopAdbServiceFactory(), new WpfUserConfirmationService(this));
    }
}
