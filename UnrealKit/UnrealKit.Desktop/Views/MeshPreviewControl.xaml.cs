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

    // true only after HTML has finished loading — window.loadGlb is not available before this
    private bool _viewerReady;
    private string _pendingGlbPath = string.Empty;
    private string _glbVirtualBase = string.Empty;

    public MeshPreviewControl()
    {
        InitializeComponent();
        _ = InitWebViewAsync();
    }

    private async Task InitWebViewAsync()
    {
        Log("InitWebViewAsync start");
        try
        {
            var env = await CoreWebView2Environment.CreateAsync();
            await WebView.EnsureCoreWebView2Async(env);
            Log("EnsureCoreWebView2Async done");

            WebView.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                Log($"NavigationCompleted IsSuccess={args.IsSuccess} WebErrorStatus={args.WebErrorStatus}");
                if (!args.IsSuccess) return;
                _viewerReady = true;
                Log($"_viewerReady=true pendingGlbPath='{_pendingGlbPath}'");
                if (!string.IsNullOrEmpty(_pendingGlbPath))
                    LoadGlb(_pendingGlbPath);
            };

            WebView.CoreWebView2.WebMessageReceived += (_, args) =>
                Log($"WebMessage: {args.TryGetWebMessageAsString()}");

            WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;

            var htmlPath = ExtractViewerHtml();
            Log($"Navigate to '{htmlPath}'");
            WebView.CoreWebView2.Navigate("file:///" + htmlPath.Replace('\\', '/'));
        }
        catch (Exception ex)
        {
            Log($"InitWebViewAsync EXCEPTION: {ex}");
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = "WebView2 初始化失败。\n请确认已安装 Edge WebView2 Runtime。\n\n" + ex.Message;
                StatusText.Visibility = Visibility.Visible;
                WebView.Visibility = Visibility.Collapsed;
            });
        }
    }

    private static readonly string _logFile = Path.Combine(Path.GetTempPath(), "unrealkit_mesh_preview", "debug.log");

    private static void Log(string msg)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {msg}";
        System.Diagnostics.Debug.WriteLine(line);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logFile)!);
            File.AppendAllText(_logFile, line + "\n");
        }
        catch { }
    }

    private void RemapGlbDirectory(string glbPath)
    {
        var dir = Path.GetDirectoryName(glbPath) ?? Path.GetTempPath();
        if (dir == _glbVirtualBase) return;

        if (!string.IsNullOrEmpty(_glbVirtualBase))
            WebView.CoreWebView2.ClearVirtualHostNameToFolderMapping("glb.local");

        WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "glb.local", dir, CoreWebView2HostResourceAccessKind.Allow);
        _glbVirtualBase = dir;
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
        Log($"LoadGlb path='{path}' fileExists={File.Exists(path)}");
        try
        {
            var bytes = File.ReadAllBytes(path);
            var b64 = Convert.ToBase64String(bytes);
            Log($"LoadGlb b64 length={b64.Length}");
            // Use PostWebMessageAsString to avoid ExecuteScriptAsync size limits
            WebView.CoreWebView2.PostWebMessageAsString(b64);
        }
        catch (Exception ex)
        {
            Log($"LoadGlb EXCEPTION: {ex.Message}");
        }
    }

    private static void OnGlbPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (MeshPreviewControl)d;
        var path = (string)e.NewValue;
        Log($"OnGlbPathChanged path='{path}' viewerReady={ctrl._viewerReady}");

        if (string.IsNullOrEmpty(path)) return;

        if (ctrl._viewerReady)
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
