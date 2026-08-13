using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using UnrealKit.Desktop;

namespace UnrealKit.Desktop.Views;

public partial class BaselineDiffView : UserControl
{
    public BaselineDiffView()
    {
        InitializeComponent();
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

        if (dialog.ShowDialog(Window.GetWindow(this)) == true && DataContext is ShellViewModel viewModel)
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

        if (dialog.ShowDialog(Window.GetWindow(this)) == true && DataContext is ShellViewModel viewModel)
        {
            viewModel.DiffCurrentPath = dialog.FileName;
        }
    }
}
