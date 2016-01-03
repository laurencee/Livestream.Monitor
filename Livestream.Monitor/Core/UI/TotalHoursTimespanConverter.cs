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
            var timespan = (TimeSpan) value;

            if (timespan.TotalMinutes < 1)
                return $"{timespan.Seconds}s";

            if (timespan.TotalHours < 1)
                return $"{timespan.Minutes}m {timespan.Seconds}s";

            return $"{Math.Floor(timespan.TotalHours)}h {timespan.Minutes}m {timespan.Seconds}s";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}