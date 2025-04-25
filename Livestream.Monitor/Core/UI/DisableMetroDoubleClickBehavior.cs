using System.Windows;
using MahApps.Metro.Controls;

namespace Livestream.Monitor.Core.UI;

public static class DisableMetroDoubleClickBehavior
{
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled",
            typeof(bool),
            typeof(DisableMetroDoubleClickBehavior),
            new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject element, bool value)
        => element.SetValue(EnabledProperty, value);

    public static bool GetEnabled(DependencyObject element)
        => (bool)element.GetValue(EnabledProperty);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (!(d is MetroWindow window) || !(bool)e.NewValue)
            return;

        window.Loaded += (s, args) =>
        {
            if (window.Template.FindName("PART_TitleBar", window) is UIElement titleBar)
            {
                titleBar.PreviewMouseLeftButtonDown += (s2, e2) =>
                {
                    if (e2.ClickCount == 2)
                        e2.Handled = true;
                };
            }
        };
    }
}