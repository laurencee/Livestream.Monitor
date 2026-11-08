using System;
using System.Windows;
using System.Windows.Threading;
using Livestream.Monitor.Model.Monitoring;
using Livestream.Monitor.WPF.Core.UI;

namespace Livestream.Monitor.WPF.Views;

public partial class TrayMinimizedView
{
    private readonly Action restore;
    private readonly Action disablePopup;

    internal TrayMinimizedView(Action restore, Action disablePopup)
    {
        this.restore = restore;
        this.disablePopup = disablePopup;
        InitializeComponent();

        var workArea = DisplayPlacement.GetWorkArea(Application.Current.MainWindow);
        var preferredSize = new Size(Width, Height);
        var bounds = DisplayPlacement.GetNotificationBounds(workArea, preferredSize, rightAligned: false);
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;

        var timer = new DispatcherTimer { Interval = LivestreamNotification.DefaultDuration };
        timer.Tick += (_, _) => Close();
        Closed += (_, _) => timer.Stop();
        timer.Start();
    }

    private void RestoreWindow(object sender, RoutedEventArgs e)
    {
        Close();
        restore();
    }

    private void DisablePopup(object sender, RoutedEventArgs e)
    {
        disablePopup();
        Close();
    }
}
