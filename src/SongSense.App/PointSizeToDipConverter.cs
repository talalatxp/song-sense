using System.Globalization;
using System.Windows.Data;
namespace SongSense.App;
public sealed class PointSizeToDipConverter : IValueConverter
{
 public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is double points ? Math.Clamp(points, 12, 24) * 4 / 3 : 16d;
 public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
