using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VrcGroupGuardian.Converters;

public class SeverityToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value?.ToString() is string severity)
        {
            return severity.ToLower() switch
            {
                "critical" => new SolidColorBrush(Colors.Red),
                "high" => new SolidColorBrush(Colors.OrangeRed),
                "medium" => new SolidColorBrush(Colors.Orange),
                "low" => new SolidColorBrush(Colors.Yellow),
                "info" => new SolidColorBrush(Colors.Blue),
                _ => new SolidColorBrush(Colors.Gray)
            };
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}