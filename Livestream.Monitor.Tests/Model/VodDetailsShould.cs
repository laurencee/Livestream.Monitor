using System;
using System.IO;
using Livestream.Monitor.Model;
using Xunit;

namespace Livestream.Monitor.Tests.Model;

public class VodDetailsShould
{
    [Theory]
    [InlineData("First\nSecond", "First\nSecond")]
    [InlineData("First\r\n\r\nSecond", "First\n\nSecond")]
    [InlineData("First\r\n\t\r\n \nSecond", "First\n\nSecond")]
    [InlineData("\nFirst\n\n\n\nSecond\n", "First\n\nSecond")]
    [InlineData("First\rSecond\u2028Third", "First\nSecond\nThird")]
    [InlineData("\r\n\n ", "")]
    public void PreserveDescriptionParagraphsWithAtMostOneBlankLine(string description, string expected)
    {
        Assert.Equal(expected, VodDetails.NormalizeDescription(description));
    }

    [Theory]
    [InlineData("https://www.twitch.tv/videos/1")]
    [InlineData("http://example.com/watch?v=1&quality=source")]
    public void ProvideAWebUriForTheBrowser(string url)
    {
        var vod = new VodDetails { Url = url };

        Assert.Equal(new Uri(url), vod.GetBrowserUri());
        Assert.Equal(url, vod.Url);
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("C:\\Windows\\System32\\calc.exe")]
    [InlineData("\\\\server\\share\\video.exe")]
    [InlineData("custom-player:video")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com/video")]
    [InlineData("/videos/1")]
    [InlineData("not a URL")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectNonWebUrlsBeforeOpeningTheBrowser(string url)
    {
        var vod = new VodDetails { Url = url };

        Assert.Throws<InvalidDataException>(vod.GetBrowserUri);
    }
}
