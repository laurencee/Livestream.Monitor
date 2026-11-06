using System.Drawing;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Direct2D.Rendering;
using Livestream.Monitor.Direct2D.Widgets;

namespace Livestream.Monitor.Direct2D.Windows.Monitor;

internal sealed class StreamQualityPane(Settings settings)
{
    private const float RowHeight = 26;
    private const float Gap = 4;
    private const float ActionWidth = 26;
    private const int ActionCount = 3;
    private const float RadioHeight = 22;
    private const string QualityInputPrefix = "settings.quality.input.";
    private static readonly string[] PlatformNames = StreamQualityDraft.Platforms.Select(platform => platform.DisplayName).ToArray();
    private readonly StreamQualityDraft draft = new(settings);
    private int selectedPlatform;

    public bool HasChanges => draft.HasChanges;

    public void SaveChanges() => draft.SaveChanges();

    public void Revert(SurfaceUi ui)
    {
        draft.Revert();

        foreach (var (apiName, _) in StreamQualityDraft.Platforms)
        {
            var id = QualityInputPrefix + apiName;
            if (ui.Value(id).Length != 0) ui.SetValue(id, "");
        }
    }

    public void Draw(SurfaceUi ui, RectangleF body, ref float y)
    {
        var apiName = StreamQualityDraft.Platforms[selectedPlatform].ApiName;
        var current = draft[apiName];
        var inputId = QualityInputPrefix + apiName;

        ui.TextRow(text: "Platform", body, ref y, spacing: Gap);
        ui.Combo(
            id: "settings.quality.platform",
            text: PlatformNames[selectedPlatform],
            choices: PlatformNames,
            selectedIndex: selectedPlatform,
            bounds: Row(body, ref y),
            changed: index => selectedPlatform = index);

        y += SettingsPane.GroupGap;

        if (apiName != Settings.AllStreamQualitiesKey && draft.HasAllPreferences)
        {
            const uint OverrideNoteColor = 0xFFCD5C5C;
            ui.TextRow(text: "Custom preferences here replace 'All'.", body, ref y, spacing: Gap, color: OverrideNoteColor);
        }

        ui.TextRow(text: "Preferred qualities", body, ref y, spacing: Gap, weight: FontWeight.SemiBold);
        ui.TextRow(text: "Qualities are tried from top to bottom.", body, ref y, spacing: Gap, color: ui.Palette.MutedText);

        if (current.Qualities.Count == 0)
        {
            var inheritsAll = apiName != Settings.AllStreamQualitiesKey && draft.HasAllPreferences && !current.IsConfigured;
            var emptyHint = inheritsAll ? "[Using 'All' settings]" : "[Using the fallback quality]";
            ui.TextRow(emptyHint, body, ref y, spacing: SettingsPane.GroupGap, color: ui.Palette.MutedText);
        }

        for (var i = 0; i < current.Qualities.Count; i++)
        {
            var index = i;
            var row = Row(body, ref y);
            var text = row with { Width = row.Width - (ActionWidth + Gap) * ActionCount };
            ui.Text($"{i + 1}   {current.Qualities[i]}", text);
            var up = row with { X = text.Right + Gap, Width = ActionWidth };
            var down = up with { X = up.Right + Gap };
            var remove = down with { X = down.Right + Gap };
            ui.Button(
                id: "settings.quality.up." + i,
                text: "↑",
                bounds: up,
                action: () => Move(index, -1),
                enabled: i > 0,
                name: "Move up");
            ui.Button(
                id: "settings.quality.down." + i,
                text: "↓",
                bounds: down,
                action: () => Move(index, 1),
                enabled: i < current.Qualities.Count - 1,
                name: "Move down");
            ui.Button(
                id: "settings.quality.remove." + i,
                text: "×",
                bounds: remove,
                action: () => current.Qualities.RemoveAt(index),
                name: "Remove quality");
            y += i + 1 == current.Qualities.Count ? SettingsPane.GroupGap : Gap;
        }

        var inputRow = Row(body, ref y);
        const float AddWidth = 45;
        var input = inputRow with { Width = inputRow.Width - AddWidth - Gap };
        var add = inputRow with { X = input.Right + Gap, Width = AddWidth };
        ui.Input(inputId, "Quality name, e.g. 480p", input, enter: Add);
        var value = ui.Value(inputId).Trim();
        var canAdd = value.Length != 0 && !current.Qualities.Contains(value, StringComparer.OrdinalIgnoreCase);
        ui.Button(id: "settings.quality.add", text: "Add", bounds: add, action: Add, enabled: canAdd);

        y += SettingsPane.GroupGap;
        ui.TextRow(text: "Fallback quality", body, ref y, spacing: Gap, weight: FontWeight.SemiBold);
        var fallback = Row(body, ref y, RadioHeight);
        var best = fallback with { Width = fallback.Width / 2 };
        var worst = best with { X = best.Right };
        ui.Radio(
            "settings.quality.best",
            "Best",
            current.FallbackQuality == FavoriteQualities.FallbackQualityOption.Best,
            best,
            _ => current.FallbackQuality = FavoriteQualities.FallbackQualityOption.Best,
            group: "settings.quality.fallback");
        ui.Radio(
            "settings.quality.worst",
            "Worst",
            current.FallbackQuality == FavoriteQualities.FallbackQualityOption.Worst,
            worst,
            _ => current.FallbackQuality = FavoriteQualities.FallbackQualityOption.Worst,
            group: "settings.quality.fallback");

        void Add()
        {
            var quality = ui.Value(inputId).Trim();
            if (quality.Length == 0 || current.Qualities.Contains(quality, StringComparer.OrdinalIgnoreCase)) return;

            current.Qualities.Insert(0, quality);
            ui.SetValue(inputId, "");
        }

        void Move(int index, int direction) =>
            (current.Qualities[index], current.Qualities[index + direction]) = (current.Qualities[index + direction], current.Qualities[index]);
    }

    private static RectangleF Row(RectangleF body, ref float y, float height = RowHeight)
    {
        var row = body with { Y = y, Height = height };
        y += height;
        return row;
    }
}
