using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using UnrealKit.Desktop.Models;

namespace UnrealKit.Desktop.Views;

public partial class TexturePreviewWindow : Window
{
    private static TexturePreviewWindow? _instance;
    private byte[]? _pngBytes;
    private BitmapSource? _sourceBitmap;
    private bool _hasAlpha;

    // 格式中含有 alpha 通道的关键字
    private static readonly HashSet<string> AlphaFormats =
    [
        "DXT5", "BC3", "BC7", "DXT3", "BC2",
        "RGBA", "BGRA", "R8G8B8A8", "B8G8R8A8",
        "PF_DXT5", "PF_BC7", "PF_B8G8R8A8", "PF_FloatRGBA",
    ];

    public static bool IsOpen => _instance is not null;

    private TexturePreviewWindow(Window owner)
    {
        InitializeComponent();
        Owner = owner;
    }

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
        _sourceBitmap = bitmap;
        _hasAlpha = HasAlphaChannel(texture.Format, bitmap);

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

        // 无 alpha 通道时禁用 A checkbox 并取消勾选
        CbA.IsEnabled = _hasAlpha;
        if (!_hasAlpha)
        {
            CbA.IsChecked = false;
        }

        if (bitmap is not null)
        {
            ApplyChannelFilter();
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

    private void ChannelCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_sourceBitmap is null) return;
        ApplyChannelFilter();
    }

    private void ApplyChannelFilter()
    {
        if (_sourceBitmap is null) return;

        bool r = CbR.IsChecked == true;
        bool g = CbG.IsChecked == true;
        bool b = CbB.IsChecked == true;
        bool a = CbA.IsChecked == true && _hasAlpha;

        // 全选或全不选时直接显示原图
        if (r && g && b && (a || !_hasAlpha))
        {
            PreviewImage.Source = _sourceBitmap;
            return;
        }

        PreviewImage.Source = BuildFilteredBitmap(_sourceBitmap, r, g, b, a);
    }

    private static BitmapSource BuildFilteredBitmap(BitmapSource source, bool r, bool g, bool b, bool a)
    {
        // 转换为 Bgra32 以便逐像素操作
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        int width  = bgra.PixelWidth;
        int height = bgra.PixelHeight;
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];
        bgra.CopyPixels(pixels, stride, 0);

        // 单通道模式：只勾选了一个通道时，把该通道值映射到 RGB 灰度输出
        int activeCount = (r ? 1 : 0) + (g ? 1 : 0) + (b ? 1 : 0) + (a ? 1 : 0);
        bool singleChannel = activeCount == 1;

        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte pb = pixels[i];
            byte pg = pixels[i + 1];
            byte pr = pixels[i + 2];
            byte pa = pixels[i + 3];

            if (singleChannel)
            {
                // 单通道：灰度显示，完全不透明
                byte v = r ? pr : g ? pg : b ? pb : pa;
                pixels[i]     = v;
                pixels[i + 1] = v;
                pixels[i + 2] = v;
                pixels[i + 3] = 255;
            }
            else
            {
                pixels[i]     = b ? pb : (byte)0;
                pixels[i + 1] = g ? pg : (byte)0;
                pixels[i + 2] = r ? pr : (byte)0;
                pixels[i + 3] = a ? pa : (byte)255;
            }
        }

        var result = BitmapSource.Create(width, height, source.DpiX, source.DpiY,
            PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    private static bool HasAlphaChannel(string format, BitmapSource? bitmap)
    {
        // 先按格式字符串判断
        string upper = format.ToUpperInvariant();
        foreach (var fmt in AlphaFormats)
        {
            if (upper.Contains(fmt, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // 退而检查像素格式
        if (bitmap is not null)
        {
            return bitmap.Format == PixelFormats.Bgra32
                || bitmap.Format == PixelFormats.Rgba128Float
                || bitmap.Format == PixelFormats.Rgba64
                || bitmap.Format == PixelFormats.Pbgra32
                || bitmap.Format == PixelFormats.Prgba64
                || bitmap.Format == PixelFormats.Prgba128Float;
        }

        return false;
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pngBytes is not { Length: > 0 }) return;

        var dialog = new SaveFileDialog
        {
            Title           = "导出纹理 PNG",
            Filter          = "PNG 图像 (*.png)|*.png",
            DefaultExt      = ".png",
            FileName        = $"{NameText.Text}.png",
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog(this) != true) return;

        File.WriteAllBytes(dialog.FileName, _pngBytes);
    }
}
