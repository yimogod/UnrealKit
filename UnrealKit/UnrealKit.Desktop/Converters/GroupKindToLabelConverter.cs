using System.Globalization;
using System.Windows.Data;

namespace UnrealKit.Desktop.Converters;

/// <summary>
/// 把 CollectionViewSource 分组 key（格式 "Group|order|label"）转换为可读标题。
/// ConverterParameter="group" 返回 Group 名，"kind" 返回 Kind 标签，默认返回 "Group · Kind"。
/// </summary>
public sealed class GroupKindToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string key) return string.Empty;
        var parts = key.Split('|');
        if (parts.Length < 3) return key;

        return (parameter as string) switch
        {
            "group" => parts[0],
            "kind"  => parts[2],
            _       => $"{parts[0]} · {parts[2]}"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
