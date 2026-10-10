using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Direct2D.Automation;
using Livestream.Monitor.Direct2D.Native;
using Livestream.Monitor.Direct2D.Rendering;
using Livestream.Monitor.Direct2D.Widgets;
using Livestream.Monitor.Direct2D.Windows;
using Xunit;

namespace Livestream.Monitor.Tests.Direct2D;

[CollectionDefinition("Native widgets", DisableParallelization = true)]
public class NativeWidgetsCollection;

[Collection("Native widgets")]
public sealed class SurfaceUiShould : IDisposable
{
    // Without a renderer, text is measured at a fixed width per character.
    private const float CharacterWidth = 7;

    private readonly HiddenWindow window;
    private readonly List<DrawCommand> commands;
    private readonly SurfaceUi ui;
    private readonly ThemePalette palette;
    private readonly RectangleF viewport;

    public SurfaceUiShould()
    {
        window = new HiddenWindow();
        commands = [];
        ui = new SurfaceUi(window, commands) { MeasureWidth = MeasureText };
        palette = Theme.Create(new Settings());
        viewport = new RectangleF(x: 0, y: 0, width: 400, height: 300);
    }

    public void Dispose() => window.Dispose();

    private static float MeasureText(string text, float fontSize) => text.Length * CharacterWidth;

    [Fact]
    public void FocusTheNativeInputWhenItsPlaceholderIsShowing()
    {
        var bounds = new RectangleF(x: 10, y: 10, width: 150, height: 24);
        ui.Begin(palette, viewport);
        ui.Input("add", "add livestream...", bounds);
        ui.End();

        ui.MouseDown(new PointF(20, 20));
        ui.MouseUp(new PointF(20, 20));

        Assert.True(ui.HasFocusedInput);
        ui.SetValue("add", "channel");
        Assert.Equal("channel", ui.Value("add"));
    }

    // WPF's caption buttons are not focusable, so clicking one leaves the focus where it was, and a selected grid row keeps its
    // active colour.
    [Fact]
    public void LeaveTheFocusWhereItWasWhenACaptionButtonIsClicked()
    {
        DrawCaptionBetweenButtons();
        ui.MouseDown(new PointF(30, 60));
        ui.MouseUp(new PointF(30, 60));
        ui.MouseDown(new PointF(320, 15));
        ui.MouseUp(new PointF(320, 15));
        DrawCaptionBetweenButtons();

        Assert.True(ui.Automation.Find("first")?.Has(AutomationStates.Focused));
        Assert.True(ui.Automation.Find("chrome.close")?.Has(AutomationStates.Unfocusable));
    }

    [Fact]
    public void SkipCaptionButtonsWhenTabbing()
    {
        DrawCaptionBetweenButtons();
        ui.MouseDown(new PointF(30, 60));
        ui.MouseUp(new PointF(30, 60));

        ui.Key(VirtualKey.Tab, shift: false);
        DrawCaptionBetweenButtons();

        Assert.True(ui.Automation.Find("second")?.Has(AutomationStates.Focused));
    }

    // Widgets in drawing order: a button, the close caption button, then another button.
    private void DrawCaptionBetweenButtons()
    {
        var first = new RectangleF(x: 10, y: 50, width: 60, height: 24);
        var close = new RectangleF(x: 300, y: 0, width: 46, height: 30);
        var second = new RectangleF(x: 80, y: 50, width: 60, height: 24);
        ui.Begin(palette, viewport);
        ui.Button(id: "first", text: "First", bounds: first, action: () => { });
        ui.CaptionButton(id: "chrome.close", bounds: close, action: () => { }, close: true);
        ui.Button(id: "second", text: "Second", bounds: second, action: () => { });
        ui.End();
    }

    // The list opens below the control with whole rows: ten under a 230-pixel height limit, else the eleven that fit
    // in the 263 pixels below it.
    [Theory]
    [InlineData(230f, 250f)]
    [InlineData(1000f, 275f)]
    public void ScrollAnOverflowingMenuWithItsOwnBar(float maximumHeight, float downArrowY)
    {
        var listUi = new SurfaceUi(window, commands) { MeasureWidth = MeasureText, ListMaximumHeight = maximumHeight };
        var longChoice = new string('_', 40);
        var choices = Enumerable.Range(1, 30).Select(i => "Stream " + i).Append(longChoice).ToArray();
        var control = new RectangleF(x: 10, y: 10, width: 140, height: 26);
        var chosen = -1;
        listUi.DropDown(id: "known", control, choices, chosen: index => chosen = index);
        listUi.Begin(palette, viewport);
        listUi.End();

        Assert.Contains(commands, command => command.Kind == DrawKind.TriangleDown);

        // As in WPF, the list stays as wide as the control however long its choices are, with the bar's down arrow at
        // its bottom right corner.
        var downArrow = new PointF(141, downArrowY);
        listUi.MouseDown(downArrow);
        listUi.MouseUp(downArrow);
        Assert.True(listUi.HasMenu);
        Assert.Equal(-1, chosen);

        var firstRow = new PointF(50, 45);
        listUi.MouseDown(firstRow);
        Assert.Equal(1, chosen);
    }

    [Theory]
    [InlineData(true, false, false, "Refresh Livestreams")]
    [InlineData(false, false, false, null)]
    [InlineData(true, true, false, null)]
    [InlineData(true, false, true, null)]
    public void ShowAControlsToolTipOnlyWhenItIsEnabledUncoveredAndNoMenuIsOpen(
        bool enabled,
        bool covered,
        bool menu,
        string expected)
    {
        // As in WPF, a disabled control shows no tooltip.
        var button = new RectangleF(x: 230, y: 100, width: 80, height: 25);
        RectangleF? coveredArea = covered ? new RectangleF(x: 200, y: 0, width: 200, height: 300) : null;
        ui.Begin(palette, viewport, coveredArea);
        const string ToolTip = "Refresh Livestreams";
        ui.Button(id: "refresh", text: "↻", button, action: () => { }, enabled: enabled, toolTip: ToolTip);
        var control = new RectangleF(x: 10, y: 0, width: 80, height: 10);
        if (menu) ui.DropDown(id: "menu", control, choices: ["Choice"], chosen: _ => { });

        Assert.Equal(expected, ui.ToolTipAt(new PointF(240, 110))?.Text);
        Assert.Null(ui.ToolTipAt(new PointF(100, 110)));
    }

    [Fact]
    public void IgnoreDisabledMenuItemsAndShowTheToolTipsOfEnabledOnes()
    {
        var chosen = -1;
        SurfaceUi.MenuChoice[] choices =
        [
            new("Open in Vod Viewer", Enabled: false, ToolTip: "Unavailable"),
            new("Enable Notifications", Checked: true, ToolTip: "Untick this"),
        ];

        ui.Popup(id: "context", position: new PointF(10, 10), choices, chosen: index => chosen = index);
        ui.Begin(palette, viewport);
        var disabledItem = new PointF(40, 20);
        var enabledItem = new PointF(40, 45);

        Assert.Null(ui.ToolTipAt(disabledItem));
        Assert.Equal("Untick this", ui.ToolTipAt(enabledItem)?.Text);
        ui.MouseDown(disabledItem);
        Assert.True(ui.HasMenu);
        Assert.Equal(-1, chosen);
        ui.MouseDown(enabledItem);
        Assert.False(ui.HasMenu);
        Assert.Equal(1, chosen);
    }

    [Fact]
    public void BlockContentHoverUnderSettingsAndWhileADropdownIsOpen()
    {
        var covered = new RectangleF(x: 200, y: 0, width: 200, height: 300);
        var row = new RectangleF(x: 0, y: 100, width: 400, height: 25);
        ui.Begin(palette, viewport, covered);
        ui.MouseMove(new PointF(240, 110));

        Assert.False(ui.IsPointerOver(row));
        Assert.False(ui.IsContentPoint(new PointF(240, 110)));
        Assert.True(ui.IsContentPoint(new PointF(40, 110)));
        var filter = new RectangleF(x: 10, y: 25, width: 80, height: 24);
        ui.DropDown(id: "filter", filter, choices: ["All", "Twitch"], chosen: _ => { });
        ui.Begin(palette, viewport, covered);
        ui.MouseMove(new PointF(40, 110));

        Assert.False(ui.IsPointerOver(row));
        Assert.False(ui.IsContentPoint(new PointF(40, 110)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StopHoveringAControlWhenAnOverlayCoversItsPointer(bool dropdown)
    {
        var button = new RectangleF(x: 230, y: 100, width: 80, height: 25);
        ui.Begin(palette, viewport);
        ui.Button(id: "underlying", text: "Control", button, action: () => { });
        ui.MouseMove(new PointF(240, 110));
        var control = new RectangleF(x: 220, y: 65, width: 80, height: 24);
        if (dropdown) ui.DropDown(id: "menu", control, choices: ["Choice"], chosen: _ => { });

        commands.Clear();
        RectangleF? covered = dropdown ? null : new RectangleF(x: 200, y: 0, width: 200, height: 300);
        ui.Begin(palette, viewport, covered);
        ui.Button(id: "underlying", text: "Control", button, action: () => { });
        ui.End();

        var background = Assert.Single(commands, command => command.Kind == DrawKind.Fill && command.Bounds == button);
        Assert.Equal(palette.Control, background.Color);
    }

    [Fact]
    public void ReleaseToolbarKeyboardFocusWhenTheUserSelectsTheCustomList()
    {
        var button = new RectangleF(x: 10, y: 10, width: 80, height: 25);
        var activations = 0;
        ui.Begin(palette, viewport);
        ui.Button(id: "refresh", text: "Refresh", button, action: () => activations++);
        ui.MouseDown(new PointF(20, 20));
        ui.MouseUp(new PointF(20, 20));
        Assert.Equal(1, activations);

        ui.MouseDown(new PointF(20, 100));
        ui.MouseUp(new PointF(20, 100));
        const int EnterKey = 13;
        Assert.False(ui.Key(EnterKey, shift: false));
        Assert.Equal(1, activations);
    }

    [Fact]
    public void KeepRadioOptionsMutuallyExclusiveAndSupportArrowKeys()
    {
        var bestBounds = new RectangleF(x: 10, y: 10, width: 80, height: 25);
        var worstBounds = new RectangleF(x: 100, y: 10, width: 80, height: 25);
        var best = true;
        ui.Begin(palette, viewport);
        ui.Radio(id: "best", text: "Best", value: true, bestBounds, changed: _ => best = true);
        ui.Radio(id: "worst", text: "Worst", value: false, worstBounds, changed: _ => best = false);

        ui.MouseDown(new PointF(110, 20));
        ui.MouseUp(new PointF(110, 20));
        Assert.False(best);
        ui.MouseDown(new PointF(110, 20));
        ui.MouseUp(new PointF(110, 20));
        Assert.False(best);

        const int LeftKey = 37;
        Assert.True(ui.Key(LeftKey, shift: false));
        Assert.True(best);
    }

    [Fact]
    public void BlockUnderlyingControlsAndMenusWhenAModalOpens()
    {
        var button = new RectangleF(x: 10, y: 10, width: 80, height: 25);
        var activations = 0;
        ui.Begin(palette, viewport);
        ui.Button(id: "query", text: "Query", button, action: () => activations++);
        var gameFilter = new RectangleF(x: 10, y: 15, width: 80, height: 24);
        ui.DropDown(id: "suggestions", gameFilter, choices: ["Game"], chosen: _ => activations++);
        Assert.True(ui.HasMenu);

        ui.ModalOverlay();

        Assert.False(ui.HasMenu);
        Assert.False(ui.MouseDown(new PointF(20, 20)));
        Assert.False(ui.MouseUp(new PointF(20, 20)));
        Assert.Equal(0, activations);
    }

    [Fact]
    public void ExposeAnOpenComboBoxsChoicesAsItsItemsAndChooseThroughThem()
    {
        var bounds = new RectangleF(x: 10, y: 10, width: 80, height: 24);
        var chosen = -1;
        DrawFrame();
        var closed = ui.Automation.Find("provider");

        Assert.NotNull(closed);
        Assert.Equal(AutomationRole.ComboBox, closed.Role);
        Assert.Equal("Twitch", closed.Value);
        Assert.False(closed.Has(AutomationStates.Expanded));
        closed.Activate!();
        DrawFrame();
        var tree = ui.Automation;
        var open = tree.Find("provider");
        var items = tree.ChildrenOf(tree.IndexOf("provider")).ToArray().Select(index => tree.Nodes[index]).ToArray();

        Assert.True(open?.Has(AutomationStates.Expanded));
        Assert.Equal(["Kick", "Twitch"], items.Select(item => item.Name));
        Assert.All(items, item => Assert.Equal(AutomationRole.ListItem, item.Role));
        Assert.Equal([false, true], items.Select(item => item.Has(AutomationStates.Selected)));
        items[0].Activate!();
        Assert.Equal(0, chosen);
        Assert.False(ui.HasMenu);

        void DrawFrame()
        {
            ui.Begin(palette, viewport);
            ui.Combo(
                id: "provider",
                text: "Twitch",
                choices: ["Kick", "Twitch"],
                selectedIndex: 1,
                bounds,
                changed: index => chosen = index);
            ui.End();
        }
    }

    [Fact]
    public void ExposeOnlyUncoveredElementsWithTheirGroupAndState()
    {
        var covered = new RectangleF(x: 200, y: 0, width: 200, height: 300);
        var coveredButton = new RectangleF(x: 230, y: 10, width: 80, height: 25);
        var addButton = new RectangleF(x: 10, y: 10, width: 25, height: 25);
        var row = new RectangleF(x: 0, y: 100, width: 190, height: 25);
        ui.Begin(palette, viewport, covered);
        ui.Button(id: "covered", text: "Covered", coveredButton, action: () => { });
        ui.Button(id: "add", text: "+", addButton, action: () => { }, enabled: false);
        ui.BeginGroup(id: "row.0", AutomationRole.DataItem, name: "shroud", row, AutomationStates.Selected);
        ui.Text(text: "shroud", row);
        ui.EndGroup();
        ui.End();
        var tree = ui.Automation;

        Assert.Null(tree.Find("covered"));
        Assert.True(tree.Find("add")?.Has(AutomationStates.Disabled));
        var text = Assert.Single(tree.ChildrenOf(tree.IndexOf("row.0")).ToArray());
        Assert.Equal(AutomationRole.Text, tree.Nodes[text].Role);
        Assert.Equal("shroud", tree.Nodes[text].Name);
        Assert.True(tree.Find("row.0")?.Has(AutomationStates.Selected));
    }

    private sealed class HiddenWindow() : DesktopWindow("Widget regression check", 400, 300);
}
