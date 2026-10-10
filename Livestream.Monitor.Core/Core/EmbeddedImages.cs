using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Livestream.Monitor.Core
{
    /// <summary>
    /// Images shared by the frontends, embedded in this assembly so frontends without WPF resources can load them.
    /// </summary>
    public static class EmbeddedImages
    {
        public const string Live = "Images.Live.png";
        public const string Offline = "Images.Offline.png";
        public const string TrayIcon = "Images.TrayIcon.ico";
        private const string CountryMappings = "Images.countryIdIsoMappings.json";

        /// <summary>
        /// Twitch broadcaster languages (ISO 639) mapped to the country codes of the flag images (ISO 3166-1 alpha-2).
        /// </summary>
        public static Dictionary<string, string> ReadCountryMappings()
        {
            using var stream = Open(CountryMappings);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
        }

        /// <summary>The name of the flag image of a country code (ISO 3166-1 alpha-2).</summary>
        public static string GetFlagName(string countryCode) =>
            $"Images.CountryFlags.{countryCode.ToLowerInvariant()}.png";

        /// <summary>Opens an embedded image, or returns null when there is no image of that name.</summary>
        public static Stream Open(string name) => typeof(EmbeddedImages).Assembly.GetManifestResourceStream(name);
    }
}
