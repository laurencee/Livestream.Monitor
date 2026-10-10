using System.Drawing;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Direct2D.Native;
using Livestream.Monitor.Direct2D.Widgets;
using Livestream.Monitor.Model.ApiClients;

namespace Livestream.Monitor.Direct2D.Windows;

internal sealed class QualitiesWindow : CanvasWindow
{
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly nint owner;
    private readonly ISettingsHandler settings;
    private readonly IApiClient[] providers;
    private readonly Dictionary<string, FavoriteQualities> drafts = [];
    private int selectedProvider;
    private int scroll;
    private bool confirmingClose;

    public QualitiesWindow(
        nint owner,
        ISettingsHandler settings,
        IApiClient[] providers,
        Func<ThemePalette> theme)
        : base(title: "Stream Qualities", width: 470, height: 300, owner: owner)
    {
        this.owner = owner;
        this.settings = settings;
        this.providers = providers;
        ThemeSource = theme;
        Revert();
        CenterOnOwner(owner);
        SetTopmost();
        Win32.EnableWindow(owner, false);
        Tick();
        Show();
        Win32.SetForegroundWindow(Handle);
        Ui.Focus(QualityInputId);
    }

    public Task Completion => completion.Task;
    private FavoriteQualities Current => drafts[providers[selectedProvider].ApiName];
    private string QualityInputId => "quality." + providers[selectedProvider].ApiName;
    private bool CanAdd
    {
        get
        {
            var value = Ui.Value(QualityInputId).Trim();
            return value.Length != 0 && !Current.Qualities.Contains(value, StringComparer.OrdinalIgnoreCase);
        }
    }

    private bool IsDirty => providers.Any(api =>
    {
        var original = settings.Settings.GetStreamQualities(api.ApiName);
        var draft = drafts[api.ApiName];
        return original.FallbackQuality != draft.FallbackQuality || !original.Qualities.SequenceEqual(draft.Qualities);
    });

    protected override void Draw(SizeF size)
    {
        DrawCaption("STREAM QUALITIES", size.Width);
        var descriptionBounds = new RectangleF(x: 120, y: 35, width: size.Width - 130, height: 22);
        Ui.Text("Quality priority (top item is tried first)", descriptionBounds);

        for (var i = 0; i < providers.Length; i++)
        {
            var index = i;
            var bounds = new RectangleF(x: 10, y: 43 + i * 33, width: 95, height: 28);
            Ui.Button(
                "provider." + i,
                providers[i].ApiName,
                bounds,
                () =>
                {
                    selectedProvider = index;
                    scroll = 0;
                }
            );
            if (i == selectedProvider) Ui.Fill(bounds with { X = 10, Width = 3 }, Ui.Palette.Accent);
        }

        const int VisibleRows = 4;
        scroll = Math.Clamp(scroll, 0, Math.Max(0, Current.Qualities.Count - VisibleRows));

        for (var i = scroll; i < Current.Qualities.Count && i < scroll + VisibleRows; i++)
        {
            var index = i;
            var y = 60 + (i - scroll) * 28;
            Ui.Text(Current.Qualities[i], new RectangleF(x: 120, y: y, width: size.Width - 225, height: 25));
            var upBounds = new RectangleF(x: size.Width - 98, y: y, width: 25, height: 24);
            Ui.Button(
                "up." + i,
                "▲",
                upBounds,
                () => Move(index, -1),
                i > 0,
                toolTip: "Increase priority",
                name: "Move up"
            );
            var downBounds = new RectangleF(x: size.Width - 68, y: y, width: 25, height: 24);
            Ui.Button(
                "down." + i,
                "▼",
                downBounds,
                () => Move(index, 1),
                i < Current.Qualities.Count - 1,
                toolTip: "Decrease priority",
                name: "Move down"
            );
            var removeBounds = new RectangleF(x: size.Width - 38, y: y, width: 25, height: 24);
            Ui.Button(
                "remove." + i,
                "×",
                removeBounds,
                () => Current.Qualities.RemoveAt(index),
                foreground: 0xFFCD5C5C,
                toolTip: "Remove quality",
                name: "Remove"
            );
        }

        var inputBounds = new RectangleF(x: 120, y: 177, width: size.Width - 190, height: 25);
        Ui.Input(QualityInputId, placeholder: "add quality favorite...", inputBounds, enter: Add);
        var addBounds = new RectangleF(x: size.Width - 63, y: 177, width: 50, height: 25);
        var canAdd = CanAdd;
        Ui.Button(
            id: "add",
            text: "Add",
            bounds: addBounds,
            action: Add,
            enabled: canAdd,
            toolTip: "Add new quality option"
        );

        // The radios follow the label at its measured width, so they stay clear of it at any font scaling.
        const string FallbackLabel = "Fallback Quality:";
        const float LabelFontSize = 12;
        const float RadioGap = 8;
        var fallbackWidth = MeasureWidth(FallbackLabel, LabelFontSize) + RadioGap;
        var fallbackBounds = new RectangleF(x: 120, y: 209, width: fallbackWidth, height: 25);
        Ui.Text(FallbackLabel, fallbackBounds, size: LabelFontSize);
        Ui.ToolTip(id: "fallback", fallbackBounds, "This option will always be used if no custom options worked");
        var bestBounds = new RectangleF(x: fallbackBounds.Right, y: 209, width: 65, height: 25);
        var bestSelected = Current.FallbackQuality == FavoriteQualities.FallbackQualityOption.Best;
        Ui.Radio(
            "best",
            "Best",
            bestSelected,
            bestBounds,
            _ => Current.FallbackQuality = FavoriteQualities.FallbackQualityOption.Best
        );
        var worstBounds = bestBounds with { X = bestBounds.Right + 10, Width = 70 };
        var worstSelected = Current.FallbackQuality == FavoriteQualities.FallbackQualityOption.Worst;
        Ui.Radio(
            "worst",
            "Worst",
            worstSelected,
            worstBounds,
            _ => Current.FallbackQuality = FavoriteQualities.FallbackQualityOption.Worst
        );
        var revertBounds = new RectangleF(x: size.Width - 180, y: size.Height - 42, width: 75, height: 27);
        Ui.Button(id: "revert", text: "Revert", bounds: revertBounds, action: Revert, enabled: IsDirty);
        var saveBounds = new RectangleF(x: size.Width - 95, y: size.Height - 42, width: 75, height: 27);
        Ui.Button(id: "save", text: "Save", bounds: saveBounds, action: Save, enabled: IsDirty);
    }

    protected override void OnScroll(int steps) => scroll = Math.Max(0, scroll - steps);

    protected override void RequestClose() => _ = ConfirmClose();

    protected override bool OnDialogKey(int key)
    {
        if (key != VirtualKey.Escape) return false;

        RequestClose();
        return true;
    }

    private async Task ConfirmClose()
    {
        if (confirmingClose) return;

        confirmingClose = true;
        try
        {
            var dialogs = new NativeDialogs(() => this);
            if (!IsDirty || await dialogs.Confirm("Unsaved changes", "Close without saving?")) Close();
        }
        finally
        {
            confirmingClose = false;
        }
    }

    private void Add()
    {
        if (!CanAdd) return;

        var value = Ui.Value(QualityInputId).Trim();
        Current.Qualities.Insert(0, value);
        scroll = 0;
    }

    private void Move(int index, int direction)
    {
        var list = Current.Qualities;
        (list[index], list[index + direction]) = (list[index + direction], list[index]);
    }

    private void Save()
    {
        foreach (var (name, draft) in drafts) settings.Settings.FavoriteApiQualities[name] = Copy(draft);

        settings.SaveSettings();
        NeedsPaint = true;
    }

    private void Revert()
    {
        foreach (var api in providers) drafts[api.ApiName] = Copy(settings.Settings.GetStreamQualities(api.ApiName));

        NeedsPaint = true;
    }

    // Drafts and saved settings never share a list, so editing one leaves the other unchanged.
    private static FavoriteQualities Copy(FavoriteQualities source) => new()
    {
        Qualities = source.Qualities.ToList(),
        FallbackQuality = source.FallbackQuality,
    };

    public override void Close()
    {
        if (IsClosed) return;

        // Windows must be able to activate the owner while destroying the owned window.
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enablewindow
        Win32.EnableWindow(owner, true);
        base.Close();
    }

    protected override void OnClosed() => completion.TrySetResult();
}
