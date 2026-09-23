using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace UnrealKit.Desktop.Converters;

/// <summary>true → Collapsed，false → Visible。用于 IsSelectedDeviceWin64 等「满足条件则隐藏」的场景。</summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
