using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Direct2D;
using Livestream.Monitor.Direct2D.Native;
using Livestream.Monitor.Direct2D.Windows.Output;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.Model.Monitoring;
using Livestream.Monitor.Tests.Ui;
using NSubstitute;
using Xunit;

namespace Livestream.Monitor.Tests.Direct2D;

[Collection(UiApplicationCollection.Name)]
public class NativeStreamLauncherShould
{
    [Theory]
    [InlineData("chat", false, false)]
    [InlineData("chat", true, false)]
    [InlineData("stream", false, true)]
    [InlineData("stream", true, true)]
    [InlineData("vod", false, true)]
    [InlineData("vod", true, true)]
    public async Task ShowInitialOutputOnlyForStreamsAndVods(string kind, bool capture, bool expectedVisible)
    {
        var title = Guid.NewGuid().ToString("N");
        const string Url = "https://example.com/channel";
        var api = Substitute.For<IApiClient>();
        api.ApiName.Returns(TwitchApiClient.API_NAME);
        api.HasChatSupport.Returns(true);
        api.GetChatUrl(Arg.Any<LivestreamModel>()).Returns(Task.FromResult(Url));
        api.GetStreamUrl(Arg.Any<LivestreamModel>()).Returns(Task.FromResult(Url));
        var command = new ExecCommand
        {
            FilePath = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Args = "/d /c exit /b 0",
            CaptureStandardOutput = capture,
            CaptureErrorOutput = capture,
        };

        var settings = new Settings
        {
            Twitch =
            {
                ChatCommand = command,
                StreamCommand = command,
                VodCommand = command,
            },
        };

        var handler = Substitute.For<ISettingsHandler>();
        handler.Settings.Returns(settings);

        using var owner = new Form();
        var dispatcher = new UiDispatcher();
        using var launcher = new NativeStreamLauncher(owner.Handle, handler, dispatcher);

        if (kind == "vod")
        {
            var vod = new VodDetails { Title = title, Url = Url, ApiClient = api };
            launcher.OpenVod(vod);
        }
        else
        {
            var channel = new ChannelIdentifier(api, title);
            var stream = new LivestreamModel(title, channel) { DisplayName = title, Live = true };
            await launcher.OpenStream(stream, chat: kind == "chat");
        }

        // Completion stays queued until the UI dispatcher runs, so even a short-lived process cannot hide a flash.
        var windowTitle = kind switch
        {
            "chat" => $"Chat '{title}'",
            "stream" => $"Stream '{title}'",
            _ => title,
        };

        var output = FindWindow(null, windowTitle);
        Assert.NotEqual(nint.Zero, output);
        Assert.Equal(expectedVisible, Win32.IsWindowVisible(output));
    }

    [Fact]
    public async Task CaptureAnOversizedLineWithoutWaitingForANewline()
    {
        var input = new string('x', BoundedOutputBuffer.DefaultCapacity * 8) + "tail";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var reader = new StreamReader(stream);
        var buffer = new BoundedOutputBuffer();
        var chunks = 0;
        await NativeStreamLauncher.Capture(reader, Append, CancellationToken.None);

        Assert.True(chunks > 1);
        Assert.True(buffer.TryRead(out var snapshot));
        Assert.Equal(input[^BoundedOutputBuffer.DefaultCapacity..], snapshot.Text);
        Assert.Equal(input.Length - BoundedOutputBuffer.DefaultCapacity, snapshot.DiscardedCharacters);

        void Append(ReadOnlySpan<char> text)
        {
            chunks++;
            Assert.InRange(text.Length, 1, 8192);
            buffer.Append(text);
        }
    }

    [Fact]
    public async Task NormalizeLineEndingsAcrossReadBoundaries()
    {
        var input = new string('x', 4095) + "\r\nnext\rlast\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var reader = new StreamReader(stream);
        var buffer = new BoundedOutputBuffer();
        await NativeStreamLauncher.Capture(reader, buffer.Append, CancellationToken.None);

        Assert.True(buffer.TryRead(out var snapshot));
        Assert.Equal(new string('x', 4095) + "\nnext\nlast\n", snapshot.Text);
    }

    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string className, string windowName);
}
