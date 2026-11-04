using System.Collections.Generic;
using System.Linq;
using Livestream.Monitor.Model.ApiClients;

namespace Livestream.Monitor.Core.Settings;

internal sealed class StreamQualityDraft
{
    internal static readonly (string ApiName, string DisplayName)[] Platforms =
    [
        (Settings.AllStreamQualitiesKey, "All"),
        (TwitchApiClient.API_NAME, "Twitch"),
        (KickApiClient.API_NAME, "Kick"),
        (YoutubeApiClient.API_NAME, "YouTube"),
    ];

    private readonly Settings settings;
    private readonly Dictionary<string, FavoriteQualities> drafts = new();

    public StreamQualityDraft(Settings settings)
    {
        this.settings = settings;
        Revert();
    }

    public FavoriteQualities this[string apiName] => drafts[apiName];

    public bool HasChanges
    {
        get
        {
            foreach (var (apiName, draft) in drafts)
                if (!draft.Equals(GetSaved(apiName)))
                    return true;

            return false;
        }
    }

    public bool HasAllPreferences => drafts[Settings.AllStreamQualitiesKey].IsConfigured;

    public void SaveChanges()
    {
        foreach (var (apiName, draft) in drafts)
            if (!draft.Equals(GetSaved(apiName)))
                settings.FavoriteApiQualities[apiName] = Copy(draft);
    }

    public void Revert()
    {
        foreach (var (apiName, _) in Platforms) drafts[apiName] = Copy(GetSaved(apiName));
    }

    private FavoriteQualities GetSaved(string apiName) =>
        settings.GetSavedStreamQualities(apiName);

    // Copy the list so draft edits don't change saved settings.
    private static FavoriteQualities Copy(FavoriteQualities source) => new()
    {
        Qualities = source.Qualities.ToList(),
        FallbackQuality = source.FallbackQuality,
    };
}
