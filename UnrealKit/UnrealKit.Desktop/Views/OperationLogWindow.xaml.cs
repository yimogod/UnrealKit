using System.Collections.Specialized;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using UnrealKit.Desktop.ViewModels;

namespace UnrealKit.Desktop.Views;

/// <summary>
/// 全局操作日志窗口。与主窗口共享同一个 <see cref="ShellViewModel"/>，
/// 因此所有功能写入的日志都会实时出现在这里，无需各页面自带日志控件。
/// </summary>
/// <remarks>
/// 由 <see cref="Show(Window, ShellViewModel)"/> 维护单实例：重复点击「操作日志」
/// 只会激活已打开的窗口，不会叠出多个。
/// </remarks>
public partial class OperationLogWindow : Window
{
    private static OperationLogWindow? _instance;

    public static readonly DependencyProperty AutoScrollProperty =
        DependencyProperty.Register(nameof(AutoScroll), typeof(bool), typeof(OperationLogWindow),
            new PropertyMetadata(true));

    /// <summary>勾选时新日志到达自动滚动到底部；取消勾选便于翻阅历史而不被打断。</summary>
    public bool AutoScroll
    {
        get => (bool)GetValue(AutoScrollProperty);
        set => SetValue(AutoScrollProperty, value);
    }

    private OperationLogWindow(Window owner, ShellViewModel viewModel)
    {
        InitializeComponent();
        Owner = owner;
        DataContext = viewModel;

        if (viewModel.OperationLogs is INotifyCollectionChanged observable)
        {
            observable.CollectionChanged += OnLogsChanged;
            Closed += (_, _) => observable.CollectionChanged -= OnLogsChanged;
        }

        Loaded += (_, _) => ScrollToEnd();
    }

    /// <summary>打开日志窗口；已打开时只激活，不新建。</summary>
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

        _instance = new OperationLogWindow(owner, viewModel);
        _instance.Closed += (_, _) => _instance = null;
        _instance.Show();
    }

    private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && AutoScroll)
        {
            ScheduleScrollToEnd();
        }
    }

    private bool _scrollPending;

    /// <summary>
    /// 滚动必须推迟到集合变更处理完成之后：<see cref="DataGrid"/> 的 ItemContainerGenerator
    /// 在处理 CollectionChanged 期间尚未完成计数对账，此刻同步调用 ScrollIntoView 会触发
    /// 「累积计数与实际计数不符」异常。合并同一批次的多次 Add，只滚动一次。
    /// </summary>
    private void ScheduleScrollToEnd()
    {
        if (_scrollPending) return;
        _scrollPending = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _scrollPending = false;
            ScrollToEnd();
        }), DispatcherPriority.Background);
    }

    private void ScrollToEnd()
    {
        if (LogGrid.Items.Count <= 10) return;
        LogGrid.ScrollIntoView(LogGrid.Items[^1]!);
    }

    private async void SaveLogs_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel viewModel) return;

        var dialog = new SaveFileDialog
        {
            Title = "保存操作日志",
            Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            DefaultExt = ".txt",
            // 扩展名不说谎：内容是每行一条的纯文本，因此只给 .txt。
            FileName = $"unrealkit-log-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.txt",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            await viewModel.SaveOperationLogsAsync(dialog.FileName);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"保存日志失败：{exception.Message}", "保存失败",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
