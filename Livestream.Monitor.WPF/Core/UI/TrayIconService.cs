using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Livestream.Monitor.Core;
using Livestream.Monitor.WPF.Views;
using Forms = System.Windows.Forms;

namespace Livestream.Monitor.WPF.Core.UI;

public sealed class TrayIconService : IDisposable
{
    private const string ProductName = "Livestream Monitor";
    private readonly Forms.NotifyIcon icon;
    private readonly Icon image;
    private readonly ContextMenu menu;
    private readonly Action show;
    private readonly Func<Task> exit;
    private TrayMinimizedView minimizedPopup;

    public TrayIconService(Action show, Func<Task> exit)
    {
        this.show = show;
        this.exit = exit;

        using var stream = EmbeddedImages.Open(EmbeddedImages.TrayIcon)
            ?? throw new InvalidOperationException("The embedded tray icon is missing.");
        using var source = new Icon(stream);
        image = (Icon)source.Clone();
        menu = new ContextMenu
        {
            Style = (Style)Application.Current.FindResource("CompactContextMenuWithoutCheckmarkColumnStyle"),
        };
        var showItem = new MenuItem { Header = "Show" };
        var exitItem = new MenuItem { Header = "Exit" };
        showItem.Click += (_, _) => show();
        exitItem.Click += OnExit;
        menu.Items.Add(showItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);
        menu.Opened += OnMenuOpened;

        icon = new Forms.NotifyIcon
        {
            Icon = image,
            Text = ProductName,
            Visible = true,
        };

        icon.MouseUp += OnTrayMouseUp;
        icon.DoubleClick += (_, _) => show();
    }

    public void NotifyMinimized(Action disablePopup)
    {
        minimizedPopup = new TrayMinimizedView(show, disablePopup);
        minimizedPopup.Closed += (_, _) => minimizedPopup = null;
        minimizedPopup.Show();
    }

    public void Dispose()
    {
        minimizedPopup?.Close();
        icon.Dispose();
        menu.IsOpen = false;
        image.Dispose();
    }

    private void OnTrayMouseUp(object sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Right) menu.IsOpen = true;
    }

    private void OnMenuOpened(object sender, RoutedEventArgs e)
    {
        // Foreground the popup itself so outside clicks dismiss it and the main window stays hidden.
        if (PresentationSource.FromVisual(menu) is HwndSource source) SetForegroundWindow(source.Handle);

        menu.Focus();
    }

    // ReSharper disable once AsyncVoidEventHandlerMethod
    private async void OnExit(object sender, RoutedEventArgs e) => await exit();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
}
