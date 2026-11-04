using System;
using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.GitHub;
using Livestream.Monitor.Core.Utility;
using Xunit;

namespace Livestream.Monitor.Tests;

public sealed class AppUpdateShould : IDisposable
{
    [Fact]
    public void RestartANotificationLaunchNormallyInTheSameProfile()
    {
        // Process ids are multiples of four, so no process has this one and the restarted launch has nothing to wait for.
        const int ExitedProcess = int.MaxValue;
        var release = new Release { Name = "Speedy startup", HtmlUrl = "https://example.org/release" };
        string[] original = ["--toast-activated", "--updated", "4", "Older", "https://example.org/older", "--data-directory", ".", "--quiet"];

        var arguments = AppUpdate.RestartArguments(original, ExitedProcess, release);
        string[] profile = ["--data-directory", Environment.CurrentDirectory, "--quiet"];
        Assert.Equal([.. profile, "--updated", "2147483647", release.Name, release.HtmlUrl], arguments);

        var notice = AppUpdate.CompleteUpdate(ref arguments);
        Assert.Equal(profile, arguments);
        Assert.NotNull(notice);
        Assert.Null(AppUpdate.CompleteUpdate(ref arguments));
    }

    [Theory]
    [InlineData("2.17.1 - Speedy startup", "Speedy startup")]
    [InlineData(null, "")]
    public void ShowTheInstalledReleaseHeadline(string name, string expected)
    {
        const string ReleasePage = "https://github.com/laurencee/Livestream.Monitor/releases/tag/2.17.1";
        var notice = ReleaseNotice.ForVersion(new Version(2, 17, 1, 0), name, ReleasePage);
        Assert.Equal("2.17.1", notice.VersionText);
        Assert.Equal(expected, notice.Title);
        Assert.Equal(ReleasePage, notice.Url.AbsoluteUri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://github.com/laurencee/Livestream.Monitor/releases/tag/2.17.2")]
    [InlineData("https://example.org/laurencee/Livestream.Monitor/releases/tag/2.17.1")]
    public void OmitTheNotesActionWithoutTheInstalledReleasePage(string url)
    {
        var installed = new Version(major: 2, minor: 17, build: 1, revision: 0);
        var notice = ReleaseNotice.ForVersion(installed, "2.17.2 - Another release", url);
        Assert.Null(notice.Url);
        Assert.Equal("", notice.Title);
    }

    private readonly string directory = Path.Combine(Path.GetTempPath(), "Livestream.Update.Tests." + Guid.NewGuid().ToString("N"));

    public AppUpdateShould() => Directory.CreateDirectory(directory);

    [Fact]
    public void SelectOnlyAnUnambiguousCompatibleVerifiedAsset()
    {
        var asset = new ReleaseAsset
        {
            Name = "Livestream.Monitor.3.1.0.win-x64.net10.exe.br",
            DownloadUrl = "https://github.com/example/update.br",
            Size = 100,
            Digest = "sha256:" + new string('a', 64),
        };
        var release = new Release { TagName = "3.1.0.0" };
        Assert.Null(AppUpdate.FindAsset(release, "Livestream.Monitor", 10));
        release.Assets = [asset];
        Assert.Same(asset, AppUpdate.FindAsset(release, "Livestream.Monitor", 10));
        release.TagName = "3.1.0.2";
        Assert.Null(AppUpdate.FindAsset(release, "Livestream.Monitor", 10));
        release.TagName = "3.1.0";
        Assert.Null(AppUpdate.FindAsset(release, "Livestream.Monitor", 11));
        release.Assets = [asset, asset];
        Assert.Null(AppUpdate.FindAsset(release, "Livestream.Monitor", 10));
        release.Assets = [null, asset];
        Assert.Same(asset, AppUpdate.FindAsset(release, "Livestream.Monitor", 10));
        asset.Digest = "sha256:" + new string('g', 64);
        Assert.Null(AppUpdate.FindAsset(release, "Livestream.Monitor", 10));
        asset.Digest = null;
        Assert.Null(AppUpdate.FindAsset(release, "Livestream.Monitor", 10));
    }

    [Fact]
    public void DecodeTheEntirePayloadAndRejectTruncationTrailingDataAndExpansionBeyondTheLimit()
    {
        var original = new byte[200_000];
        new Random(42).NextBytes(original);
        var packed = Path.Combine(directory, "payload.br");
        var output = Path.Combine(directory, "candidate.exe");
        using (var file = File.Create(packed))
        using (var encoder = new BrotliStream(file, CompressionLevel.SmallestSize)) encoder.Write(original);

        var valid = File.ReadAllBytes(packed);
        AppUpdate.Decode(packed, output, original.Length, CancellationToken.None);
        Assert.Equal(original, File.ReadAllBytes(output));
        Assert.Throws<InvalidDataException>(() => AppUpdate.Decode(packed, output, original.Length - 1, CancellationToken.None));
        File.WriteAllBytes(packed, valid[..^1]);
        Assert.Throws<InvalidDataException>(() => AppUpdate.Decode(packed, output, original.Length, CancellationToken.None));
        File.WriteAllBytes(packed, [.. valid, 0]);
        Assert.Throws<InvalidDataException>(() => AppUpdate.Decode(packed, output, original.Length, CancellationToken.None));
    }

    [Fact]
    public async Task ReplaceARunningExecutableAndCleanUpOnlyItsUpdateFiles()
    {
        var target = Path.Combine(directory, "Renamed monitor.exe");
        var staging = target + ".update";
        var sibling = target + ".previous";
        var settings = Path.Combine(directory, "settings.json");
        var streams = Path.Combine(directory, "livestreams.json");
        var commandProcessor = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        var previous = await File.ReadAllBytesAsync(commandProcessor);
        File.Copy(commandProcessor, target);
        await File.WriteAllTextAsync(sibling, "user backup");
        await File.WriteAllTextAsync(settings, "my settings");
        await File.WriteAllTextAsync(streams, "my streams");

        // Stays running until its input is closed, as the updating instance and the instances of other profiles do during an update.
        var start = new ProcessStartInfo(target) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };

        using var running = Process.Start(start)!;
        try
        {
            using (var failing = await PrepareUpdate(target))
                Assert.Throws<InvalidOperationException>(() => failing.Install(() => throw new InvalidOperationException("No start")));

            Assert.Equal(previous, await File.ReadAllBytesAsync(target));
            Assert.False(Directory.Exists(staging));

            var started = false;
            using (var update = await PrepareUpdate(target))
            {
                // A starting instance of another profile cleans up while this one is about to install.
                await AppUpdate.Cleanup(target);
                update.Install(() => started = true);
            }

            Assert.True(started);
            Assert.Equal(await File.ReadAllBytesAsync(await UpdateExecutable()), await File.ReadAllBytesAsync(target));
            Assert.False(running.HasExited);

            await AppUpdate.Cleanup(target);
            var backup = Assert.Single(Directory.GetFiles(staging));
            Assert.Equal(previous, await File.ReadAllBytesAsync(backup));

            // The restarted instance runs the executable now at the target's path. It waits for the instance that started it,
            // which continues from the renamed file, not for a later process that reused its id or for another program.
            var runningId = running.Id;
            AppUpdate.WaitForPreviousExit(runningId, target, running.StartTime.ToUniversalTime().AddSeconds(-1));
            AppUpdate.WaitForPreviousExit(runningId, commandProcessor, DateTime.UtcNow);
            Assert.False(running.HasExited);
            var restarted = Task.Run(() => AppUpdate.WaitForPreviousExit(runningId, target, DateTime.UtcNow));
            const int ObservedWaitMilliseconds = 300;
            Assert.NotSame(restarted, await Task.WhenAny(restarted, Task.Delay(ObservedWaitMilliseconds)));
            running.StandardInput.Close();
            await restarted;
            Assert.True(running.HasExited);
        }
        finally
        {
            if (!running.HasExited) running.Kill();
            await running.WaitForExitAsync();
        }

        await AppUpdate.Cleanup(target);

        Assert.False(Directory.Exists(staging));
        Assert.Equal("user backup", await File.ReadAllTextAsync(sibling));
        Assert.Equal("my settings", await File.ReadAllTextAsync(settings));
        Assert.Equal("my streams", await File.ReadAllTextAsync(streams));
    }

    [Fact]
    public async Task VerifyTheDigestAndVersionBeforeAcceptingAnExecutable()
    {
        var target = Path.Combine(directory, "monitor.exe");
        await File.WriteAllTextAsync(target, "old");

        using (await PrepareUpdate(target))
        {
            // An instance of another profile runs the same executable and is offered the same update.
            await Assert.ThrowsAsync<IOException>(() => PrepareUpdate(target));
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => PrepareUpdate(target, digest: "sha256:" + new string('0', 64)));
        await Assert.ThrowsAsync<InvalidDataException>(() => PrepareUpdate(target, versionOffset: 1));
        Assert.Equal("old", await File.ReadAllTextAsync(target));
        Assert.False(Directory.Exists(target + ".update"));
    }

    private static async Task<string> UpdateExecutable() =>
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "ui-app.txt"))).Trim();

    /// <summary>Prepares the built application executable as the update of the target.</summary>
    private static async Task<AppUpdate.PreparedUpdate> PrepareUpdate(string target, string digest = null, int versionOffset = 0)
    {
        var executable = await UpdateExecutable();
        var built = Version.Parse(FileVersionInfo.GetVersionInfo(executable).FileVersion!);
        var version = new Version(major: built.Major, minor: built.Minor + versionOffset, build: built.Build, revision: built.Revision);

        using var compressed = new MemoryStream();
        await using (var encoder = new BrotliStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            await encoder.WriteAsync(await File.ReadAllBytesAsync(executable));

        var payload = compressed.ToArray();

        using var client = new HttpClient(new ResponseHandler(payload));
        var asset = new ReleaseAsset
        {
            DownloadUrl = "https://example.test/update.br",
            Size = payload.Length,
            Digest = digest ?? "sha256:" + Convert.ToHexString(SHA256.HashData(payload)),
        };

        var cancellation = CancellationToken.None;
        return await AppUpdate.Prepare(client, asset, version, target, cancellation);
    }

    private sealed class ResponseHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }

    [Fact]
    public async Task RetryATransientDestinationLockAndRestoreTheExecutableWhenReplacementFails()
    {
        var target = Path.Combine(directory, "locked.exe");
        var candidate = Path.Combine(directory, "candidate.exe");
        var backup = target + ".previous";
        await File.WriteAllTextAsync(target, "old");
        await File.WriteAllTextAsync(candidate, "new");
        Action start = () => { };
        var held = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None);
        var replacement = Task.Run(() => AppUpdate.Swap(candidate, target, backup, start));
        try { await Task.Delay(100); }
        finally { await held.DisposeAsync(); }
        await replacement;
        Assert.Equal("old", await File.ReadAllTextAsync(backup));
        Assert.Equal("new", await File.ReadAllTextAsync(target));

        // The candidate is gone, so a second replacement fails after the executable has been moved aside.
        var second = target + ".second";
        Assert.Throws<FileNotFoundException>(() => AppUpdate.Swap(candidate, target, second, start));
        Assert.Equal("new", await File.ReadAllTextAsync(target));
        Assert.False(File.Exists(second));
    }

    public void Dispose() => Directory.Delete(directory, true);
}
