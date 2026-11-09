using System.Drawing;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Direct2D.Native;
using Livestream.Monitor.Direct2D.Windows;
using Livestream.Monitor.Direct2D.Windows.Output;
using NSubstitute;
using Xunit;

namespace Livestream.Monitor.Tests.Direct2D;

[Collection("Native widgets")]
public class OutputWindowShould
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReopenCenteredAfterTheOwnerMoves(bool minimizeOwner)
    {
        var settingsHandler = Substitute.For<ISettingsHandler>();
        settingsHandler.Settings.Returns(new Settings());

        using var owner = new OwnerWindow();
        using var output = new OutputWindow(owner: owner.Handle, title: "Stream output", settings: settingsHandler, theme: null);

        var workArea = Monitors.GetWorkArea(new Win32.Point());
        var width = workArea.Width / 2;
        var height = workArea.Height / 2;
        var left = workArea.Left + width / 4;
        var top = workArea.Top + height / 4;
        var flags = Win32.SwpNoZOrder | Win32.SwpNoActivate;
        Assert.True(Win32.SetWindowPos(owner.Handle, insertAfter: 0, left, top, width, height, flags));

        output.ShowInitial();
        output.Hide();
        left += width / 2;
        top += height / 2;
        Assert.True(Win32.SetWindowPos(owner.Handle, insertAfter: 0, left, top, width, height, flags));

        if (minimizeOwner)
        {
            Win32.ShowWindow(owner.Handle, Win32.SwMinimize);
            Assert.True(Win32.IsIconic(owner.Handle));
            left = workArea.Left;
            top = workArea.Top;
            width = workArea.Width;
            height = workArea.Height;
        }

        output.ShowFinal();

        Assert.True(Win32.GetWindowRect(output.Handle, out var bounds));
        Assert.Equal(left + (width - bounds.Width) / 2, bounds.Left);
        Assert.Equal(top + (height - bounds.Height) / 2, bounds.Top);
    }

    [Fact]
    public void KeepGrowingOutputOnItsCurrentDisplay()
    {
        var settingsHandler = Substitute.For<ISettingsHandler>();
        settingsHandler.Settings.Returns(new Settings());

        using var owner = new OwnerWindow();
        using var output = new OutputWindow(owner: owner.Handle, title: "Stream output", settings: settingsHandler, theme: null);

        var workArea = Monitors.GetWorkArea(new Win32.Point());
        var flags = Win32.SwpNoZOrder | Win32.SwpNoActivate;
        Assert.True(Win32.SetWindowPos(
            window: owner.Handle,
            insertAfter: 0,
            x: workArea.Left,
            y: workArea.Top,
            width: workArea.Width / 2,
            height: workArea.Height,
            flags));

        output.ShowInitial();
        Assert.True(Win32.GetWindowRect(output.Handle, out var initial));

        // Place the centre just inside the work area so growth would move the centre across its edge.
        var left = workArea.Right - initial.Width / 2 - 1;
        var moveFlags = flags | Win32.SwpNoSize;
        Assert.True(Win32.SetWindowPos(output.Handle, insertAfter: 0, left, initial.Top, width: 0, height: 0, flags: moveFlags));

        var resizeFlags = flags | Win32.SwpNoMove;
        Assert.True(Win32.SetWindowPos(
            window: owner.Handle,
            insertAfter: 0,
            x: 0,
            y: 0,
            width: workArea.Width,
            height: workArea.Height,
            flags: resizeFlags));

        const int WideLineLength = 200;
        output.Append(new string('W', WideLineLength));
        output.ShowFinal();

        Assert.True(Win32.GetWindowRect(output.Handle, out var grown));
        Assert.True(grown.Width > initial.Width);
        Assert.Equal(workArea.Right, grown.Right);
    }

    [Fact]
    public void GrowTheOutputPopupWithItsWrappedContent()
    {
        var maximumSize = OutputWindow.MaximumSize(new SizeF(1200, 800));
        var shortOutput = OutputWindow.CalculateSize(320, 20, maximumSize);
        var longOutput = OutputWindow.CalculateSize(320, 200, maximumSize);

        Assert.True(shortOutput.Height < longOutput.Height);
        Assert.True(shortOutput.Height < 200);
        Assert.Equal(shortOutput.Width, longOutput.Width);
    }

    // The WPF output viewport is at most 700 wide and 500 high, below a 30-pixel caption and above a 23-pixel footer;
    // the window is also shown at most 75% as wide as the app and no taller than it. The size the output asks for does
    // not depend on the app, so output that arrived while the app was small is shown in full once the app is enlarged.
    [Theory]
    [InlineData(2000, 2000, 700, 553)]
    [InlineData(800, 400, 600, 400)]
    public void CapTheOutputWindowAtTheWpfLimitsAndTheAppSize(
        float appWidth,
        float appHeight,
        float expectedWidth,
        float expectedHeight)
    {
        var requestedSize = OutputWindow.RequestedSize(SizeF.Empty, contentWidth: 1200, contentHeight: 1200);
        var size = OutputWindow.ShownSize(requestedSize, noticeHeight: 0, new SizeF(appWidth, appHeight));

        Assert.Equal(new SizeF(expectedWidth, expectedHeight), size);
    }

    [Fact]
    public void KeepItsSizeWhenLaterOutputNeedsLess()
    {
        var requestedSize = OutputWindow.RequestedSize(SizeF.Empty, contentWidth: 600, contentHeight: 300);

        Assert.Equal(requestedSize, OutputWindow.RequestedSize(requestedSize, contentWidth: 100, contentHeight: 50));
    }

    private sealed class OwnerWindow() : DesktopWindow(title: "Stream output owner", width: 1, height: 1);
}
