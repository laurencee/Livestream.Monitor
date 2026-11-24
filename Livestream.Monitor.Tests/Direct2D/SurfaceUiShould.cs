using System;
using System.Collections.Generic;
using System.Drawing;
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

    [Theory]
    [InlineData("settings.notifications", false)]
    [InlineData("settings.Twitch.stream.stdout", true)]
    public async System.Threading.Tasks.Task AskBeforeRestartingOnlyWithUnsavedPreferences(string control, bool platform)
    {
        var settings = new Settings();
        var handler = Substitute.For<ISettingsHandler>();
        handler.Settings.Returns(settings);
        var dialogs = Substitute.For<IUserDialogs>();
        dialogs.Confirm(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        var pane = new SettingsPane(handler, dialogs, () => { });
        DrawSettings(pane);
        Click("settings.section.0");
        Click(platform ? "settings.section.1" : "settings.section.5");
        Click(control);

        Assert.Equal(!platform, await pane.ConfirmUpdateRestart(ui));

        void Click(string id)
        {
            ClickWidget(id);
            DrawSettings(pane);
        }
    }

    private static float MeasureText(string text, float fontSize) => text.Length * CharacterWidth;

    private static float MeasureWrappedText(string text, float width, float fontSize, FontWeight weight) =>
        MathF.Ceiling(MeasureText(text, fontSize) / width) * Painter.LineHeight(fontSize);

    [Fact]
    public void KeepActionFeedbackInsideTheWindowWithoutInterceptingClicks()
    {
        // The bottom right corner, where the feedback has no room to the right or below.
        const float X = 399;
        const float Y = 299;
        var feedback = new ActionFeedback();
        feedback.Show("Copied", new PointF(X, Y), now: 0);
        ui.Begin(palette, viewport);
        feedback.Draw(ui, viewport);
        ui.End();

        var background = Assert.Single(commands, command => command.Kind == DrawKind.Fill);
        Assert.True(viewport.Contains(background.Bounds));
        const float EdgeMargin = 4;
        Assert.InRange(X, background.Bounds.Left - EdgeMargin, background.Bounds.Right + EdgeMargin);
        Assert.InRange(Y, background.Bounds.Top - EdgeMargin, background.Bounds.Bottom + EdgeMargin);
        var text = Assert.Single(commands, command => command.Kind == DrawKind.Text);
        Assert.Equal("Copied", text.Text);

        Assert.False(ui.MouseDown(background.Bounds.Location));
        Assert.False(ui.MouseUp(background.Bounds.Location));

        // Nothing is drawn once the feedback has disappeared.
        commands.Clear();
        const int DisappearMilliseconds = 1_500;
        feedback.Tick(DisappearMilliseconds);
        feedback.Draw(ui, viewport);
        Assert.Empty(commands);
    }

    [Fact]
    public void SaveNotificationOptionsWithAnUnavailablePlaybackCommand()
    {
        const string LongBanner = "settings.bannerduration.long";
        var settings = new Settings();
        settings.YouTube.VodCommand.FilePath = "unavailable-vod-player.exe";
        var handler = Substitute.For<ISettingsHandler>();
        handler.Settings.Returns(settings);
        var pane = new SettingsPane(handler, Substitute.For<IUserDialogs>(), () => { });

        DrawSettings(pane);
        Assert.Null(ui.Automation.Find(LongBanner));
        ClickWidget("settings.section.5");
        DrawSettings(pane);
        ClickWidget(LongBanner);
        DrawSettings(pane);

        Assert.True(settings.UseLongStreamNotificationBanners);
        handler.Received(1).SaveSettings();
    }

    [Fact]
    public void SaveAndRevertOnePlatformWhileAnotherHasAnInvalidDraft()
    {
        const string InvalidPathId = "YouTube.vod.path";
        const string MissingPlayer = "unavailable-vod-player.exe";
        const string SavedArgs = "saved args";
        var executable = Assert.IsType<string>(Environment.ProcessPath);
        var settings = new Settings { MetroThemeBaseColour = MetroThemeBaseColour.BaseLight };
        var twitchArgs = settings.Twitch.StreamCommand.Args;
        foreach (var platform in new ApiPlatformSettings[] { settings.Twitch, settings.Kick, settings.YouTube })
        {
            platform.StreamCommand.FilePath = executable;
            platform.VodCommand.FilePath = executable;
            platform.ChatCommand.FilePath = executable;
        }

        var handler = Substitute.For<ISettingsHandler>();
        handler.Settings.Returns(settings);
        handler.TrySavePlatformSettings(Arg.Any<TwitchSettings>()).Returns(call =>
        {
            settings.Twitch = call.Arg<TwitchSettings>();
            return true;
        });
        var pane = new SettingsPane(handler, Substitute.For<IUserDialogs>(), () => { });
        var testPalette = Theme.Create(settings);

        Draw();
        ClickWidget("settings.section.0");
        Draw();
        ClickWidget("settings.section.3");
        Draw();
        ui.SetValue(InvalidPathId, MissingPlayer);
        ClickWidget("settings.section.3");
        Draw();
        ClickWidget("settings.section.1");
        Draw();
        ui.SetValue("Twitch.stream.args", SavedArgs);
        Draw();

        Assert.Equal(twitchArgs, settings.Twitch.StreamCommand.Args);
        Assert.True(pane.RevealPendingPlatform(ui));
        Draw();
        Draw();
        pane.FocusRevealedControl(ui);
        ClickWidget("settings.Twitch.save");
        handler.Received(1).TrySavePlatformSettings(Arg.Any<TwitchSettings>());
        Assert.Equal(SavedArgs, settings.Twitch.StreamCommand.Args);
        Assert.Equal(executable, settings.YouTube.VodCommand.FilePath);

        ui.SetValue("Twitch.stream.args", "second draft");
        Draw();
        ClickWidget("settings.Twitch.revert");
        Assert.Equal(SavedArgs, ui.Value("Twitch.stream.args"));
        Assert.Equal(SavedArgs, settings.Twitch.StreamCommand.Args);
        pane.ScrollWithKey(VirtualKey.Home);
        Draw();
        ClickWidget("settings.section.1");
        Draw();

        Assert.True(pane.RevealPendingPlatform(ui));
        Draw();
        Draw();
        Assert.True(ui.Automation.Find("settings.YouTube.save")?.Has(AutomationStates.Disabled));
        ClickWidget("settings.YouTube.showerror");
        Draw();
        Draw();
        var input = Assert.IsType<AutomationNode>(ui.Automation.Find(InvalidPathId));
        var inputFill = Assert.Single(commands, command => command.Kind == DrawKind.Fill && command.Color == testPalette.Input &&
            command.Bounds.Height.Equals(input.Bounds.Height) && command.Bounds.IntersectsWith(input.Bounds));

        Assert.Equal(inputFill.Bounds, input.Bounds);
        Assert.True(pane.RevealPendingPlatform(ui));
        Draw();
        Draw();
        pane.FocusRevealedControl(ui);
        ClickWidget("settings.YouTube.revert");
        Assert.Equal(executable, ui.Value(InvalidPathId));
        Assert.False(pane.RevealPendingPlatform(ui));
        handler.Received(1).TrySavePlatformSettings(Arg.Any<TwitchSettings>());

        void Draw()
        {
            commands.Clear();
            ui.Begin(testPalette, viewport with { Height = 900 });
            pane.Draw(ui, viewport with { Height = 900 });
            ui.End();
        }
    }

    [Fact]
    public void RevealAndFocusTheRecoveryActionInAShortSettingsPane()
    {
        const string RevertId = "settings.Kick.revert";
        const float ShortWindowHeight = 100;
        var handler = Substitute.For<ISettingsHandler>();
        handler.Settings.Returns(new Settings());
        var pane = new SettingsPane(handler, Substitute.For<IUserDialogs>(), () => { });

        DrawSettings(pane);
        ClickWidget("settings.section.2");
        DrawSettings(pane);
        ui.SetValue("Kick.stream.path", "unavailable-kick-player.exe");

        var shortViewport = viewport with { Height = ShortWindowHeight };
        Assert.True(pane.RevealPendingPlatform(ui));
        DrawShortPane();
        DrawShortPane();
        pane.FocusRevealedControl(ui);
        DrawShortPane();

        Assert.True(ui.Automation.Find(RevertId)?.Has(AutomationStates.Focused));

        void DrawShortPane()
        {
            ui.Begin(palette, shortViewport);
            pane.Draw(ui, shortViewport);
            ui.End();
        }
    }

    [Fact]
    public void InvokeAnAutomationOnlyButtonAsLongAsItIsAvailable()
    {
        const string ComboId = "combo";
        const string ButtonId = "automation-only";
        var comboBounds = new RectangleF(x: 10, y: 10, width: 100, height: 24);
        var buttonBounds = comboBounds with { X = 200 };
        var invocations = 0;
        ui.Begin(palette, viewport);
        ui.Combo(id: ComboId, name: "Combo", text: "Option", choices: ["Option"], selectedIndex: 0, bounds: comboBounds, changed: _ => { });
        ui.AddAutomationButton(id: ButtonId, name: "Scroll", bounds: buttonBounds, action: Scroll, enabled: true);
        ui.End();

        var button = Assert.IsType<AutomationNode>(ui.Automation.Find(ButtonId));
        var invoke = Assert.IsType<Action>(button.Activate);
        Assert.False(ui.ContainsHit(new PointF(buttonBounds.X + 1, buttonBounds.Y + 1)));
        ClickWidget(ComboId);
        Assert.True(ui.HasMenu);

        invoke();
        Assert.False(ui.HasMenu);
        Assert.Equal(1, invocations);

        ui.Begin(palette, viewport);
        ui.End();
        invoke();
        Assert.Equal(1, invocations);

        void Scroll() => invocations++;
    }

    [Fact]
    public void WrapBannerDurationArrowNavigationWithinItsOwnGroup()
    {
        var handler = Substitute.For<ISettingsHandler>();
        handler.Settings.Returns(new Settings());
        var pane = new SettingsPane(handler, Substitute.For<IUserDialogs>(), () => { });

        DrawSettings(pane);
        ClickWidget("settings.section.5");
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
    public void SizeTextRowsToTheirMeasuredContent()
    {
        const float MeasuredHeight = 48;
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
                return MeasuredHeight;
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
        Assert.Equal(MeasuredHeight, note.Bounds.Height);
        Assert.Equal(note.Bounds.Bottom + Spacing, measuredUi.Automation.Find("next")!.Bounds.Y);
    }

    [Fact]
    public void NameTheInputAndHostItsElementOnTheNativeInputFocusedFromItsPlaceholder()
    {
        const string Placeholder = "add livestream...";
        var textPattern = Interop.UIAutomationClient.UIA_PatternIds.UIA_TextPatternId;
        DrawInput();
        var drawn = Assert.IsType<AutomationNode>(ui.Automation.Find("add"));

        Assert.Equal(AutomationRole.Edit, drawn.Role);
        Assert.Equal("New channel", drawn.Name);
        Assert.Equal(Placeholder, drawn.HelpText);
        Assert.Equal(0, drawn.Window);
        Assert.True(ElementProvider.Supports(drawn, textPattern));
        Assert.False(ElementProvider.Supports(drawn with { Value = "channel" }, textPattern));
        ui.MouseDown(new PointF(20, 20));
        ui.MouseUp(new PointF(20, 20));

        // The element is hosted as the focus moves, before the next frame, so clients reading the focused native input find
        // its name.
        Assert.True(ui.HasFocusedInput);
        Assert.NotEqual(0, ui.Automation.Find("add")!.Window);
        ui.SetValue("add", "channel");
        Assert.Equal("channel", ui.Value("add"));

        // With text, the element stays hosted on the shown native input, which supplies its text.
        DrawInput();
        var hosted = Assert.IsType<AutomationNode>(ui.Automation.Find("add"));
        Assert.Equal("New channel", hosted.Name);
        Assert.NotEqual(0, hosted.Window);
        Assert.False(ElementProvider.Supports(hosted, textPattern));

        void DrawInput()
        {
            var bounds = new RectangleF(x: 10, y: 10, width: 150, height: 24);
            ui.Begin(palette, viewport);
            ui.Input("add", "New channel", Placeholder, bounds);
            ui.End();
        }
    }

    // Caption buttons are not focusable, so clicking one leaves the focus where it was, and a selected grid row keeps its
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

        // Tab skips the caption button drawn between the two buttons.
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

    // Tab cycles inside an open pane instead of reaching the toolbar behind it.
    [Fact]
    public void KeepTabInsideTheFocusScope()
    {
        DrawPaneBesideToolbar();
        ui.Focus("pane.close");

        ui.Key(VirtualKey.Tab, shift: false);
        DrawPaneBesideToolbar();
        ui.Key(VirtualKey.Tab, shift: false);
        DrawPaneBesideToolbar();

        Assert.True(ui.Automation.Find("pane.close")?.Has(AutomationStates.Focused));
    }

    [Fact]
    public void TabBetweenAnInputAndAnEmptyListInBothDirections()
    {
        const string ListKey = "vod.tiles";
        DrawInputAndList();
        ui.Focus("filter");

        Assert.True(ui.Key(VirtualKey.Tab, shift: false));
        DrawInputAndList();
        var list = Assert.IsType<AutomationNode>(ui.Automation.Find(ListKey));
        Assert.Equal("tiles", list.AutomationId);
        Assert.True(list.Has(AutomationStates.Focused));
        Assert.False(ui.Key(VirtualKey.Enter, shift: false));

        Assert.True(ui.Key(VirtualKey.Tab, shift: true));
        Assert.True(ui.HasFocusedInput);

        void DrawInputAndList()
        {
            ui.Begin(palette, viewport);
            var filter = new RectangleF(x: 10, y: 10, width: 90, height: 24);
            var bounds = new RectangleF(x: 10, y: 40, width: 300, height: 200);
            ui.Input("filter", "Filter", "Name...", filter);
            ui.BeginList(id: "tiles", name: "VODs", bounds, selectedKey: null, focus: () => ui.Focus(ListKey), contentKey: ListKey);
            ui.EndList();
            ui.End();
        }
    }

    [Fact]
    public void RestoreFocusToTheSelectedItemInAReplacementList()
    {
        const string ListId = "rows";
        const string RowKey = "row:twitch:shroud";
        ui.Focus("tiles");
        ui.Begin(palette, viewport);
        var list = new RectangleF(x: 10, y: 10, width: 300, height: 240);
        var row = list with { Height = 24 };
        ui.BeginList(id: ListId, name: "Livestreams", bounds: list, selectedKey: RowKey, focus: () => ui.Focus(ListId));
        ui.BeginGroup(id: "row.0", AutomationRole.DataItem, name: "shroud", row, AutomationStates.Selected, key: RowKey);
        ui.EndGroup();
        ui.EndList();

        Assert.True(ui.TryReplaceMissingFocus(preferred: "covered", alternate: ListId));
        ui.End();

        Assert.Equal(ListId, ui.FocusedId);
        var tree = ui.Automation;
        Assert.True(tree.Find(RowKey)?.Has(AutomationStates.Focused));
        Assert.False(tree.Find(ListId)?.Has(AutomationStates.Focused));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public void TransferImplicitListFocusOnlyForADrawnSelection(bool drawItem, bool allowImplicitFocus, bool transfers)
    {
        const string RowKey = "row:twitch:shroud";
        ui.ClearFocus();
        ui.Begin(palette, viewport);
        var bounds = new RectangleF(x: 10, y: 10, width: 300, height: 240);
        ui.BeginList(id: "rows", name: "Livestreams", bounds, selectedKey: RowKey, focus: () => ui.Focus("rows"));

        if (drawItem)
        {
            var row = bounds with { Height = 24 };
            var states = AutomationStates.Selected | AutomationStates.Focused;
            var item = new AutomationItem(Position: 1, Count: 1, Status: "", Act: _ => { });
            ui.BeginGroup(id: "row.0", AutomationRole.DataItem, name: "shroud", row, states, key: RowKey, item: item);
            ui.EndGroup();
        }

        ui.EndList();
        ui.End();

        Assert.Equal(transfers, ui.TryTransferListFocus("rows", "tiles", allowImplicitFocus));
        Assert.Equal(transfers ? "tiles" : null, ui.FocusedId);
    }

    // Widgets in drawing order: a toolbar button, then a pane's close and save buttons inside the focus scope.
    private void DrawPaneBesideToolbar()
    {
        var toolbar = new RectangleF(x: 10, y: 10, width: 60, height: 24);
        var close = new RectangleF(x: 300, y: 10, width: 60, height: 24);
        var save = new RectangleF(x: 300, y: 50, width: 60, height: 24);
        ui.Begin(palette, viewport);
        ui.Button(id: "toolbar", text: "Toolbar", bounds: toolbar, action: () => { });
        var list = new RectangleF(x: 10, y: 40, width: 190, height: 200);
        ui.BeginList(id: "rows", name: "Livestreams", bounds: list, selectedKey: null, focus: () => ui.Focus("rows"));
        ui.EndList();
        ui.BeginFocusScope();
        ui.Button(id: "pane.close", text: "Close", bounds: close, action: () => { });
        ui.Button(id: "pane.save", text: "Save", bounds: save, action: () => { });
        ui.EndFocusScope();
        ui.End();
    }

    [Fact]
    public void SkipDisabledMenuItemsWhenChoosingWithTheKeyboard()
    {
        var chosen = -1;
        SurfaceUi.MenuChoice[] choices = [new("Open in Vod Viewer", Enabled: false), new("Enable Notifications", Checked: true)];
        ui.Popup(id: "context", anchor: new RectangleF(new PointF(10, 10), SizeF.Empty), choices, chosen: index => chosen = index);

        Assert.True(ui.Key(VirtualKey.Down, shift: false));
        Assert.True(ui.Key(VirtualKey.Enter, shift: false));

        Assert.False(ui.HasMenu);
        Assert.Equal(1, chosen);

        // Enter with no suggestion highlighted submits the text in the list's focused input, as it does without the list.
        var submitted = 0;
        var filter = new RectangleF(x: 10, y: 10, width: 150, height: 24);
        ui.Begin(palette, viewport);
        ui.Input("game", "Game filter", "Partial game name...", filter, enter: () => submitted++);
        ui.End();
        ui.MouseDown(new PointF(20, 20));
        ui.MouseUp(new PointF(20, 20));
        Assert.True(ui.HasFocusedInput);
        ui.DropDown(id: "suggestions", name: "Suggestions", filter, choices: ["Game"], chosen: index => chosen = index, input: "game");

        Assert.True(ui.Key(VirtualKey.Enter, shift: false));

        Assert.False(ui.HasMenu);
        Assert.Equal(1, submitted);
        Assert.Equal(1, chosen);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void UseMostRecentInputForStandaloneContextMenuSelection(bool cursorMoves, int expectedChoice)
    {
        var chosen = -1;
        SurfaceUi.MenuChoice[] choices = [new("Show"), new("Exit")];
        var size = ui.Popup(id: "tray", anchor: null, choices: choices, chosen: index => chosen = index);
        var menuViewport = new RectangleF(PointF.Empty, size);
        ui.Begin(palette, menuViewport);
        ui.End();

        var showPosition = new PointF(size.Width / 2, size.Height / 4);
        ui.MouseMove(showPosition);
        ui.Key(VirtualKey.Up, shift: false);
        var cursorPosition = cursorMoves ? showPosition with { X = showPosition.X + 1 } : showPosition;
        ui.MouseMove(cursorPosition);
        ui.Key(VirtualKey.Enter, shift: false);

        Assert.Equal(expectedChoice, chosen);
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
        ui.DropDown(id: "suggestions", name: "Suggestions", gameFilter, choices: ["Game"], chosen: _ => activations++);
        Assert.True(ui.HasMenu);

        ui.ModalOverlay();

        Assert.False(ui.HasMenu);
        Assert.False(ui.MouseDown(new PointF(20, 20)));
        Assert.False(ui.MouseUp(new PointF(20, 20)));
        Assert.Equal(0, activations);
    }

    [Fact]
    public void CloseAnOpenComboBoxWhenItBecomesDisabled()
    {
        var bounds = new RectangleF(x: 10, y: 10, width: 80, height: 24);
        var chosen = -1;
        DrawFrame(enabled: true);
        ui.Automation.Find("provider")!.Activate!();
        DrawFrame(enabled: true);
        var openTree = ui.Automation;
        var firstChoice = openTree.ChildrenOf(openTree.IndexOf("provider"))[0];
        var activateChoice = openTree.Nodes[firstChoice].Activate!;

        DrawFrame(enabled: false);
        activateChoice();

        Assert.Equal(-1, chosen);
        Assert.False(ui.HasMenu);

        void DrawFrame(bool enabled)
        {
            ui.Begin(palette, viewport);
            ui.Combo(
                id: "provider",
                name: "Platform",
                text: "Twitch",
                choices: ["Kick", "Twitch"],
                selectedIndex: 1,
                bounds,
                changed: index => chosen = index,
                enabled);
            ui.End();
        }
    }

    [Fact]
    public void ExposeOnlyUncoveredElementsWithTheirGroupAndState()
    {
        var covered = new RectangleF(x: 200, y: 0, width: 200, height: 300);
        var coveredButton = new RectangleF(x: 230, y: 10, width: 80, height: 25);
        var addButton = new RectangleF(x: 10, y: 10, width: 25, height: 25);
        var list = new RectangleF(x: 0, y: 100, width: 400, height: 100);
        var row = new RectangleF(x: 0, y: 100, width: 190, height: 25);
        ui.Begin(palette, viewport, covered);
        ui.Button(id: "covered", text: "Covered", coveredButton, action: () => { });
        ui.Button(id: "add", text: "+", addButton, action: () => { }, enabled: false);
        const string RowKey = "row:twitch:shroud";
        ui.BeginList(id: "rows", name: "Livestreams", bounds: list, selectedKey: RowKey, focus: () => ui.Focus("rows"));
        ui.BeginGroup(id: "row.0", AutomationRole.DataItem, name: "shroud", row, AutomationStates.Selected, key: RowKey);
        ui.Text(text: "shroud", row);
        ui.EndGroup();
        ui.EndList();
        ui.End();
        var tree = ui.Automation;

        Assert.Null(tree.Find("covered"));
        Assert.True(tree.Find("add")?.Has(AutomationStates.Disabled));

        // The partly covered list keeps its uncovered row, which is keyed by its content rather than its id.
        Assert.Equal(RowKey, tree.Find("rows")?.Value);
        var rowIndex = tree.IndexOf(RowKey);
        Assert.Equal(tree.IndexOf("rows"), tree.Nodes[rowIndex].Parent);
        Assert.Equal("row.0", tree.Nodes[rowIndex].AutomationId);
        Assert.True(tree.Nodes[rowIndex].Has(AutomationStates.Selected));
        var text = Assert.Single(tree.ChildrenOf(rowIndex).ToArray());
        Assert.Equal(AutomationRole.Text, tree.Nodes[text].Role);
        Assert.Equal("shroud", tree.Nodes[text].Name);
    }

    private sealed class HiddenWindow() : DesktopWindow("Widget regression check", 400, 300);
}
