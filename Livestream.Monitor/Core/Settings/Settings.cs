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

        public const string DefaultFirefoxFullPath = @"C:\Program Files\Mozilla Firefox\firefox.exe";
        public const string DefaultFirefoxArgs = $"-url {UrlReplacementToken}";

        public const string DefaultEdgePath = "msedge";

        public const string DefaultLivestreamerFullPath = @"C:\Program Files (x86)\Livestreamer\livestreamer.exe";
        public const string DefaultStreamlinkFullPath = @"C:\Program Files\Streamlink\bin\streamlink.exe";
        public const string DefaultStreamlinkX86FullPath = @"C:\Program Files (x86)\Streamlink\bin\streamlink.exe";
        public const int DefaultMinimumPopularEventViewers = 50000;

        private MetroThemeBaseColour metroThemeBaseColour = MetroThemeBaseColour.BaseDark;
        private MetroThemeAccentColour metroThemeAccentColour = MetroThemeAccentColour.Orange;
        private int minimumEventViewers = DefaultMinimumPopularEventViewers;
        private string livestreamerFullPath;
        private bool disableNotifications, hideStreamOutputMessageBoxOnLoad, checkForNewVersions, disableRefreshErrorDialogs;
        private int settingsVersion;
        private DataGridSortState livestreamListSortState;
        private TwitchSettings twitch = new();
        private KickSettings kick = new();
        private YouTubeSettings youTube = new();
        private bool debugMode;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int SettingsVersion
        {
            get => settingsVersion;
            set => Set(ref settingsVersion, value);
        }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool CheckForNewVersions
        {
            get => checkForNewVersions;
            set => Set(ref checkForNewVersions, value);
        }

        [JsonProperty]
        public bool DebugMode
        {
            get => debugMode;
            set => Set(ref debugMode, value);
        }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool DisableRefreshErrorDialogs
        {
            get => disableRefreshErrorDialogs;
            set => Set(ref disableRefreshErrorDialogs, value);
        }

        [DefaultValue(MetroThemeBaseColour.BaseDark)]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public MetroThemeBaseColour MetroThemeBaseColour
        {
            get => metroThemeBaseColour;
            set => Set(ref metroThemeBaseColour, value);
        }

        [DefaultValue(MetroThemeAccentColour.Orange)]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public MetroThemeAccentColour MetroThemeAccentColour
        {
            get => metroThemeAccentColour;
            set => Set(ref metroThemeAccentColour, value);
        }

        [DefaultValue(DefaultStreamlinkFullPath)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public string LivestreamerFullPath
        {
            get => livestreamerFullPath;
            set => Set(ref livestreamerFullPath, value);
        }

        /// <summary> Minimum event viewers before popular notifications occur, set to 0 to disable notifications </summary>
        [DefaultValue(DefaultMinimumPopularEventViewers)]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int MinimumEventViewers
        {
            get => minimumEventViewers;
            set => Set(ref minimumEventViewers, value);
        }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool DisableNotifications
        {
            get => disableNotifications;
            set => Set(ref disableNotifications, value);
        }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool HideStreamOutputMessageBoxOnLoad
        {
            get => hideStreamOutputMessageBoxOnLoad;
            set => Set(ref hideStreamOutputMessageBoxOnLoad, value);
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
            get => livestreamListSortState;
            set => Set(ref livestreamListSortState, value);
        }



        [JsonProperty]
        public TwitchSettings Twitch
        {
            get => twitch;
            set => Set(ref twitch, value);
        }

        [JsonProperty]
        public KickSettings Kick
        {
            get => kick;
            set => Set(ref kick, value);
        }

        [JsonProperty]
        public YouTubeSettings YouTube
        {
            get => youTube;
            set => Set(ref youTube, value);
        }

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