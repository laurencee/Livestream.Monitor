/*
 * Created by Drew Noakes
 * 20 May 2009
 * http://drewnoakes.com
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using Livestream.Monitor.Properties;
using Newtonsoft.Json;

namespace Livestream.Monitor.Core.UI
{
    /// <summary>
    /// Provides an image source for the flag of a country, as specified via the country's two letter ISO code.
    /// </summary>
    /// <remarks>
    /// Flag images used by this converter have been provided by FamFamFam and are publicaly available at:
    /// http://www.famfamfam.com/lab/icons/flags/
    /// </remarks>
    [ValueConversion(typeof(string), typeof(BitmapImage))]
    public sealed class CountryIdToFlagImageSourceConverter : IValueConverter
    {
        static CountryIdToFlagImageSourceConverter()
        {
            try
            {
                var deserializeObject = JsonConvert.DeserializeObject<Dictionary<string, string>>(Resources.countryIdIsoMappings);
                Iso639To3166 = deserializeObject;

                // Rerun this code to generate the iso mappings, they will then be imported into the resources for this project so they can be read out quickly
                //GenerateIsoConversionData();
                //File.WriteAllText("countryIdIsoMappings.json", JsonConvert.SerializeObject(Iso639to3166));
            }
            catch { /* dont do anything */ }
        }

        /// <summary>
        /// A mapping of the language iso format provided by twitch (ISO 639) converted into the iso format of the country flag images on disk (ISO-3166-alpha 2).
        /// </summary>
        private static readonly Dictionary<string, string> Iso639To3166 = new Dictionary<string, string>();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var countryId = value as string;

            if (countryId == null)
                return null;

            // convert the twitch ISO 639 language code into a country flag ISO 3166-alpha 2 code
            if (Iso639To3166.ContainsKey(countryId))
                countryId = Iso639To3166[countryId];

            try
            {
                var path = $"/Livestream.Monitor;component/Images/CountryFlags/{countryId.ToLower()}.png";
                var uri = new Uri(path, UriKind.Relative);
                var resourceStream = Application.GetResourceStream(uri);
                if (resourceStream == null)
                    return null;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = resourceStream.Stream;
                bitmap.EndInit();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        // Just use 1 time to generate the list of language mappings
        private static void GenerateIsoConversionData()
        {
            CultureInfo[] cinfo = CultureInfo.GetCultures(CultureTypes.AllCultures & ~CultureTypes.NeutralCultures);
            foreach (CultureInfo cul in cinfo)
            {
                // don't overwrite keys, just use the first one found
                if (Iso639To3166.ContainsKey(cul.TwoLetterISOLanguageName)) continue;
                // need a special avoidence for english since this doesn't convert into a useful iso code
                if (cul.TwoLetterISOLanguageName == "en") continue;

                RegionInfo ri = null;
                try
                {
                    ri = new RegionInfo(cul.Name);
                    if (ri.TwoLetterISORegionName.Length != 2) continue;
                    Iso639To3166[cul.TwoLetterISOLanguageName] = ri.TwoLetterISORegionName.ToLower();
                }
                catch
                {
                    // can't do anything 
                }
            }

            // custom chinese mapping - unsure why twitch switches between ietf tags and ISO 639 2 character language codes
            Iso639To3166.Add("zh-HK", "cn");
        }
    }
}
