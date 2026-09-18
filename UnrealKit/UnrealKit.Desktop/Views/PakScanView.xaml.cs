using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        if (sender is not ShellViewModel vm) return;

        if (e.PropertyName == nameof(ShellViewModel.SelectedTextureBitmap))
        {
            if (vm.SelectedPakTexture is null) return;
            if (!TexturePreviewWindow.IsOpen) return;
            var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
            TexturePreviewWindow.Show(owner, vm.SelectedPakTexture, vm.SelectedTextureBitmap, vm.LastDecodedTexturePng, vm.TexturePreviewStatus);
        }
        else if (e.PropertyName is nameof(ShellViewModel.MeshPreviewGlbPath) or nameof(ShellViewModel.MeshPreviewStatus))
        {
            if (!MeshPreviewWindow.IsOpen) return;
            var meshName = vm.SelectedPakStaticMesh?.Name ?? vm.SelectedPakSkeletalMesh?.Name ?? string.Empty;
            var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
            MeshPreviewWindow.Show(owner, meshName, vm.MeshPreviewGlbPath, vm.MeshPreviewStatus);
        }
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

    private void StaticMeshDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
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

    private void SkeletalMeshDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is not ShellViewModel vm) return;
        if (vm.SelectedPakSkeletalMesh is null) return;
        var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
        MeshPreviewWindow.Show(owner, vm.SelectedPakSkeletalMesh.Name, vm.MeshPreviewGlbPath, vm.MeshPreviewStatus);
    }

    private void TextureDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (DataContext is not ShellViewModel vm) return;
        e.Handled = true;
        ApplyDataGridSort(e, vm.PakTextures);
    }

    private void StaticMeshDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (DataContext is not ShellViewModel vm) return;
        e.Handled = true;
        ApplyDataGridSort(e, vm.PakStaticMeshes);
    }

    private void SkeletalMeshDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (DataContext is not ShellViewModel vm) return;
        e.Handled = true;
        ApplyDataGridSort(e, vm.PakSkeletalMeshes);
    }

    private void MaterialDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (DataContext is not ShellViewModel vm) return;
        e.Handled = true;
        ApplyDataGridSort(e, vm.PakMaterials);
    }

    private void MapMeshUsageDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (DataContext is not ShellViewModel vm) return;
        e.Handled = true;
        ApplyDataGridSort(e, vm.PakMapMeshUsages);
    }

    private void MapMeshAggregateDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (DataContext is not ShellViewModel vm) return;
        e.Handled = true;
        ApplyDataGridSort(e, vm.PakMapMeshAggregates);
    }

    private void MapLocalMeshUsageDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (DataContext is not ShellViewModel vm) return;
        e.Handled = true;
        ApplyDataGridSort(e, vm.PakMapLocalMeshUsages);
    }

    // Cycles: none → ascending → descending → ascending …
    private static void ApplyDataGridSort<T>(DataGridSortingEventArgs e, PagedSearchList<T> list)
        where T : class
    {
        var header = e.Column.Header?.ToString() ?? string.Empty;
        bool descending = e.Column.SortDirection == ListSortDirection.Ascending;
        e.Column.SortDirection = descending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        list.ApplySort(header, descending);
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {

    }
}
