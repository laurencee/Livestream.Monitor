using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Caliburn.Micro;
using Livestream.Monitor.Core.UI;
using Livestream.Monitor.Model.ApiClients;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Livestream.Monitor.Core
{
    public class Settings : PropertyChangedBase
    {
        public const string DEFAULT_CHROME_FULL_PATH = @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe";
        public const string DEFAULT_LIVESTREAMER_FULL_PATH = @"C:\Program Files (x86)\Livestreamer\livestreamer.exe";
        public const int DEFAULT_MINIMUM_EVENT_VIEWERS = 30000;

        private MetroThemeBaseColour? metroThemeBaseColour;
        private MetroThemeAccentColour? metroThemeAccentColour;
        private StreamQuality defaultStreamQuality;
        private string livestreamerFullPath;
        private string chromeFullPath;
        private int minimumEventViewers = DEFAULT_MINIMUM_EVENT_VIEWERS;
        private bool disableNotifications;

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

        [DefaultValue(DEFAULT_LIVESTREAMER_FULL_PATH)]
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

        [DefaultValue(DEFAULT_CHROME_FULL_PATH)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
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
        
        /// <summary>
        /// Channel names in this collection should not raise notifications. <para/>
        /// We store these in settings so it can apply to both monitored and popular streams.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(ExcludeNotifyConverter))]
        public ObservableCollection<ExcludeNotify> ExcludeFromNotifying { get; } = new ObservableCollection<ExcludeNotify>();
    }

    /// <summary>
    /// Migrates from the old array of streamids format to the new format using <see cref="ExcludeNotify"/> type
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
            
            var exclusions = (ObservableCollection<ExcludeNotify>) existingValue;
            
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonToken.EndArray:
                        return exclusions;
                    case JsonToken.StartObject:
                        var excludeNotify = serializer.Deserialize<ExcludeNotify>(reader);
                        exclusions.Add(excludeNotify);
                        break;
                    default: // convert old array of stream ids
                        var streamId = reader.Value.ToString();
                        SaveRequired = true; // if we ran conversions then we should save the new output file
                        exclusions.Add(new ExcludeNotify(TwitchApiClient.API_NAME, streamId));
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

    public class ExcludeNotify
    {
        public ExcludeNotify(string apiClientName, string streamId)
        {
            if (String.IsNullOrWhiteSpace(apiClientName))
                throw new ArgumentException("Argument is null or whitespace", nameof(apiClientName));
            if (String.IsNullOrWhiteSpace(streamId))
                throw new ArgumentException("Argument is null or whitespace", nameof(streamId));

            ApiClientName = apiClientName;
            StreamId = streamId;
        }

        public string ApiClientName { get; set; }

        public string StreamId { get; set; }

        public override string ToString() => $"{ApiClientName}:{StreamId}";

        #region equality members

        protected bool Equals(ExcludeNotify other)
        {
            return string.Equals(ApiClientName, other.ApiClientName) && string.Equals(StreamId, other.StreamId);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((ExcludeNotify) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((ApiClientName?.GetHashCode() ?? 0) * 397) ^ (StreamId?.GetHashCode() ?? 0);
            }
        }

        #endregion
    }
}