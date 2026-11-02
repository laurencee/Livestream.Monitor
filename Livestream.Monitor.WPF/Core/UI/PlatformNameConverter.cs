using System;
using System.Globalization;
using System.Windows.Data;
using Livestream.Monitor.Core.UI;

namespace Livestream.Monitor.WPF.Core.UI;

public sealed class PlatformNameConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        value is string apiName ? PlatformNames.GetDisplayName(apiName) : string.Empty;

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}
