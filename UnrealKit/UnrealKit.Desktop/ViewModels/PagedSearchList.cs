using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;

namespace UnrealKit.Desktop.ViewModels;

/// <summary>
/// 带搜索过滤、全量排序和分页功能的列表封装。
/// 排序作用于全部过滤数据，再分页展示，而不是仅排当前页。
/// XAML 绑定路径示例：Search、PageInfo、Items、PrevPageCommand、NextPageCommand。
/// </summary>
public sealed class PagedSearchList<T> : INotifyPropertyChanged
    where T : class
{
    private readonly Func<T, string> _nameSelector;
    private readonly Func<T, string> _pathSelector;
    private readonly Func<int> _getPageSize;
    private readonly (string Prefix, Func<T, string> Selector)? _prefixFilter;

    // column header → key selector（由外部在构造后注册）
    private readonly Dictionary<string, Func<T, IComparable>> _sortKeys = new(StringComparer.OrdinalIgnoreCase);

    private List<T> _allItems = [];
    private string _search = string.Empty;
    private int _page = 1;
    private string? _sortColumn;
    private bool _sortDescending;

    // Cached count from the last ComputeFiltered() call; PageCount/PageInfo read this
    // instead of re-running the filter+sort pipeline on every property-changed notification.
    private int _filteredCount;

    private DispatcherTimer? _searchDebounce;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<T> Items { get; } = [];

    public ICommand PrevPageCommand { get; }
    public ICommand NextPageCommand { get; }

    public PagedSearchList(
        Func<T, string> nameSelector,
        Func<T, string> pathSelector,
        Func<int> getPageSize,
        (string Prefix, Func<T, string> Selector)? prefixFilter = null)
    {
        _nameSelector = nameSelector;
        _pathSelector = pathSelector;
        _getPageSize  = getPageSize;
        _prefixFilter = prefixFilter;

        PrevPageCommand = new DelegateCommand(() => GoToPage(_page - 1), () => _page > 1);
        NextPageCommand = new DelegateCommand(() => GoToPage(_page + 1), () => _page < PageCount);
    }

    /// <summary>注册列头对应的排序键，供 DataGrid Sorting 事件使用。</summary>
    public void RegisterSortKey(string columnHeader, Func<T, IComparable> keySelector) =>
        _sortKeys[columnHeader] = keySelector;

    public string Search
    {
        get => _search;
        set
        {
            if (_search == value) return;
            _search = value;
            OnPropertyChanged();

            // Debounce: wait 300 ms of inactivity before running filter+sort over
            // potentially 60 k items. Without this every keystroke blocks the UI thread.
            if (_searchDebounce is null)
            {
                _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                _searchDebounce.Tick += OnSearchDebounced;
            }
            _searchDebounce.Stop();
            _searchDebounce.Start();
        }
    }

    private void OnSearchDebounced(object? sender, EventArgs e)
    {
        _searchDebounce!.Stop();
        GoToPage(1);
    }

    public int Page => _page;

    public int PageCount => (int)Math.Ceiling(_filteredCount / (double)Math.Max(1, _getPageSize()));

    public string PageInfo
    {
        get
        {
            int pages = (int)Math.Ceiling(_filteredCount / (double)Math.Max(1, _getPageSize()));
            return $"{_page} / {Math.Max(1, pages)}  （共 {_filteredCount} 条）";
        }
    }

    /// <summary>
    /// 对全量过滤数据按指定列排序并回到第一页。
    /// </summary>
    public void ApplySort(string columnHeader, bool descending)
    {
        _sortColumn    = columnHeader;
        _sortDescending = descending;
        GoToPage(1);
    }

    /// <summary>替换全部原始数据并回到第一页。</summary>
    public void Reset(IEnumerable<T> source)
    {
        _allItems = source.ToList();
        GoToPage(1);
    }

    /// <summary>返回全量原始数据（不受当前搜索/分页影响），供跨资产查询使用。</summary>
    public IReadOnlyList<T> AllItems => _allItems;

    /// <summary>清空数据、搜索词、排序，回到第一页。</summary>
    public void Clear()
    {
        _search = string.Empty;
        _sortColumn = null;
        _allItems = [];
        GoToPage(1);
        OnPropertyChanged(nameof(Search));
    }

    private List<T> ComputeFiltered()
    {
        IEnumerable<T> source;
        if (string.IsNullOrEmpty(_search))
        {
            source = _allItems;
        }
        else if (_prefixFilter is { } pf && _search.StartsWith(pf.Prefix, StringComparison.OrdinalIgnoreCase))
        {
            var term = _search[pf.Prefix.Length..];
            source = _allItems.Where(t => pf.Selector(t).Contains(term, StringComparison.Ordinal));
        }
        else
        {
            source = _allItems.Where(t =>
                _nameSelector(t).Contains(_search, StringComparison.Ordinal) ||
                _pathSelector(t).Contains(_search, StringComparison.Ordinal));
        }

        if (_sortColumn is not null && _sortKeys.TryGetValue(_sortColumn, out var keySelector))
        {
            source = _sortDescending
                ? source.OrderByDescending(keySelector)
                : source.OrderBy(keySelector);
        }

        return source.ToList();
    }

    public void GoToPage(int page)
    {
        // Single filter+sort pass for the entire call; PageCount/PageInfo use _filteredCount.
        var filtered = ComputeFiltered();
        _filteredCount = filtered.Count;

        int pageSize = Math.Max(1, _getPageSize());
        int total    = (int)Math.Ceiling(_filteredCount / (double)pageSize);
        _page        = Math.Clamp(page, 1, Math.Max(1, total));

        Items.Reset(filtered.Skip((_page - 1) * pageSize).Take(pageSize));

        OnPropertyChanged(nameof(Page));
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(PageInfo));
        (PrevPageCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        (NextPageCommand as DelegateCommand)?.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
