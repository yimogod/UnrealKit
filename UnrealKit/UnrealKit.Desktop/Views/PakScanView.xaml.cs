using System.Windows;
using System.Windows.Controls;
using UnrealKit.Desktop.ViewModels;

namespace UnrealKit.Desktop.Views;

public partial class PakScanView : UserControl
{
    public PakScanView()
    {
        InitializeComponent();
    }

    private void OpenTexturePreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel vm) return;
        if (vm.SelectedPakTexture is null) return;

        var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
        TexturePreviewWindow.Show(
            owner,
            vm.SelectedPakTexture,
            vm.SelectedTextureBitmap,
            vm.LastDecodedTexturePng,
            vm.TexturePreviewStatus);
    }
}
