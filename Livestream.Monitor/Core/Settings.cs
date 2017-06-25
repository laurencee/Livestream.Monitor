using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
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
        public const string DEFAULT_CHROME_FULL_PATH = @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe";
        public const string DEFAULT_FIREFOX_FULL_PATH = @"C:\Program Files (x86)\Mozilla Firefox\firefox.exe";
        public const string DEFAULT_CHROME_COMMAND_LINE = DEFAULT_CHROME_FULL_PATH + " " + CHROME_ARGS;
        public const string DEFAULT_LIVESTREAMER_FULL_PATH = @"C:\Program Files (x86)\Livestreamer\livestreamer.exe";
        public const string DEFAULT_STREAMLINK_FULL_PATH = @"C:\Program Files (x86)\Streamlink\streamlink.exe";
        public const int DEFAULT_MINIMUM_EVENT_VIEWERS = 30000;

        public const string CHAT_URL_REPLACEMENT_TOKEN = "{url}";
        public const string CHROME_ARGS = "--app=" + CHAT_URL_REPLACEMENT_TOKEN + " --window-size=350 -height=760";
        public const string FIREFOX_ARGS = "-url " + CHAT_URL_REPLACEMENT_TOKEN;

        private MetroThemeBaseColour? metroThemeBaseColour;
        private MetroThemeAccentColour? metroThemeAccentColour;
        private StreamQuality defaultStreamQuality;
        private string livestreamerFullPath;
        private string chatCommandLine;
        private string chromeFullPath;
        private int minimumEventViewers = DEFAULT_MINIMUM_EVENT_VIEWERS;
        private bool disableNotifications, passthroughClientId;
        private bool hideStreamOutputMessageBoxOnLoad;
        private string twitchAuthToken;
        private bool twitchAuthTokenInLivestreamerConfig;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public MetroThemeBaseColour? MetroThemeBaseColour
        {
            get { return metroThemeBaseColour; }
            set
            {
                if (value == metroThemeBaseColour) return;
                metroThemeBaseColour = value;
                NotifyOfPropertyChange(() => MetroThemeBaseColour);
            }
        }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public MetroThemeAccentColour? MetroThemeAccentColour
        {
            get { return metroThemeAccentColour; }
            set
            {
                if (value == metroThemeAccentColour) return;
                metroThemeAccentColour = value;
                NotifyOfPropertyChange(() => MetroThemeAccentColour);
            }
        }

        [JsonProperty]
        public StreamQuality DefaultStreamQuality
        {
            get { return defaultStreamQuality; }
            set
            {
                if (value == defaultStreamQuality) return;
                defaultStreamQuality = value;
                NotifyOfPropertyChange(() => DefaultStreamQuality);
            }
        }

        [DefaultValue(DEFAULT_STREAMLINK_FULL_PATH)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public string LivestreamerFullPath
        {
            get { return livestreamerFullPath; }
            set
            {
                if (value == livestreamerFullPath) return;
                livestreamerFullPath = value;
                NotifyOfPropertyChange(() => LivestreamerFullPath);
            }
        }

        [DefaultValue(DEFAULT_CHROME_COMMAND_LINE)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public string ChatCommandLine
        {
            get { return chatCommandLine; }
            set
            {
                if (value == chatCommandLine) return;
                chatCommandLine = value;
                NotifyOfPropertyChange(() => ChatCommandLine);
            }
        }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        [Obsolete("Replaced by " + nameof(ChatCommandLine))]
        public string ChromeFullPath
        {
            get { return chromeFullPath; }
            set
            {
                if (value == chromeFullPath) return;
                chromeFullPath = value;
                NotifyOfPropertyChange(() => ChromeFullPath);
            }
        }

        [DefaultValue(DEFAULT_MINIMUM_EVENT_VIEWERS)]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int MinimumEventViewers
        {
            get { return minimumEventViewers; }
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
            get { return disableNotifications; }
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
            get { return hideStreamOutputMessageBoxOnLoad; }
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
            get { return passthroughClientId; }
            set
            {
                if (value == passthroughClientId) return;
                passthroughClientId = value;
                NotifyOfPropertyChange(() => PassthroughClientId);
            }
        }

        /// <summary>
        /// Channel names in this collection should not raise notifications. <para/>
        /// We store these in settings so it can apply to both monitored and popular streams.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(ExcludeNotifyConverter))]
        public ObservableCollection<UniqueStreamKey> ExcludeFromNotifying { get; } = new ObservableCollection<UniqueStreamKey>();

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool TwitchAuthTokenInLivestreamerConfig
        {
            get { return twitchAuthTokenInLivestreamerConfig; }
            set
            {
                if (value == twitchAuthTokenInLivestreamerConfig) return;
                twitchAuthTokenInLivestreamerConfig = value;
                NotifyOfPropertyChange(() => TwitchAuthTokenInLivestreamerConfig);
            }
        }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public string TwitchAuthToken
        {
            get { return twitchAuthToken; }
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
        public bool TwitchAuthTokenSet => TwitchAuthTokenInLivestreamerConfig ||
                                          !string.IsNullOrWhiteSpace(TwitchAuthToken);

        /// <summary>
        /// Name of the livestreamer/streamlink exe without the file extension
        /// </summary>
        public string LivestreamExeDisplayName => Path.GetFileNameWithoutExtension(LivestreamerFullPath);
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
            
            var exclusions = (ObservableCollection<UniqueStreamKey>) existingValue;
            
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonToken.EndArray:
                        return exclusions;
                    case JsonToken.StartObject:
                        var excludeNotify = serializer.Deserialize<UniqueStreamKey>(reader);
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
}