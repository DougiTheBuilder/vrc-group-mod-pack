using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VrcGroupGuardian.Converters;

public class StatusToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value?.ToString() is string status)
        {
            return status.ToLower() switch
            {
                "active" => Visibility.Visible,
                "online" => Visibility.Visible,
                "pending" => Visibility.Visible,
                _ => Visibility.Collapsed
            };
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}