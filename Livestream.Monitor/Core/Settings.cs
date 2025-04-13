using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Caliburn.Micro;
using Livestream.Monitor.Core.UI;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.ApiClients;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Livestream.Monitor.Core
{
    public class Settings : PropertyChangedBase
    {
        public const int CurrentSettingsVersion = 3;
        public const string ChatUrlReplacementToken = "{url}";

        public const string DefaultChromeFullPath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
        public const string ChromeArgs = "--app=" + ChatUrlReplacementToken + " --window-size=350,760";
        public const string DefaultChromeCommand = $"\"{DefaultChromeFullPath}\" {ChromeArgs}";

        public const string DefaultFirefoxFullPath = @"C:\Program Files\Mozilla Firefox\firefox.exe";
        public const string FirefoxArgs = $"-url {ChatUrlReplacementToken}";

        public const string DefaultEdgeChatCommand = $"start microsoft-edge:{ChatUrlReplacementToken}";

        public const string DefaultLivestreamerFullPath = @"C:\Program Files (x86)\Livestreamer\livestreamer.exe";
        public const string DefaultStreamlinkFullPath = @"C:\Program Files\Streamlink\bin\streamlink.exe";
        public const string DefaultStreamlinkX86FullPath = @"C:\Program Files (x86)\Streamlink\bin\streamlink.exe";
        public const int DefaultMinimumPopularEventViewers = 50000;

        private MetroThemeBaseColour metroThemeBaseColour = MetroThemeBaseColour.BaseDark;
        private MetroThemeAccentColour metroThemeAccentColour = MetroThemeAccentColour.Orange;
        private int minimumEventViewers = DefaultMinimumPopularEventViewers;
        private string livestreamerFullPath, chatCommandLine, twitchAuthToken;
        private bool disableNotifications, passthroughClientId, hideStreamOutputMessageBoxOnLoad, checkForNewVersions, disableRefreshErrorDialogs;
        private int settingsVersion;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int SettingsVersion
        {
            get => settingsVersion;
            set
            {
                if (value == settingsVersion) return;
                settingsVersion = value;
                NotifyOfPropertyChange(() => SettingsVersion);
            }
        }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool CheckForNewVersions
        {
            get => checkForNewVersions;
            set
            {
                if (value == checkForNewVersions) return;
                checkForNewVersions = value;
                NotifyOfPropertyChange(() => SettingsVersion);
            }
        }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool DisableRefreshErrorDialogs
        {
            get => disableRefreshErrorDialogs;
            set
            {
                if (value == disableRefreshErrorDialogs) return;
                disableRefreshErrorDialogs = value;
                NotifyOfPropertyChange(() => DisableRefreshErrorDialogs);
            }
        }

        [DefaultValue(MetroThemeBaseColour.BaseDark)]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public MetroThemeBaseColour MetroThemeBaseColour
        {
            get => metroThemeBaseColour;
            set
            {
                if (value == metroThemeBaseColour) return;
                metroThemeBaseColour = value;
                NotifyOfPropertyChange(() => MetroThemeBaseColour);
            }
        }

        [DefaultValue(MetroThemeAccentColour.Orange)]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public MetroThemeAccentColour MetroThemeAccentColour
        {
            get => metroThemeAccentColour;
            set
            {
                if (value == metroThemeAccentColour) return;
                metroThemeAccentColour = value;
                NotifyOfPropertyChange(() => MetroThemeAccentColour);
            }
        }

        [DefaultValue(DefaultStreamlinkFullPath)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public string LivestreamerFullPath
        {
            get => livestreamerFullPath;
            set
            {
                if (value == livestreamerFullPath) return;
                livestreamerFullPath = value;
                NotifyOfPropertyChange(() => LivestreamerFullPath);
            }
        }

        [DefaultValue(DefaultChromeCommand)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public string ChatCommandLine
        {
            get => chatCommandLine;
            set
            {
                if (value == chatCommandLine) return;
                chatCommandLine = value;
                NotifyOfPropertyChange(() => ChatCommandLine);
            }
        }

        /// <summary> Minimum event viewers before popular notifications occur, set to 0 to disable notifications </summary>
        [DefaultValue(DefaultMinimumPopularEventViewers)]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int MinimumEventViewers
        {
            get => minimumEventViewers;
            set
            {
                if (value == minimumEventViewers) return;
                minimumEventViewers = value;
                NotifyOfPropertyChange(() => MinimumEventViewers);
            }
        }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool DisableNotifications
        {
            get => disableNotifications;
            set
            {
                if (value == disableNotifications) return;
                disableNotifications = value;
                NotifyOfPropertyChange(() => DisableNotifications);
            }
        }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool HideStreamOutputMessageBoxOnLoad
        {
            get => hideStreamOutputMessageBoxOnLoad;
            set
            {
                if (value == hideStreamOutputMessageBoxOnLoad) return;
                hideStreamOutputMessageBoxOnLoad = value;
                NotifyOfPropertyChange(() => HideStreamOutputMessageBoxOnLoad);
            }
        }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool PassthroughClientId
        {
            get => passthroughClientId;
            set
            {
                if (value == passthroughClientId) return;
                passthroughClientId = value;
                NotifyOfPropertyChange(() => PassthroughClientId);
            }
        }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, FavoriteQualities> FavoriteApiQualities { get; } =
            new Dictionary<string, FavoriteQualities>();


        /// <summary>
        /// Channel names in this collection should not raise notifications. <para/>
        /// We store these in settings so it can apply to both monitored and popular streams.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(ExcludeNotifyConverter))]
        public BindableCollection<UniqueStreamKey> ExcludeFromNotifying { get; } = new BindableCollection<UniqueStreamKey>();

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public string TwitchAuthToken
        {
            get => twitchAuthToken;
            set
            {
                if (value == twitchAuthToken) return;
                twitchAuthToken = value;
                NotifyOfPropertyChange(() => TwitchAuthToken);
            }
        }

        /// <summary>
        /// Flag to indicate if the twitch oauth token has been defined either in livestream monitor settings
        /// or in the livestreamer/streamlink configuration file
        /// </summary>
        public bool TwitchAuthTokenSet => !string.IsNullOrWhiteSpace(TwitchAuthToken);

        /// <summary>
        /// Name of the livestreamer/streamlink exe without the file extension
        /// </summary>
        public string LivestreamExeDisplayName => Path.GetFileNameWithoutExtension(LivestreamerFullPath);

        public FavoriteQualities GetStreamQualities(string apiName)
        {
            FavoriteApiQualities.TryGetValue(apiName, out var qualities);
            return qualities ?? new FavoriteQualities();
        }
    }

    /// <summary>
    /// Migrates from the old array of streamids format to the new format using <see cref="UniqueStreamKey"/> type
    /// </summary>
    public class ExcludeNotifyConverter : JsonConverter
    {
        public static bool SaveRequired;

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var jsonObj = JArray.FromObject(value);
            jsonObj.WriteTo(writer);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;

            var exclusions = (ObservableCollection<UniqueStreamKey>)existingValue;

            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonToken.EndArray:
                        return exclusions;
                    case JsonToken.StartObject:
                        var excludeNotify = serializer.Deserialize<UniqueStreamKey>(reader); // could have null property values
                        exclusions.Add(excludeNotify);
                        break;
                    default: // convert old array of stream ids
                        var streamId = reader.Value.ToString();
                        SaveRequired = true; // if we ran conversions then we should save the new output file
                        exclusions.Add(new UniqueStreamKey(TwitchApiClient.API_NAME, streamId));
                        break;
                }
            }

            return exclusions;
        }

        public override bool CanConvert(Type objectType)
        {
            return true;
        }
    }

    public class FavoriteQualities
    {
        public static class FallbackQualityOption
        {
            public const string Worst = "Worst";
            public const string Best = "Best";
        }

        public List<string> Qualities { get; set; } = new List<string>();

        public string FallbackQuality { get; set; } = FallbackQualityOption.Best;

        public override bool Equals(object obj)
        {
            var qualities = obj as FavoriteQualities;
            return qualities != null &&
                   Qualities.SequenceEqual(qualities.Qualities) &&
                   FallbackQuality == qualities.FallbackQuality;
        }

        public override int GetHashCode()
        {
            var hashCode = 643755790;
            hashCode = hashCode * -1521134295 + EqualityComparer<List<string>>.Default.GetHashCode(Qualities);
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(FallbackQuality);
            return hashCode;
        }
    }
}