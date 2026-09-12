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

    private void OpenStaticMeshPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel vm) return;
        if (vm.SelectedPakStaticMesh is null) return;

        var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
        MeshPreviewWindow.Show(owner, vm.SelectedPakStaticMesh.Name, vm.MeshPreviewGlbPath, vm.MeshPreviewStatus);
    }

    private void OpenSkeletalMeshPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel vm) return;
        if (vm.SelectedPakSkeletalMesh is null) return;

        var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
        MeshPreviewWindow.Show(owner, vm.SelectedPakSkeletalMesh.Name, vm.MeshPreviewGlbPath, vm.MeshPreviewStatus);
    }
}
