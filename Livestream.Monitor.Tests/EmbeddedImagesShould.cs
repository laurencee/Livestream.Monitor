using System;
using System.Globalization;
using System.Linq;
using System.Windows.Media.Imaging;
using Livestream.Monitor.Core;
using Livestream.Monitor.WPF.Core.UI;
using Xunit;

namespace Livestream.Monitor.Tests;

public class EmbeddedImagesShould
{
    [Theory]
    [InlineData(EmbeddedImages.Live)]
    [InlineData(EmbeddedImages.Offline)]
    [InlineData(EmbeddedImages.TrayIcon)]
    [InlineData(EmbeddedImages.FlagAtlas)]
    [InlineData(EmbeddedImages.GuernseyFlag)]
    public void ProvideTheSharedImages(string name)
    {
        using var stream = EmbeddedImages.Open(name);

        Assert.NotNull(stream);
        Assert.True(stream.Length > 0);
    }

    [Theory]
    [InlineData("ar", "SA")]
    [InlineData("zh-HK", "CN")]
    [InlineData("us", "US")]
    [InlineData("Scotland", "SCOTLAND")]
    [InlineData("AU", "HM")]
    [InlineData("BV", "SJ")]
    [InlineData("FR", "GF")]
    public void ResolveLanguageAliasesAndCountryFlags(string language, string country)
    {
        var flag = EmbeddedImages.FindFlag(language);

        Assert.NotNull(flag);
        Assert.Equal(EmbeddedImages.FindFlag(country), flag);
    }

    [Fact]
    public void PreferExactLanguageAliasesOverCountryCodes()
    {
        var argentina = EmbeddedImages.FindFlag("AR");

        Assert.NotNull(argentina);
        Assert.NotEqual(argentina, EmbeddedImages.FindFlag("ar"));
        Assert.Null(EmbeddedImages.FindFlag("zh-hk"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("zz")]
    [InlineData("gv")]
    public void ReturnNoFlagForUnknownInputs(string language)
    {
        Assert.Null(EmbeddedImages.FindFlag(language));
    }

    [Theory]
    [InlineData("CH", 11, 11)]
    [InlineData("ME", 16, 12)]
    [InlineData("NP", 9, 11)]
    public void ShowFlagsAtTheirOriginalSize(string country, int width, int height)
    {
        var converter = new CountryIdToFlagImageSourceConverter();
        var result = converter.Convert(country, typeof(BitmapSource), null, CultureInfo.InvariantCulture);
        var flag = Assert.IsAssignableFrom<BitmapSource>(result);

        Assert.Equal(width, flag.PixelWidth);
        Assert.Equal(height, flag.PixelHeight);
        Assert.True(flag.IsFrozen);
    }

    [Fact]
    public void EmbedOnlyGuernseyAsAnIndividualFlag()
    {
        var resources = typeof(EmbeddedImages).Assembly.GetManifestResourceNames();
        var flags = resources.Where(name => name.StartsWith("Images.CountryFlags.", StringComparison.Ordinal));

        Assert.Equal(EmbeddedImages.GuernseyFlag, Assert.Single(flags));
        Assert.True(EmbeddedImages.FindFlag("gg")?.IsStandalone);
    }
}
