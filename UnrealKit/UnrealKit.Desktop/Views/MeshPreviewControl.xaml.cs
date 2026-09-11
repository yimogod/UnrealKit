using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace UnrealKit.Desktop.Views;

public partial class MeshPreviewControl : UserControl
{
    public static readonly DependencyProperty GlbPathProperty =
        DependencyProperty.Register(nameof(GlbPath), typeof(string), typeof(MeshPreviewControl),
            new PropertyMetadata(string.Empty, OnGlbPathChanged));

    public static readonly DependencyProperty StatusMessageProperty =
        DependencyProperty.Register(nameof(StatusMessage), typeof(string), typeof(MeshPreviewControl),
            new PropertyMetadata(string.Empty, OnStatusMessageChanged));

    public string GlbPath
    {
        get => (string)GetValue(GlbPathProperty);
        set => SetValue(GlbPathProperty, value);
    }

    public string StatusMessage
    {
        get => (string)GetValue(StatusMessageProperty);
        set => SetValue(StatusMessageProperty, value);
    }

    private bool _webViewReady;
    private string _pendingGlbPath = string.Empty;

    public MeshPreviewControl()
    {
        InitializeComponent();
        _ = InitWebViewAsync();
    }

    private async Task InitWebViewAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync();
            await WebView.EnsureCoreWebView2Async(env);
            _webViewReady = true;

            // Extract embedded HTML to temp and navigate to it
            var htmlPath = ExtractViewerHtml();
            WebView.CoreWebView2.Navigate("file:///" + htmlPath.Replace('\\', '/'));

            WebView.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                if (!string.IsNullOrEmpty(_pendingGlbPath))
                    LoadGlb(_pendingGlbPath);
            };
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = "WebView2 初始化失败。\n请确认已安装 Edge WebView2 Runtime。\n\n" + ex.Message;
                StatusText.Visibility = Visibility.Visible;
                WebView.Visibility = Visibility.Collapsed;
            });
        }
    }

    private static string ExtractViewerHtml()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "unrealkit_mesh_preview");
        Directory.CreateDirectory(tmpDir);
        var dest = Path.Combine(tmpDir, "mesh_viewer.html");

        var asm = Assembly.GetExecutingAssembly();
        const string res = "UnrealKit.Desktop.Assets.mesh_viewer.html";
        using var stream = asm.GetManifestResourceStream(res)
            ?? throw new InvalidOperationException($"Embedded resource not found: {res}");
        using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write);
        stream.CopyTo(fs);

        return dest;
    }

    private void LoadGlb(string path)
    {
        _pendingGlbPath = string.Empty;
        var jsPath = path.Replace('\\', '/');
        WebView.CoreWebView2.ExecuteScriptAsync($"window.loadGlb('file:///{jsPath}')");
    }

    private static void OnGlbPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (MeshPreviewControl)d;
        var path = (string)e.NewValue;

        if (string.IsNullOrEmpty(path)) return;

        if (ctrl._webViewReady)
            ctrl.Dispatcher.Invoke(() => ctrl.LoadGlb(path));
        else
            ctrl._pendingGlbPath = path;
    }

    private static void OnStatusMessageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (MeshPreviewControl)d;
        var msg = (string)e.NewValue;
        ctrl.Dispatcher.Invoke(() =>
        {
            if (string.IsNullOrEmpty(msg))
            {
                ctrl.StatusText.Visibility = Visibility.Collapsed;
            }
            else
            {
                ctrl.StatusText.Text = msg;
                ctrl.StatusText.Visibility = Visibility.Visible;
            }
        });
    }
}
