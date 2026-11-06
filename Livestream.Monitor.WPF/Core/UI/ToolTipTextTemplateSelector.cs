using System.Windows;
using System.Windows.Controls;

namespace Livestream.Monitor.WPF.Core.UI;

public sealed class ToolTipTextTemplateSelector : DataTemplateSelector
{
    public DataTemplate TextTemplate { get; set; }

    public override DataTemplate SelectTemplate(object item, DependencyObject container) =>
        item is string ? TextTemplate : base.SelectTemplate(item, container);
}
