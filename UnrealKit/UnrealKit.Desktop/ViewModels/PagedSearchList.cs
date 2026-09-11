using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace UnrealKit.Desktop.ViewModels;

/// <summary>
/// 带搜索过滤和分页功能的列表封装。
/// 通过 Name/Path 两个字段做区分大小写的子串匹配。
/// XAML 绑定路径示例：Search、PageInfo、Items、PrevPageCommand、NextPageCommand。
/// </summary>
public sealed class PagedSearchList<T> : INotifyPropertyChanged
    where T : class
{
    private readonly Func<T, string> _nameSelector;
    private readonly Func<T, string> _pathSelector;
    private readonly Func<int> _getPageSize;

    private List<T> _allItems = [];
    private string _search = string.Empty;
    private int _page = 1;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<T> Items { get; } = [];

    public ICommand PrevPageCommand { get; }
    public ICommand NextPageCommand { get; }

    public PagedSearchList(
        Func<T, string> nameSelector,
        Func<T, string> pathSelector,
        Func<int> getPageSize)
    {
        _nameSelector = nameSelector;
        _pathSelector = pathSelector;
        _getPageSize  = getPageSize;

        PrevPageCommand = new DelegateCommand(() => GoToPage(_page - 1), () => _page > 1);
        NextPageCommand = new DelegateCommand(() => GoToPage(_page + 1), () => _page < PageCount);
    }

    public string Search
    {
        get => _search;
        set
        {
            if (_search == value) return;
            _search = value;
            GoToPage(1);
            OnPropertyChanged();
        }
    }

    public int Page      => _page;
    public int PageCount => (int)Math.Ceiling(Filtered.Count / (double)Math.Max(1, _getPageSize()));
    public string PageInfo
    {
        get
        {
            var f = Filtered;
            int pages = (int)Math.Ceiling(f.Count / (double)Math.Max(1, _getPageSize()));
            return $"{_page} / {Math.Max(1, pages)}  （共 {f.Count} 条）";
        }
    }

    /// <summary>替换全部原始数据并回到第一页。</summary>
    public void Reset(IEnumerable<T> source)
    {
        _allItems = source.ToList();
        GoToPage(1);
    }

    /// <summary>清空数据、搜索词，回到第一页。</summary>
    public void Clear()
    {
        _search = string.Empty;
        _allItems = [];
        GoToPage(1);
        OnPropertyChanged(nameof(Search));
    }

    private IReadOnlyList<T> Filtered =>
        string.IsNullOrEmpty(_search)
            ? _allItems
            : _allItems.Where(t =>
                _nameSelector(t).Contains(_search, StringComparison.Ordinal) ||
                _pathSelector(t).Contains(_search, StringComparison.Ordinal)).ToList();

    public void GoToPage(int page)
    {
        var filtered = Filtered;
        int total = (int)Math.Ceiling(filtered.Count / (double)Math.Max(1, _getPageSize()));
        _page = Math.Clamp(page, 1, Math.Max(1, total));

        var slice = filtered.Skip((_page - 1) * _getPageSize()).Take(_getPageSize());
        Items.Reset(slice);

        OnPropertyChanged(nameof(Page));
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(PageInfo));
        (PrevPageCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        (NextPageCommand as DelegateCommand)?.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
