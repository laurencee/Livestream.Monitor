using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Livestream.Monitor.Direct2D.Windows.Monitor;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.Model.Monitoring;
using NSubstitute;
using Xunit;

namespace Livestream.Monitor.Tests.Direct2D;

public class MonitorWindowShould
{
    [Theory]
    [InlineData(true, true, "long")]
    [InlineData(true, false, "short")]
    [InlineData(false, true, "long")]
    [InlineData(false, false, "short")]
    public void KeepToastContentAndClickTargetAtEitherBannerDuration(bool withThumbnail, bool longBanner, string expectedDuration)
    {
        var target = new StreamNotificationTarget(new UniqueStreamKey("twitchtv", "stream"), "channel", Followed: false);
        var thumbnail = withThumbnail ? new Uri(@"C:\Thumbnails\Stream & preview.jpg") : null;
        const string Title = "[POPULAR 42 Viewers]\nStreamer & Co";
        const string Description = "Playing <a game>";

        var toast = StreamNotifications.BuildToast(Title, Description, target, thumbnail, longBanner);
        var parsed = XElement.Parse(toast.ToString(SaveOptions.DisableFormatting));
        var visual = Assert.IsType<XElement>(parsed.Element("visual"));
        var binding = Assert.IsType<XElement>(visual.Element("binding"));
        string[] expected = [Title, Description];
        Assert.Equal(expected, binding.Elements("text").Select(text => text.Value));
        Assert.Equal(expectedDuration, (string)parsed.Attribute("duration"));
        Assert.True(StreamNotificationTarget.TryDecode((string)parsed.Attribute("launch"), out var decoded));
        Assert.Equal(target, decoded);

        var image = binding.Element("image");
        if (withThumbnail)
        {
            var thumbnailImage = Assert.IsType<XElement>(image);
            Assert.Equal("appLogoOverride", (string)thumbnailImage.Attribute("placement"));
            Assert.Equal(Assert.IsType<Uri>(thumbnail).AbsoluteUri, (string)thumbnailImage.Attribute("src"));
        }
        else
        {
            Assert.Null(image);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PreserveNotificationDestinationAndStreamIdentity(bool followed)
    {
        var first = new StreamNotificationTarget(new UniqueStreamKey("youtube", "video|1 & ✓"), "channel", followed);
        var second = new StreamNotificationTarget(new UniqueStreamKey("twitchtv", "video|1 & ✓"), "channel", followed);

        Assert.True(StreamNotificationTarget.TryDecode(first.Encode(), out var firstTarget));
        Assert.True(StreamNotificationTarget.TryDecode(second.Encode(), out var secondTarget));
        Assert.Equal(first, firstTarget);
        Assert.Equal(second, secondTarget);
        Assert.NotEqual(firstTarget.Key, secondTarget.Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2|followed|youtube|video|channel")]
    [InlineData("1|run|youtube|video|channel")]
    [InlineData("1|top||video|channel")]
    [InlineData("1|followed|youtube||channel")]
    [InlineData("1|followed|youtube|bad%escape|channel")]
    [InlineData("1|followed|youtube|video|channel|extra")]
    public void RejectInvalidNotificationTargets(string arguments)
    {
        Assert.False(StreamNotificationTarget.TryDecode(arguments, out _));
    }

    [Fact]
    public void SelectTheNotifiedVideoOrItsOfflineChannel()
    {
        var provider = Substitute.For<IApiClient>();
        provider.ApiName.Returns("youtube");
        var channel = new ChannelIdentifier(provider, "channel");
        var otherChannel = new ChannelIdentifier(provider, "other");
        var offline = new LivestreamModel("offline", channel);
        var otherOffline = new LivestreamModel("offline", otherChannel);
        var video = new LivestreamModel("video", channel) { Live = true };
        var otherVideo = new LivestreamModel("other video", channel) { Live = true };
        var target = new StreamNotificationTarget(video.UniqueStreamKey, channel.ChannelId, Followed: true);

        Assert.Equal(2, target.FindFollowedRow([otherOffline, otherVideo, video]));
        Assert.Equal(1, target.FindFollowedRow([otherOffline, offline]));
        Assert.Equal(-1, target.FindFollowedRow([otherOffline, otherVideo]));
    }

    [Fact]
    public void HandOffDistinctNotificationTargetsToTheRunningInstance()
    {
        var received = new ConcurrentQueue<StreamNotificationTarget>();
        var pipeName = $"Livestream.Monitor.Direct2D.Test.{Guid.NewGuid():N}";
        using var primary = new ToastActivationPipe((target, _) =>
        {
            received.Enqueue(target);
            return Task.FromResult(true);
        }, pipeName);
        var first = new StreamNotificationTarget(new UniqueStreamKey("youtube", "video"), "channel", Followed: true);
        var second = new StreamNotificationTarget(new UniqueStreamKey("twitchtv", "stream"), "stream", Followed: false);

        ToastActivationPipe.Forward(first, pipeName);
        ToastActivationPipe.Forward(second, pipeName);

        Assert.Equal(new[] { first, second }, received.ToArray());
    }

    [Fact]
    public void ReportWhenThePrimaryCannotAcceptANotificationTarget()
    {
        var pipeName = $"Livestream.Monitor.Direct2D.Test.{Guid.NewGuid():N}";
        using var primary = new ToastActivationPipe((_, _) => Task.FromResult(false), pipeName);
        var target = new StreamNotificationTarget(new UniqueStreamKey("youtube", "video"), "channel", Followed: true);

        Assert.ThrowsAny<IOException>(() => ToastActivationPipe.Forward(target, pipeName));
    }

    [Theory]
    [InlineData(20, 300)]
    [InlineData(250, 0)]
    [InlineData(600, 125)]
    public void KeepKeyboardSelectionVisibleAfterWheelScrolling(int selectedTop, int offset)
    {
        const int ItemHeight = 120;
        const int Viewport = 250;
        var selectedBottom = selectedTop + ItemHeight;
        var next = MonitorWindow.GetVisibleOffset(selectedTop, selectedBottom, offset, Viewport);
        Assert.InRange(selectedTop, next, next + Viewport - ItemHeight);
    }

    [Fact]
    public void KeepTheViewportStationaryWhenSelectionIsAlreadyVisible()
    {
        const int SelectedTop = 125;
        const int SelectedBottom = SelectedTop + 120;
        const int Offset = 100;
        const int Viewport = 250;
        var next = MonitorWindow.GetVisibleOffset(SelectedTop, SelectedBottom, Offset, Viewport);
        Assert.Equal(Offset, next);
    }
}
