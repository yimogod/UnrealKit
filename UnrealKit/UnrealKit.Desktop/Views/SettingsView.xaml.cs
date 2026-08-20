using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using UnrealKit.Desktop.ViewModels;

namespace UnrealKit.Desktop.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// PasswordBox 的 Password 不是依赖属性，不能直接数据绑定。这里手工双向同步：
    /// 用户在框内输入 → 写回 ViewModel；ViewModel 加载工程或其它来源改密码 → 回填掩码框。
    /// </summary>
    private void FtpPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel viewModel && FtpPasswordBox.Password != viewModel.FtpPassword)
        {
            viewModel.FtpPassword = FtpPasswordBox.Password;
        }
    }

    private void FtpPasswordBox_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ShellViewModel oldViewModel)
        {
            oldViewModel.PropertyChanged -= OnFtpPasswordChanged;
        }

        if (e.NewValue is ShellViewModel newViewModel)
        {
            FtpPasswordBox.Password = newViewModel.FtpPassword;
            newViewModel.PropertyChanged += OnFtpPasswordChanged;
        }
    }

    private void OnFtpPasswordChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ShellViewModel.FtpPassword) or null)
            || sender is not ShellViewModel viewModel)
        {
            return;
        }

        if (FtpPasswordBox.Password != viewModel.FtpPassword)
        {
            FtpPasswordBox.Password = viewModel.FtpPassword;
        }
    }
}
