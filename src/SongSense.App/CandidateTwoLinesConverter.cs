using System.Globalization;
using System.Windows.Data;
namespace SongSense.App;
public sealed class CandidateTwoLinesConverter : IValueConverter
{
 public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is double font ? font * (parameter as string == "Line" ? 1.4 : 2.8) : 35d;
 public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
