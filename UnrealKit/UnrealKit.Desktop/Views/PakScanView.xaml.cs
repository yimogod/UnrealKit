using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using UnrealKit.Desktop.ViewModels;

namespace UnrealKit.Desktop.Views;

public partial class PakScanView : UserControl
{
    public PakScanView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ShellViewModel old)
            old.PropertyChanged -= OnVmPropertyChanged;
        if (e.NewValue is ShellViewModel vm)
            vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ShellViewModel.SelectedTextureBitmap)) return;
        if (sender is not ShellViewModel vm) return;
        if (vm.SelectedPakTexture is null) return;
        if (!TexturePreviewWindow.IsOpen) return;

        var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
        TexturePreviewWindow.Show(owner, vm.SelectedPakTexture, vm.SelectedTextureBitmap, vm.LastDecodedTexturePng, vm.TexturePreviewStatus);
    }

    private void OpenTexturePreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel vm) return;
        if (vm.SelectedPakTexture is null) return;
        var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
        TexturePreviewWindow.Show(owner, vm.SelectedPakTexture, vm.SelectedTextureBitmap, vm.LastDecodedTexturePng, vm.TexturePreviewStatus);
    }

    private void TextureDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is not ShellViewModel vm) return;
        if (vm.SelectedPakTexture is null) return;
        var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
        TexturePreviewWindow.Show(owner, vm.SelectedPakTexture, vm.SelectedTextureBitmap, vm.LastDecodedTexturePng, vm.TexturePreviewStatus);
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
