using System;
using System.Linq;
using Livestream.Monitor.Core;
using Xunit;

namespace Livestream.Monitor.Tests;

public class EmbeddedImagesShould
{
    [Fact]
    public void StoreTheFlagAtlasAsAnIndexedPng()
    {
        using var stream = EmbeddedImages.Open(EmbeddedImages.FlagAtlas);

        // IHDR stores the bit depth and color type consecutively after the dimensions.
        const int BitDepthOffset = 24;
        const int IndexedColorType = 3;
        stream.Position = BitDepthOffset;

        Assert.Equal(8, stream.ReadByte());
        Assert.Equal(IndexedColorType, stream.ReadByte());
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
    public void ReturnFlagRegionsAtTheirOriginalSize(string country, int width, int height)
    {
        var flag = EmbeddedImages.FindFlag(country);

        Assert.NotNull(flag);
        Assert.Equal(width, flag.Value.Width);
        Assert.Equal(height, flag.Value.Height);
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
