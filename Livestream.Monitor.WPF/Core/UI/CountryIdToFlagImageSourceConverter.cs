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
using Livestream.Monitor.Core;

namespace Livestream.Monitor.WPF.Core.UI
{
    /// <summary>Provides an image source for the flag of a broadcaster language or country code.</summary>
    /// <remarks>Flag images are provided by FamFamFam at http://www.famfamfam.com/lab/icons/flags/.</remarks>
    [ValueConversion(typeof(string), typeof(BitmapSource))]
    public sealed class CountryIdToFlagImageSourceConverter : IValueConverter
    {
        private readonly Dictionary<FlagRegion, BitmapSource> flags = new();
        private BitmapSource atlas;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (EmbeddedImages.FindFlag(value as string) is not { } region) return null;
            if (flags.TryGetValue(region, out var flag)) return flag;

            flag = region.IsStandalone ? EmbeddedImageSources.Load(EmbeddedImages.GuernseyFlag) : CropFlag(region);
            flags.Add(region, flag);
            return flag;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private CroppedBitmap CropFlag(FlagRegion region)
        {
            atlas ??= EmbeddedImageSources.Load(EmbeddedImages.FlagAtlas);
            var bounds = new Int32Rect(region.X, region.Y, region.Width, region.Height);
            var flag = new CroppedBitmap(atlas, bounds);
            flag.Freeze();
            return flag;
        }
    }
}
