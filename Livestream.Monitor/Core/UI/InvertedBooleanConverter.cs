using System;
using System.Globalization;
using System.Windows.Data;

namespace Livestream.Monitor.Core.UI
{
    [ValueConversion(typeof(bool), typeof(bool))]
    public class InvertedBoolenConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(bool)value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(bool)value;
        }
    }
}
