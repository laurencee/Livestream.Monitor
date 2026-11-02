using Livestream.Monitor.Core.UI;
using Xunit;

namespace Livestream.Monitor.Tests;

public class PlatformNamesShould
{
    [Theory]
    [InlineData("twitchtv", "Twitch")]
    [InlineData("kick", "Kick")]
    [InlineData("youtube", "YouTube")]
    [InlineData("All", "All")]
    [InlineData("custom-platform", "custom-platform")]
    public void MapStoredIdentifiersToDisplayNames(string identifier, string displayName)
    {
        Assert.Equal(displayName, PlatformNames.GetDisplayName(identifier));
    }
}
