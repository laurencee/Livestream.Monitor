using System.ComponentModel;
using Caliburn.Micro;
using Livestream.Monitor.Core.UI;
using Newtonsoft.Json;

namespace Livestream.Monitor.Core
{
    public class Settings : PropertyChangedBase
    {
        public const string DEFAULT_CHROME_FULL_PATH = @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe";
        public const string DEFAULT_LIVESTREAMER_FULL_PATH = @"C:\Program Files (x86)\Livestreamer\livestreamer.exe";

        private MetroThemeBaseColour? metroThemeBaseColour;
        private MetroThemeAccentColour? metroThemeAccentColour;
        private StreamQuality defaultStreamQuality;
        private string livestreamerFullPath;
        private string chromeFullPath;
        private int minimumEventViewers;

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

        [DefaultValue(30000)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
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
    }
}