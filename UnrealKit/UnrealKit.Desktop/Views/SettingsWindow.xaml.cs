using System.Windows;
using UnrealKit.Desktop.ViewModels;

namespace UnrealKit.Desktop.Views;

/// <summary>
/// 工程配置窗口。与主窗口共享同一个 <see cref="ShellViewModel"/>，
/// 保存后主窗口各页面立即读到同一份配置。
/// </summary>
/// <remarks>
/// 由 <see cref="Show(Window, ShellViewModel)"/> 维护单实例，理由同
/// <see cref="OperationLogWindow"/>：重复点菜单只激活已打开的窗口。
/// 非模态，方便边改配置边操作主窗口。
/// </remarks>
public partial class SettingsWindow : Window
{
    private static SettingsWindow? _instance;

    private SettingsWindow(Window owner, ShellViewModel viewModel)
    {
        InitializeComponent();
        Owner = owner;
        DataContext = viewModel;
    }

    /// <summary>打开配置窗口；已打开时只激活，不新建。</summary>
    public static void Show(Window owner, ShellViewModel viewModel)
    {
        if (_instance is not null)
        {
            if (_instance.WindowState == WindowState.Minimized)
            {
                _instance.WindowState = WindowState.Normal;
            }
            _instance.Activate();
            return;
        }

        _instance = new SettingsWindow(owner, viewModel);
        _instance.Closed += (_, _) => _instance = null;
        _instance.Show();
    }
}
