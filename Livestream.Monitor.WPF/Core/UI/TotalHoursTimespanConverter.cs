using System;
using System.Globalization;
using System.Windows.Data;
using Livestream.Monitor.Core;

namespace Livestream.Monitor.WPF.Core.UI
{
    /// <summary> Returns a timespan in format "TotalHours:Mins:Secs" e.g. "44:12:01" </summary>
    [ValueConversion(typeof(TimeSpan), typeof(string))]
    public class TotalHoursTimespanConverter : IValueConverter
    {
        public object Convert(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture)
        {
            if (value is not TimeSpan timeSpan) throw new ArgumentException("The value is not a TimeSpan.", nameof(value));

            return timeSpan.ToTotalHoursText();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}