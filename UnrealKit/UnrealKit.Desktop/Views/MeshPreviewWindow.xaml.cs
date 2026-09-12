using System.Windows;

namespace UnrealKit.Desktop.Views;

public partial class MeshPreviewWindow : Window
{
    private static MeshPreviewWindow? _instance;

    private MeshPreviewWindow(Window owner)
    {
        InitializeComponent();
        Owner = owner;
    }

    /// <summary>打开或刷新预览窗口；已打开时更新内容并激活，不新建。</summary>
    public static void Show(Window owner, string meshName, string glbPath, string status)
    {
        if (_instance is null)
        {
            _instance = new MeshPreviewWindow(owner);
            _instance.Closed += (_, _) => _instance = null;
            _instance.Show();
        }
        else
        {
            if (_instance.WindowState == WindowState.Minimized)
                _instance.WindowState = WindowState.Normal;
            _instance.Activate();
        }

        _instance.Update(meshName, glbPath, status);
    }

    private void Update(string meshName, string glbPath, string status)
    {
        Title = $"模型预览 — {meshName}";
        TitleText.Text = meshName;
        Preview.StatusMessage = status;
        Preview.GlbPath = glbPath;
    }
}
