using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using UnrealKit.Desktop.Models;

namespace UnrealKit.Desktop.Views;

public partial class TexturePreviewWindow : Window
{
    private static TexturePreviewWindow? _instance;
    private byte[]? _pngBytes;

    private TexturePreviewWindow(Window owner)
    {
        InitializeComponent();
        Owner = owner;
    }

    /// <summary>打开或刷新预览窗口；已打开时更新内容并激活，不新建。</summary>
    public static void Show(Window owner, PakScanTextureOption texture, BitmapSource? bitmap, byte[]? pngBytes, string status)
    {
        if (_instance is null)
        {
            _instance = new TexturePreviewWindow(owner);
            _instance.Closed += (_, _) => _instance = null;
            _instance.Show();
        }
        else
        {
            if (_instance.WindowState == WindowState.Minimized)
                _instance.WindowState = WindowState.Normal;
            _instance.Activate();
        }

        _instance.Update(texture, bitmap, pngBytes, status);
    }

    private void Update(PakScanTextureOption texture, BitmapSource? bitmap, byte[]? pngBytes, string status)
    {
        _pngBytes = pngBytes;

        Title = $"纹理预览 — {texture.Name}";
        TitleText.Text = texture.Name;

        NameText.Text     = texture.Name;
        PathText.Text     = texture.Path;
        SizeText.Text     = $"{texture.SizeX} × {texture.SizeY}";
        FormatText.Text   = texture.Format;
        MipsText.Text     = texture.NumMips.ToString();
        LodGroupText.Text = texture.LodGroup;
        LodBiasText.Text  = texture.LodBias.ToString();
        SizeMbText.Text   = $"{texture.EstimatedSizeMB} MB";

        if (bitmap is not null)
        {
            PreviewImage.Source = bitmap;
            StatusText.Visibility = Visibility.Collapsed;
            FooterText.Text = $"{texture.SizeX} × {texture.SizeY}  {texture.Format}  {texture.EstimatedSizeMB} MB";
        }
        else
        {
            PreviewImage.Source = null;
            StatusText.Text = string.IsNullOrWhiteSpace(status) ? "预览不可用" : status;
            StatusText.Visibility = Visibility.Visible;
            FooterText.Text = status;
        }

        ExportButton.IsEnabled = pngBytes is { Length: > 0 };
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pngBytes is not { Length: > 0 }) return;

        var dialog = new SaveFileDialog
        {
            Title    = "导出纹理 PNG",
            Filter   = "PNG 图像 (*.png)|*.png",
            DefaultExt = ".png",
            FileName = $"{NameText.Text}.png",
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog(this) != true) return;

        File.WriteAllBytes(dialog.FileName, _pngBytes);
    }
}
