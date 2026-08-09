using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace UnrealKit.Desktop;

public sealed class PointsToPointCollectionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ObservableCollection<Point> points || points.Count == 0)
            return new PointCollection();

        var collection = new PointCollection();
        foreach (var point in points)
            collection.Add(point);
        return collection;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
