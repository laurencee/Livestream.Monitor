using System;
using System.Globalization;
using System.Windows.Data;

namespace Livestream.Monitor.WPF.Core.UI;

public sealed class SingleLineTextConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        value is string text ? text.ReplaceLineEndings(" ") : value;

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}
