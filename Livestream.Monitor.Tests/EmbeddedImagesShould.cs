using Livestream.Monitor.Core;
using Xunit;

namespace Livestream.Monitor.Tests;

public class EmbeddedImagesShould
{
    [Theory]
    [InlineData(EmbeddedImages.Live)]
    [InlineData(EmbeddedImages.Offline)]
    [InlineData(EmbeddedImages.TrayIcon)]
    public void ProvideTheSharedImages(string name)
    {
        using var stream = EmbeddedImages.Open(name);

        Assert.NotNull(stream);
        Assert.True(stream.Length > 0);
    }

    [Fact]
    public void ProvideTheFlagOfAMappedBroadcasterLanguage()
    {
        var country = EmbeddedImages.ReadCountryMappings()["ar"];

        using var stream = EmbeddedImages.Open(EmbeddedImages.GetFlagName(country));

        Assert.NotNull(stream);
    }
}
