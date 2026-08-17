using System.Windows;
using System.Windows.Markup;

namespace UnrealKit.Desktop.Models;

/// <summary>
/// 一个左侧导航项。<see cref="Title"/> 即页面标识，与 <c>SelectedNavigationItem</c>
/// 和 <c>PageDescription</c> 的分支一一对应；<see cref="Group"/> 是分组标签文本。
/// </summary>
/// <remarks>
/// 导航必须由 <c>ItemsSource</c> 驱动，不能写字面 <c>TabItem</c> 子元素：
/// 后者使用的 <c>ItemCollection</c> 的 <c>CanGroup</c> 恒为 <c>false</c>，
/// <c>GroupDescriptions.Add</c> 会被静默忽略，分组标签不会渲染。
/// </remarks>
[ContentProperty(nameof(View))]
public sealed class NavigationPage
{
    public string Title { get; set; } = string.Empty;

    public string Group { get; set; } = string.Empty;

    /// <summary>页面视图实例。只创建一次，切换导航不会重建，页面内输入状态得以保留。</summary>
    public FrameworkElement? View { get; set; }
}
