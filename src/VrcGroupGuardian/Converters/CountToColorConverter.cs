using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VrcGroupGuardian.Converters;

public class CountToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count switch
            {
                0 => new SolidColorBrush(Colors.Gray),
                > 0 and <= 5 => new SolidColorBrush(Colors.Blue),
                > 5 and <= 10 => new SolidColorBrush(Colors.Orange),
                _ => new SolidColorBrush(Colors.Red)
            };
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}