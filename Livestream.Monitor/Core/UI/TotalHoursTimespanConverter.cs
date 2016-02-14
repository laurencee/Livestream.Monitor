using System;
using System.Globalization;
using System.Windows.Data;

namespace Livestream.Monitor.Core.UI
{
    /// <summary> Returns a timespan in format "TotalHours:Mins:Secs" e.g. "44:12:01" </summary>
    [ValueConversion(typeof(TimeSpan), typeof(string))]
    public class TotalHoursTimespanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var timespan = (TimeSpan)value;

            if (timespan == TimeSpan.Zero)
                return "0";

            return $"{Math.Floor(timespan.TotalHours):00}h {timespan.Minutes:00}m {timespan.Seconds:00}s";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}