using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace UnrealKit.Desktop.ViewModels;

internal static class ObservableCollectionExtensions
{
    /// <summary>
    /// 用 <paramref name="items"/> 批量替换集合内容，只触发一次 Reset 通知。
    /// 相比逐条 Add，避免大量数据时 DataGrid 每次都重排，防止 UI 线程冻结。
    /// </summary>
    internal static void Reset<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        // 通过反射访问 ObservableCollection 内部的 List<T> Items，
        // 直接操作 backing list 然后手动发 Reset 通知，整个替换只触发一次 CollectionChanged。
        var itemsProp = typeof(System.Collections.ObjectModel.Collection<T>)
            .GetProperty("Items", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var list = (List<T>)itemsProp.GetValue(collection)!;
        list.Clear();
        list.AddRange(items);

        var changedMethod = typeof(ObservableCollection<T>)
            .GetMethod("OnCollectionChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                [typeof(NotifyCollectionChangedEventArgs)])!;
        changedMethod.Invoke(collection, [new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset)]);

        var countMethod = typeof(ObservableCollection<T>)
            .GetMethod("OnPropertyChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                [typeof(System.ComponentModel.PropertyChangedEventArgs)])!;
        countMethod.Invoke(collection, [new System.ComponentModel.PropertyChangedEventArgs("Count")]);
        countMethod.Invoke(collection, [new System.ComponentModel.PropertyChangedEventArgs("Item[]")]);
    }
}
