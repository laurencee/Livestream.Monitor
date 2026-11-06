using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Core.UI;
using Livestream.Monitor.Direct2D.Automation;
using Livestream.Monitor.Direct2D.Native;
using Livestream.Monitor.Direct2D.Rendering;
using Livestream.Monitor.Direct2D.Widgets;
using Livestream.Monitor.Direct2D.Windows;
using Livestream.Monitor.Direct2D.Windows.Monitor;
using Livestream.Monitor.Core;
using NSubstitute;
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
        ui = new SurfaceUi(window, commands) { MeasureWidth = MeasureText, MeasureWrapped = MeasureWrappedText };
        palette = Theme.Create(new Settings());
        viewport = new RectangleF(x: 0, y: 0, width: 400, height: 300);
    }

    public void Dispose() => window.Dispose();

    private static float MeasureText(string text, float fontSize) => text.Length * CharacterWidth;

    private static float MeasureWrappedText(string text, float width, float fontSize, FontWeight weight) =>
        MathF.Ceiling(MeasureText(text, fontSize) / width) * Painter.LineHeight(fontSize);

    [Fact]
    public void ReplaceAndFadeActionFeedbackWithoutInterceptingClicks()
    {
        var feedback = new ActionFeedback();
        feedback.Show("First action", PointF.Empty, now: 0);
        const int RestartMilliseconds = 500;
        const int FadeMidpointMilliseconds = RestartMilliseconds + 1_375;
        var clickPosition = new PointF(50, 80);
        feedback.Show("Second action", clickPosition, RestartMilliseconds);
        feedback.Tick(FadeMidpointMilliseconds);
        ui.Begin(palette, viewport);
        feedback.Draw(ui, viewport);
        ui.End();

        var text = Assert.Single(commands, command => command.Kind == DrawKind.Text);
        Assert.Equal("Second action", text.Text);
        Assert.True(viewport.Contains(text.Bounds));
        var background = Assert.Single(commands, command => command.Kind == DrawKind.Fill);
        Assert.Equal(clickPosition, background.Bounds.Location);
        const int AlphaShift = 24;
        const uint HalfOpacityAlpha = 128;
        foreach (var command in commands) Assert.Equal(HalfOpacityAlpha, command.Color >> AlphaShift);

        var point = new PointF(text.Bounds.X, text.Bounds.Y);
        Assert.False(ui.MouseDown(point));
        Assert.False(ui.MouseUp(point));

        commands.Clear();
        const int DisappearMilliseconds = RestartMilliseconds + 1_500;
        feedback.Tick(DisappearMilliseconds);
        feedback.Draw(ui, viewport);
        Assert.Empty(commands);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(399, 299)]
    [InlineData(399, 80)]
    [InlineData(50, 299)]
    public void KeepActionFeedbackInsideTheWindowAtEdgeClicks(float x, float y)
    {
        var feedback = new ActionFeedback();
        feedback.Show("Copied", new PointF(x, y), now: 0);
        ui.Begin(palette, viewport);
        feedback.Draw(ui, viewport);
        ui.End();

        var background = Assert.Single(commands, command => command.Kind == DrawKind.Fill);
        Assert.True(viewport.Contains(background.Bounds));
        const float EdgeMargin = 4;
        Assert.InRange(x, background.Bounds.Left - EdgeMargin, background.Bounds.Right + EdgeMargin);
        Assert.InRange(y, background.Bounds.Top - EdgeMargin, background.Bounds.Bottom + EdgeMargin);
    }

    [Fact]
    public void KeepMeasuredSettingsTextInsideTheGroupPadding()
    {
        var settings = new Settings();
        settings.Twitch.StreamCommand.FilePath = Environment.ProcessPath;
        var handler = Substitute.For<ISettingsHandler>();
        handler.Settings.Returns(settings);
        var pane = new SettingsPane(handler, Substitute.For<IUserDialogs>(), () => { });
        Draw();
        ClickSection("settings.section.0");
        ClickSection("settings.section.1");
        var label = commands.First(command => command.Text == "File path");
        var border = commands.Where(command => command.Kind == DrawKind.Stroke && command.Bounds.Contains(label.Bounds))
            .MinBy(command => command.Bounds.Width * command.Bounds.Height);
        var leftInset = label.Bounds.Left - border.Bounds.Left;
        var rightInset = border.Bounds.Right - label.Bounds.Right;
        Assert.True(leftInset > 0);
        Assert.Equal(leftInset, rightInset);

        void Draw()
        {
            ui.Begin(palette, viewport);
            pane.Draw(ui, viewport);
            ui.End();
        }

        void ClickSection(string id)
        {
            var bounds = ui.Automation.Find(id)!.Bounds;
            var point = new PointF(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
            ui.MouseDown(point);
            ui.MouseUp(point);
            Draw();
        }
    }

    [Fact]
    public void SaveBannerDurationWithAnUnavailablePlaybackCommand()
    {
        var settings = new Settings();
        settings.YouTube.VodCommand.FilePath = "unavailable-vod-player.exe";
        var handler = Substitute.For<ISettingsHandler>();
        handler.Settings.Returns(settings);
        var pane = new SettingsPane(handler, Substitute.For<IUserDialogs>(), () => { });

        DrawSettings(pane);
        ClickWidget("settings.bannerduration.long");
        DrawSettings(pane);
        ClickWidget("settings.save");

        Assert.True(settings.UseLongStreamNotificationBanners);
        handler.Received(1).SaveSettings();
    }

    [Fact]
    public void WrapBannerDurationArrowNavigationWithinItsOwnGroup()
    {
        var handler = Substitute.For<ISettingsHandler>();
        handler.Settings.Returns(new Settings());
        var pane = new SettingsPane(handler, Substitute.For<IUserDialogs>(), () => { });

        DrawSettings(pane);
        ClickWidget("settings.section.4");
        DrawSettings(pane);
        Assert.NotNull(ui.Automation.Find("settings.quality.best"));
        ClickWidget("settings.bannerduration.long");
        Assert.True(ui.Key(VirtualKey.Right, shift: false));
        DrawSettings(pane);

        Assert.True(ui.Automation.Find("settings.bannerduration.short")?.Has(AutomationStates.Selected));
    }

    private void DrawSettings(SettingsPane pane)
    {
        var tallViewport = viewport with { Height = 1000 };
        ui.Begin(palette, tallViewport);
        pane.Draw(ui, tallViewport);
        ui.End();
    }

    private void ClickWidget(string id)
    {
        var bounds = Assert.IsType<AutomationNode>(ui.Automation.Find(id)).Bounds;
        var center = new PointF(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        Assert.True(ui.MouseDown(center));
        Assert.True(ui.MouseUp(center));
    }

    [Fact]
    public void AlignQualityRowActionsWithTheAddButton()
    {
        var settings = new Settings { FavoriteApiQualities = { ["all"] = new FavoriteQualities { Qualities = ["720p"] } } };
        var pane = new StreamQualityPane(settings);
        ui.Begin(palette, viewport);
        var y = viewport.Y;
        pane.Draw(ui, viewport, ref y);
        ui.End();

        Assert.Equal(ui.Automation.Find("settings.quality.add")!.Bounds.Right, ui.Automation.Find("settings.quality.remove.0")!.Bounds.Right);
    }

    [Fact]
    public void ClearUnaddedQualityInputWhenReverting()
    {
        var pane = new StreamQualityPane(new Settings());
        ui.Begin(palette, viewport);
        var y = viewport.Y;
        pane.Draw(ui, viewport, ref y);
        ui.End();
        const string InputId = "settings.quality.input.all";
        ui.SetValue(InputId, "unadded");

        pane.Revert(ui);

        Assert.Equal("", ui.Value(InputId));
    }

    [Theory]
    [InlineData(16f)]
    [InlineData(48f)]
    public void SizeTextRowsToTheirMeasuredContent(float measuredHeight)
    {
        const string Message = "A note that can wrap when the pane becomes narrow";
        const float Spacing = 4;
        const float FontSize = 12;
        var measuredUi = new SurfaceUi(window, commands)
        {
            MeasureWidth = MeasureText,
            // The callback verifies the renderer arguments.
            // ReSharper disable ParameterOnlyUsedForPreconditionCheck.Local
            MeasureWrapped = (text, width, size, weight) =>
            {
                Assert.Equal(Message, text);
                Assert.Equal(viewport.Width, width);
                Assert.Equal(FontSize, size);
                Assert.Equal(FontWeight.SemiBold, weight);
                return measuredHeight;
            },
            // ReSharper restore ParameterOnlyUsedForPreconditionCheck.Local
        };
        measuredUi.Begin(palette, viewport);
        var y = viewport.Y;
        measuredUi.TextRow(Message, viewport, ref y, spacing: Spacing, size: FontSize, weight: FontWeight.SemiBold);
        const float ButtonHeight = 26;
        var nextBounds = viewport with { Y = y, Height = ButtonHeight };
        measuredUi.Button(id: "next", text: "Next", bounds: nextBounds, action: () => { });
        measuredUi.End();

        var note = Assert.Single(commands, command => command.Kind == DrawKind.WrappedText);
        Assert.Equal(measuredHeight, note.Bounds.Height);
        Assert.Equal(note.Bounds.Bottom + Spacing, measuredUi.Automation.Find("next")!.Bounds.Y);
    }

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
        var listUi = new SurfaceUi(window, commands) { MeasureWidth = MeasureText, MeasureWrapped = MeasureWrappedText, ListMaximumHeight = maximumHeight };
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

    [Theory]
    [InlineData(MetroThemeBaseColour.BaseDark, 0xFF252525u, 0xFF313131u, 0xFF7E7E7Eu)]
    [InlineData(MetroThemeBaseColour.BaseLight, 0xFFFFFFFFu, 0xFFDEDEDEu, 0xFF7F7F7Fu)]
    public void DrawTheWpfContextMenuColours(
        MetroThemeBaseColour theme,
        uint background,
        uint hover,
        uint disabledText)
    {
        const string DisabledLabel = "Unavailable";
        var menuPalette = Theme.Create(new Settings { MetroThemeBaseColour = theme, MetroThemeAccentColour = MetroThemeAccentColour.Orange });
        SurfaceUi.MenuChoice[] choices = [new("Open", Checked: true), new(DisabledLabel, Enabled: false)];
        ui.Popup(id: "context", position: new PointF(10, 10), choices, chosen: _ => { });
        ui.Begin(menuPalette, viewport);
        ui.MouseMove(new PointF(40, 20));
        ui.End();

        var fills = commands.Where(command => command.Kind == DrawKind.Fill).Select(command => command.Color);
        Assert.Equal([background, hover], fills);
        var label = Assert.Single(commands, command => command is { Kind: DrawKind.Text, Text: DisabledLabel });
        Assert.Equal(disabledText, label.Color);
        var border = theme == MetroThemeBaseColour.BaseLight ? 0xE0717070u : 0xFFCCCCCCu;
        var checkmark = theme == MetroThemeBaseColour.BaseLight ? 0xCCFA6800u : 0xFFFA6800u;
        Assert.Equal(border, Assert.Single(commands, command => command.Kind == DrawKind.Stroke).Color);
        Assert.Equal(checkmark, Assert.Single(commands, command => command.Kind == DrawKind.Icon).Color);
    }

    [Fact]
    public void DrawLightThemeCheckBoxesAndRadioButtonsWithMahAppsColours()
    {
        var settings = new Settings
        {
            MetroThemeBaseColour = MetroThemeBaseColour.BaseLight,
            MetroThemeAccentColour = MetroThemeAccentColour.Orange,
        };

        var light = Theme.Create(settings);
        var checkBounds = new RectangleF(x: 10, y: 10, width: 120, height: 24);
        var radioBounds = new RectangleF(x: 10, y: 40, width: 120, height: 24);

        ui.Begin(light, viewport);
        ui.Check(id: "check", text: "Check", value: true, checkBounds, changed: _ => { });
        ui.Radio(id: "radio", text: "Radio", value: true, radioBounds, changed: _ => { }, group: "choices");
        ui.End();

        Assert.Equal(0xFFFFFFFFu, Assert.Single(commands, command => command.Kind == DrawKind.Fill).Color);
        Assert.Equal(0xFFB9B9B9u, Assert.Single(commands, command => command.Kind == DrawKind.Stroke).Color);
        Assert.Equal(0xFFB94C00u, Assert.Single(commands, command => command.Kind == DrawKind.Icon).Color);
        var radioColours = commands.Where(command => command.Kind == DrawKind.Ellipse).Select(command => command.Color);
        Assert.Equal([0xFFB9B9B9u, 0xFFFFFFFFu, 0xFFB94C00u], radioColours);
    }

    [Fact]
    public void DrawDisabledLightButtonsWithMahAppsColours()
    {
        var light = Theme.Create(new Settings { MetroThemeBaseColour = MetroThemeBaseColour.BaseLight });
        var bounds = new RectangleF(x: 10, y: 10, width: 120, height: 24);
        ui.Begin(light, viewport);
        ui.MouseMove(new PointF(40, 20));
        ui.Button(id: "disabled", text: "Disabled", bounds, action: () => { }, enabled: false);
        ui.End();

        Assert.Equal(0xFFFBFBFBu, Assert.Single(commands, command => command.Kind == DrawKind.Fill).Color);
        Assert.Equal(0xFFE3E3E3u, Assert.Single(commands, command => command.Kind == DrawKind.Stroke).Color);
        Assert.Equal(0xFFB0B0B0u, Assert.Single(commands, command => command.Kind == DrawKind.Text).Color);
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
        ui.Radio(id: "best", text: "Best", value: true, bestBounds, changed: _ => best = true, group: "quality");
        ui.Radio(id: "worst", text: "Worst", value: false, worstBounds, changed: _ => best = false, group: "quality");

        ui.MouseDown(new PointF(110, 20));
        ui.MouseUp(new PointF(110, 20));
        Assert.False(best);
        ui.MouseDown(new PointF(110, 20));
        ui.MouseUp(new PointF(110, 20));
        Assert.False(best);

        const int LeftKey = 37;
        Assert.True(ui.Key(LeftKey, shift: false));
        Assert.True(best);

        ui.Begin(palette, viewport);
        ui.PushClip(viewport with { Y = 100 });
        ui.Radio(id: "best", text: "Best", value: true, bestBounds, changed: _ => best = true, group: "quality");
        ui.Radio(id: "worst", text: "Worst", value: false, worstBounds, changed: _ => best = false, group: "quality");
        ui.PopClip();

        Assert.False(ui.Key(LeftKey, shift: false));
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
