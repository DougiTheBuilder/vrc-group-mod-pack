using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VrcGroupGuardian.Converters;

public class StatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value?.ToString() is string status)
        {
            return status.ToLower() switch
            {
                "online" => new SolidColorBrush(Colors.Green),
                "active" => new SolidColorBrush(Colors.Green),
                "offline" => new SolidColorBrush(Colors.Gray),
                "inactive" => new SolidColorBrush(Colors.Gray),
                "busy" => new SolidColorBrush(Colors.Orange),
                "away" => new SolidColorBrush(Colors.Yellow),
                "error" => new SolidColorBrush(Colors.Red),
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