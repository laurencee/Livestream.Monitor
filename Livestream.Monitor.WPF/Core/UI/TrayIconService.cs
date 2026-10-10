using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Livestream.Monitor.Core;

namespace Livestream.Monitor.WPF.Core.UI;

public sealed class TrayIconService : IDisposable
{
    private const string ProductName = "Livestream Monitor";
    private readonly NotifyIcon icon;
    private readonly Icon image;
    private readonly ContextMenuStrip menu;
    private readonly Func<Task> exit;

    public TrayIconService(Action show, Func<Task> exit)
    {
        this.exit = exit;

        using var stream = EmbeddedImages.Open(EmbeddedImages.TrayIcon)
            ?? throw new InvalidOperationException("The embedded tray icon is missing.");
        using var source = new Icon(stream);
        image = (Icon)source.Clone();
        menu = new ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => show());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, OnExit);
        icon = new NotifyIcon
        {
            Icon = image,
            Text = ProductName,
            ContextMenuStrip = menu,
            Visible = true,
        };
        icon.DoubleClick += (_, _) => show();
    }

    public void NotifyMinimized()
    {
        const int DurationMilliseconds = 5000;
        icon.ShowBalloonTip(
            DurationMilliseconds,
            ProductName,
            $"{ProductName} minimized to tray",
            ToolTipIcon.Info);
    }

    public void Dispose()
    {
        icon.Dispose();
        menu.Dispose();
        image.Dispose();
    }

    // WinForms click events return void; a failure of the exit callback reaches WPF's dispatcher as unhandled.
    // ReSharper disable once AsyncVoidEventHandlerMethod
    private async void OnExit(object sender, EventArgs e) => await exit();
}
