using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Serialization;
using Livestream.Monitor.Core.UI;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.ApiClients;

namespace Livestream.Monitor.Core.Settings
{
    [JsonConverter(typeof(JsonStringEnumConverter<StreamNotificationImage>))]
    public enum StreamNotificationImage
    {
        ChannelIcon,
        LiveThumbnail,
    }

    [ExternalAPIs.IgnoreNullProperties(
        nameof(SettingsVersion),
        nameof(CheckForNewVersions),
        nameof(DisableRefreshErrorDialogs),
        nameof(DisableMinimizeToTrayNotification),
        nameof(MetroThemeBaseColour),
        nameof(MetroThemeAccentColour),
        nameof(MinimumEventViewers),
        nameof(DisableNotifications),
        nameof(StreamNotificationImage),
        nameof(UseLongStreamNotificationBanners),
        nameof(HideStreamOutputMessageBoxOnLoad),
        nameof(FavoriteApiQualities),
        nameof(ExcludeFromNotifying),
        nameof(ShowOnlineOnly),
        nameof(LivestreamListSortState))]
    public class Settings : ObservableModel
    {
        public const int CurrentSettingsVersion = 4;
        public const string UrlReplacementToken = "{url}";
        private const string QualitiesReplacementToken = "{qualities}";
        internal const string AllStreamQualitiesKey = "all";

        public const string DefaultChromeFullPath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
        public const string DefaultChromeArgs = "--app=" + UrlReplacementToken + " --window-size=350,760";

        public const string DefaultEdgePath = "msedge";

        public const int DefaultMinimumPopularEventViewers = 100_000;

        public int SettingsVersion
        {
            get;
            set => Set(ref field, value);
        }

        public bool CheckForNewVersions
        {
            get;
            set => Set(ref field, value);
        }

        public bool DebugMode
        {
            get;
            set => Set(ref field, value);
        }

        public bool DisableRefreshErrorDialogs
        {
            get;
            set => Set(ref field, value);
        }

        public bool DisableMinimizeToTrayNotification
        {
            get;
            set => Set(ref field, value);
        }

        [DefaultValue(MetroThemeBaseColour.BaseDark)]
        public MetroThemeBaseColour MetroThemeBaseColour
        {
            get;
            set => Set(ref field, value);
        } = MetroThemeBaseColour.BaseDark;

        [DefaultValue(MetroThemeAccentColour.Orange)]
        public MetroThemeAccentColour MetroThemeAccentColour
        {
            get;
            set => Set(ref field, value);
        } = MetroThemeAccentColour.Orange;

        /// <summary> Minimum event viewers before popular notifications occur, set to 0 to disable notifications </summary>
        [DefaultValue(DefaultMinimumPopularEventViewers)]
        public int MinimumEventViewers
        {
            get;
            set => Set(ref field, value);
        } = DefaultMinimumPopularEventViewers;

        public bool DisableNotifications
        {
            get;
            set => Set(ref field, value);
        }

        public StreamNotificationImage StreamNotificationImage
        {
            get;
            set => Set(ref field, value);
        }

        /// <summary>Requests the long duration for Direct2D stream notification banners.</summary>
        public bool UseLongStreamNotificationBanners
        {
            get;
            set => Set(ref field, value);
        }

        public bool HideStreamOutputMessageBoxOnLoad
        {
            get;
            set => Set(ref field, value);
        }

        [JsonInclude]
        public Dictionary<string, FavoriteQualities> FavoriteApiQualities { get; private set; } = new();

        /// <summary>
        /// Channel names in this collection should not raise notifications. <para/>
        /// We store these in settings so it can apply to both monitored and popular streams.
        /// </summary>
        [JsonInclude]
        [JsonConverter(typeof(ExcludeNotifyJsonConverter))]
        public ObservableList<UniqueStreamKey> ExcludeFromNotifying { get; private set; } = [];

        public bool ShowOnlineOnly
        {
            get;
            set => Set(ref field, value);
        }

        public DataGridSortState LivestreamListSortState
        {
            get;
            set => Set(ref field, value);
        }

        public TwitchSettings Twitch
        {
            get;
            set => Set(ref field, value);
        } = new();

        public KickSettings Kick
        {
            get;
            set => Set(ref field, value);
        } = new();

        public YouTubeSettings YouTube
        {
            get;
            set => Set(ref field, value);
        } = new();

        public FavoriteQualities GetStreamQualities(string apiName)
        {
            var qualities = GetSavedStreamQualities(apiName);
            if (qualities.IsConfigured) return qualities;

            return FavoriteApiQualities.ContainsKey(AllStreamQualitiesKey) ? GetSavedStreamQualities(AllStreamQualitiesKey) : qualities;
        }

        /// <summary> Reads saved preferences, using defaults for null values. Does not apply the All fallback or change saved settings. </summary>
        internal FavoriteQualities GetSavedStreamQualities(string apiName)
        {
            FavoriteApiQualities.TryGetValue(apiName, out var qualities);
            if (qualities == null) return new FavoriteQualities();
            if (qualities.Qualities != null && qualities.FallbackQuality != null) return qualities;

            return new FavoriteQualities
            {
                Qualities = qualities.Qualities ?? [],
                FallbackQuality = qualities.FallbackQuality ?? FavoriteQualities.FallbackQualityOption.Best,
            };
        }

        /// <summary>
        /// The placeholders a stream or VOD command's arguments may use: the platform's, such as the url, and the
        /// favourite qualities followed by the fallback quality.
        /// </summary>
        public Dictionary<string, string> GetLaunchReplacements(string apiName, string url)
        {
            var favorites = GetStreamQualities(apiName);
            var qualities = string.Join(",", favorites.Qualities.Union([favorites.FallbackQuality]));
            var replacements = GetPlatformSettings(apiName).GetReplacements(url).ToDictionary(x => x.Key, x => x.Value);
            replacements[QualitiesReplacementToken] = qualities;
            return replacements;
        }

        public ApiPlatformSettings GetPlatformSettings(string apiName)
        {
            return apiName switch
            {
                TwitchApiClient.API_NAME => Twitch,
                KickApiClient.API_NAME => Kick,
                YoutubeApiClient.API_NAME => YouTube,
                _ => throw new ArgumentException($"Unknown API platform: {apiName}"),
            };
        }
    }

    public class DataGridSortState
    {
        public string Column { get; set; }

        public ListSortDirection SortDirection { get; set; }
    }
}
