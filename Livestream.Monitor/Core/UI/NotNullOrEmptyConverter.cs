using System;
using System.Globalization;
using System.Windows.Data;

namespace Livestream.Monitor.Core.UI
{
    [ValueConversion(typeof(string), typeof(bool))]
    public class NotNullOrEmptyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrEmpty(value as string);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
