using System.Collections.Generic;
using Livestream.Monitor.Model;
using Xunit;

namespace Livestream.Monitor.Tests.Model;

public class UniqueStreamKeyShould
{
    [Theory]
    [InlineData("twitchtv", "TWITCHTV")]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void FindEquivalentKeysRegardlessOfCase(string apiName, string equivalentApiName)
    {
        var key = new UniqueStreamKey(apiName, "channel");
        var equivalent = new UniqueStreamKey(equivalentApiName, "CHANNEL");
        var streams = new Dictionary<UniqueStreamKey, string> { [key] = "stream" };

        Assert.Equal(key, equivalent);
        Assert.True(streams.TryGetValue(equivalent, out var stream));
        Assert.Equal("stream", stream);
    }
}
