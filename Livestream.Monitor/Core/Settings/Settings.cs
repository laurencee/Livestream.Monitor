using System;
using System.Collections.Generic;
using System.ComponentModel;
using Caliburn.Micro;
using Livestream.Monitor.Core.UI;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.ApiClients;
using Newtonsoft.Json;

namespace Livestream.Monitor.Core
{
    public class Settings : PropertyChangedBase
    {
        public const int CurrentSettingsVersion = 4;
        public const string UrlReplacementToken = "{url}";

        public const string DefaultChromeFullPath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
        public const string DefaultChromeArgs = "--app=" + UrlReplacementToken + " --window-size=350,760";

        public const string DefaultEdgePath = "msedge";

        public const int DefaultMinimumPopularEventViewers = 50000;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int SettingsVersion
        {
            get;
            set => Set(ref field, value);
        }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool CheckForNewVersions
        {
            get;
            set => Set(ref field, value);
        }

        [JsonProperty]
        public bool DebugMode
        {
            get;
            set => Set(ref field, value);
        }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool DisableRefreshErrorDialogs
        {
            get;
            set => Set(ref field, value);
        }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool DisableMinimizeToTrayNotification
        {
            get;
            set => Set(ref field, value);
        }

        [DefaultValue(MetroThemeBaseColour.BaseDark)]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public MetroThemeBaseColour MetroThemeBaseColour
        {
            get;
            set => Set(ref field, value);
        } = MetroThemeBaseColour.BaseDark;

        [DefaultValue(MetroThemeAccentColour.Orange)]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public MetroThemeAccentColour MetroThemeAccentColour
        {
            get;
            set => Set(ref field, value);
        } = MetroThemeAccentColour.Orange;

        /// <summary> Minimum event viewers before popular notifications occur, set to 0 to disable notifications </summary>
        [DefaultValue(DefaultMinimumPopularEventViewers)]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int MinimumEventViewers
        {
            get;
            set => Set(ref field, value);
        } = DefaultMinimumPopularEventViewers;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool DisableNotifications
        {
            get;
            set => Set(ref field, value);
        }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool HideStreamOutputMessageBoxOnLoad
        {
            get;
            set => Set(ref field, value);
        }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, FavoriteQualities> FavoriteApiQualities { get; } = new();

        /// <summary>
        /// Channel names in this collection should not raise notifications. <para/>
        /// We store these in settings so it can apply to both monitored and popular streams.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(ExcludeNotifyJsonConverter))]
        public BindableCollection<UniqueStreamKey> ExcludeFromNotifying { get; } = [];

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public DataGridSortState LivestreamListSortState
        {
            get;
            set => Set(ref field, value);
        }

        [JsonProperty]
        public TwitchSettings Twitch
        {
            get;
            set => Set(ref field, value);
        } = new();

        [JsonProperty]
        public KickSettings Kick
        {
            get;
            set => Set(ref field, value);
        } = new();

        [JsonProperty]
        public YouTubeSettings YouTube
        {
            get;
            set => Set(ref field, value);
        } = new();

        public FavoriteQualities GetStreamQualities(string apiName)
        {
            FavoriteApiQualities.TryGetValue(apiName, out var qualities);
            return qualities ?? new FavoriteQualities();
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