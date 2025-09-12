using System;
using System.Globalization;
using System.Windows.Data;

namespace VrcGroupGuardian.Converters;

public class TimeSpanToMinutesConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is TimeSpan timeSpan)
        {
            return $"{(int)timeSpan.TotalMinutes}m {timeSpan.Seconds}s";
        }
        return "0m 0s";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}