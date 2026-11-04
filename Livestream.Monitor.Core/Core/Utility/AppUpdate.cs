using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs;
using ExternalAPIs.GitHub;

namespace Livestream.Monitor.Core.Utility;

/// <summary>
/// Updates the portable executable in place. The running process downloads and verifies the new executable into a staging
/// directory beside its own, renames its own executable into that directory, moves the new one into its place and starts it.
/// The started process waits for this one to exit, and every start deletes what an earlier update left in the staging directory.
/// </summary>
internal static class AppUpdate
{
    private const int MaximumBytes = 256 * 1024 * 1024;
    private const int BufferSize = 64 * 1024;
    private const int RetryDelayMilliseconds = 250;
    private const int PreviousExitTimeoutMilliseconds = 10000;
    private const int SharingViolationError = 32;
    private const int LockViolationError = 33;
    private const int Win32ErrorMask = 0xFFFF;

    // Followed by the updating process's id, the release name and the release page. A later version receives these from the
    // version that installed it, so their meaning and count must stay the same across versions.
    private const string UpdatedOption = "--updated";
    private const int UpdatedValueCount = 3;

    private const string DigestPrefix = "sha256:";
    private const int UnbufferedSize = 1;
    private const string StagingSuffix = ".update";
    private const string LockFile = "update.lock";
    private const string PayloadFile = "payload.br";
    private const string CandidateFile = "candidate.exe";
    private const string BackupPrefix = "previous-";
    private const string BackupExtension = ".exe";
    private const string PreservedMessage = "The existing executable has been preserved. You can download the release manually.";
    private static readonly Assembly EntryAssembly = Assembly.GetEntryAssembly() ?? throw new InvalidOperationException("Missing entry assembly.");
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MoveTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(2);

    // Tells later whether an instance of another profile has replaced the executable. The version resource cannot tell:
    // Windows reads it from the running image instead of the file now at that path.
    private static readonly DateTime StartedExecutableWritten;

    // An explicit static constructor makes the runtime read the time at the first call into this class, which startup makes
    // before anything else, instead of at some later first use of a static field.
    static AppUpdate() => StartedExecutableWritten = File.GetLastWriteTimeUtc(Environment.ProcessPath!);

    public static string AssetName(string frontend, Version version, int runtimeMajor) =>
        $"{frontend}.{Normalize(version).ToString(version.Revision > 0 ? 4 : 3)}.win-x64.net{runtimeMajor}.exe.br";

    internal static Version Normalize(Version version)
    {
        var build = Math.Max(0, version.Build);
        var revision = Math.Max(0, version.Revision);
        return new Version(major: version.Major, minor: version.Minor, build: build, revision: revision);
    }

    public static ReleaseAsset FindAsset(Release release, string frontend, int runtimeMajor)
    {
        if (!Version.TryParse(release.TagName, out var version)) return null;

        var name = AssetName(frontend, version, runtimeMajor);
        var matches = (release.Assets ?? []).Where(asset => asset?.Name == name).ToArray();
        if (matches.Length != 1) return null;

        var asset = matches[0];
        if (asset.Size is <= 0 or > MaximumBytes || asset.Digest?.StartsWith(DigestPrefix, StringComparison.Ordinal) != true) return null;

        var digest = asset.Digest.AsSpan(DigestPrefix.Length);
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        var status = Convert.FromHexString(digest, hash, out _, out var written);
        if (status != OperationStatus.Done || written != hash.Length) return null;

        return Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? asset : null;
    }

    public static async Task Offer(
        IUserDialogs dialogs,
        Func<Func<CancellationToken, Task>, Task> progress,
        Func<Task<bool>> beforeRestart,
        Func<Task> shutdown)
    {
        Release release;
        try
        {
            release = await new GitHubClient().GetLatestRelease();
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException or OperationCanceledException or JsonException)
        {
            // Being offline or rate limited is not a failed update. The next start checks again.
            return;
        }

        var assembly = EntryAssembly;
        if (release == null || !Version.TryParse(release.TagName, out var version)) return;
        if (Normalize(version) <= Normalize(assembly.GetName().Version!)) return;

        PreparedUpdate prepared = null;
        var restarting = false;
        string failure = null;
        try
        {
            var framework = new System.Runtime.Versioning.FrameworkName(AppContext.TargetFrameworkName!);
            var asset = FindAsset(release, "Livestream.Monitor", framework.Version.Major);
            // The runtime reports an empty Location for a single-file app. An ordinary build output has other files beside the
            // executable, so the executable cannot be replaced on its own.
#pragma warning disable IL3000
            var portable = assembly.Location.Length == 0;
#pragma warning restore IL3000
            if (!portable || asset == null)
            {
                if (await dialogs.Confirm(
                        title: "New version available",
                        message: "Open the release page to download this version?",
                        affirmative: "Download",
                        negative: "Later"))
                {
                    OpenRelease(release.HtmlUrl);
                }
                return;
            }

            var updateMessage = $"Version {version} is available. Download it and restart?";
            var accepted = await dialogs.Confirm(
                title: "New version available",
                message: updateMessage,
                affirmative: "Update and restart",
                negative: "Later");
            if (!accepted) return;

            // An instance of another profile may have installed this or a later version since this one started. Installing the
            // offered version again could then replace a later one, so this instance restarts into the installed one.
            var target = Environment.ProcessPath!;
            if (File.GetLastWriteTimeUtc(target) == StartedExecutableWritten)
            {
                await progress(async cancellation =>
                {
                    using var client = HttpClientExtensions.CreateCompressionHttpClient();
                    prepared = await Prepare(client, asset, version, target, cancellation);
                });
                if (prepared == null) return;
            }

            if (!await beforeRestart()) return;

            // Checked again while this instance holds the update lock, which no other instance can now install under.
            if (prepared != null && File.GetLastWriteTimeUtc(target) != StartedExecutableWritten)
            {
                prepared.Dispose();
                prepared = null;
            }

            var arguments = RestartArguments(Environment.GetCommandLineArgs().Skip(1).ToArray(), Environment.ProcessId, release);
            var workingDirectory = Environment.CurrentDirectory;

            // Runs on the caller's UI thread, so the window cannot close and end the process while the executable is moved aside.
            if (prepared == null) Start(target, workingDirectory, arguments);
            else prepared.Install(() => Start(target, workingDirectory, arguments));

            restarting = true;
        }
        catch (OperationCanceledException) { }
        catch (UpdateNotRestoredException exception)
        {
            failure = exception.Message;
        }
        catch (Exception exception)
        {
            failure = exception.Message + Environment.NewLine + PreservedMessage;
        }
        finally
        {
            // Releases the update lock before any dialog, which can stay open for as long as the user leaves it.
            prepared?.Dispose();
        }

        if (failure != null) await dialogs.ShowMessage("Unable to update", failure);
        else if (restarting) await shutdown();
    }

    private static void OpenRelease(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Invalid release URL.");

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    /// <summary>
    /// Downloads, verifies and decodes the update into the staging directory beside the target. The result holds the staging
    /// lock, which keeps other instances from updating or cleaning up, until it is disposed.
    /// </summary>
    public static async Task<PreparedUpdate> Prepare(
        HttpClient client,
        ReleaseAsset asset,
        Version version,
        string target,
        CancellationToken cancellation)
    {
        var directory = target + StagingSuffix;
        var staging = Directory.CreateDirectory(directory);
        if ((staging.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException($"{directory} must be an ordinary folder.");

        if (!TryOpenLock(directory, out var updateLock))
            throw new IOException("Another Livestream Monitor instance is installing an update. Try again later.");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(DownloadTimeout);
            var token = timeout.Token;
            var packed = Path.Combine(directory, PayloadFile);
            using (var response = await client.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(token);
                await using var output = File.Create(packed);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[BufferSize];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, token)) != 0)
                {
                    total += read;
                    if (total > asset.Size || total > MaximumBytes) throw new InvalidDataException("Update download exceeds its advertised size.");

                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                }

                var actualDigest = Convert.ToHexString(hash.GetHashAndReset());
                if (total != asset.Size || !actualDigest.Equals(asset.Digest[DigestPrefix.Length..], StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Update download failed its size or SHA-256 check.");
            }

            var candidate = Path.Combine(directory, CandidateFile);
            await Task.Run(() => Decode(packed, candidate, MaximumBytes, token), token);
            ValidateExecutable(candidate, version);
            token.ThrowIfCancellationRequested();
            return new PreparedUpdate(updateLock, directory, target);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            Discard(directory, updateLock);
            throw new TimeoutException("The update download timed out.");
        }
        catch
        {
            Discard(directory, updateLock);
            throw;
        }
    }

    public static void Decode(
        string packed,
        string destination,
        int maximumBytes,
        CancellationToken cancellation)
    {
        using var input = File.OpenRead(packed);
        using var output = File.Create(destination);
        using var decoder = new BrotliDecoder();
        var source = new byte[BufferSize];
        var decoded = new byte[BufferSize];
        var available = 0;
        long total = 0;
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            // A read of no bytes means the end of the payload only when there was room to read into.
            var room = source.Length - available;
            var read = input.Read(source, available, room);
            var ended = room > 0 && read == 0;
            available += read;
            var inputSpan = source.AsSpan(0, available);
            var status = decoder.Decompress(inputSpan, decoded, out var consumed, out var written);
            total += written;
            if (total > maximumBytes) throw new InvalidDataException("Decoded update exceeds the size limit.");

            output.Write(decoded, 0, written);
            available -= consumed;
            source.AsSpan(consumed, available).CopyTo(source);
            if (status == OperationStatus.Done)
            {
                if (available != 0 || input.ReadByte() != -1) throw new InvalidDataException("Trailing data in update payload.");

                output.Flush(true);
                return;
            }

            if (status == OperationStatus.InvalidData || (status == OperationStatus.NeedMoreData && ended))
                throw new InvalidDataException("Invalid or incomplete Brotli update.");
        }
    }

    private static void ValidateExecutable(string path, Version version)
    {
        var info = FileVersionInfo.GetVersionInfo(path);
        if (!Version.TryParse(info.FileVersion, out var actual) || Normalize(actual) != Normalize(version))
            throw new InvalidDataException("Update executable version is incorrect.");

        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        if (reader.PEHeaders.CoffHeader.Machine != Machine.Amd64) throw new InvalidDataException("Update executable is not x64.");
    }

    /// <summary>
    /// The arguments that start the updated executable in this instance's profile and tell it which process and release it follows.
    /// </summary>
    internal static string[] RestartArguments(string[] originalArguments, int processId, Release release)
    {
        var arguments = new System.Collections.Generic.List<string>();
        for (var i = 0; i < originalArguments.Length; i++)
        {
            if (originalArguments[i] == UpdatedOption) { i += UpdatedValueCount; continue; }
            // Windows delivers a toast callback only to the launch that the notification started, not to a restart.
            if (originalArguments[i] != "--toast-activated") arguments.Add(originalArguments[i]);
        }

        var index = arguments.IndexOf("--data-directory");
        if (index >= 0 && index + 1 < arguments.Count) arguments[index + 1] = Environment.CurrentDirectory;

        arguments.Add(UpdatedOption);
        arguments.Add(processId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        arguments.Add(release.Name ?? "");
        arguments.Add(release.HtmlUrl ?? "");
        return arguments.ToArray();
    }

    /// <summary>
    /// For a start that follows an update: removes the update switch from the arguments, waits for the process that installed
    /// the update to exit and returns the notice of the installed release. Returns null for any other start.
    /// </summary>
    public static ReleaseNotice CompleteUpdate(ref string[] arguments)
    {
        var index = Array.IndexOf(arguments, UpdatedOption);
        if (index < 0 || index + UpdatedValueCount >= arguments.Length) return null;

        var processId = arguments[index + 1];
        var notice = ReleaseNotice.ForVersion(EntryAssembly.GetName().Version!, name: arguments[index + 2], url: arguments[index + 3]);
        arguments = [.. arguments.Take(index), .. arguments.Skip(index + 1 + UpdatedValueCount)];

        if (!int.TryParse(processId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id))
            return notice;

        using var current = Process.GetCurrentProcess();
        WaitForPreviousExit(id, Environment.ProcessPath!, current.StartTime.ToUniversalTime());
        return notice;
    }

    /// <summary>
    /// Waits for the process that installed the update and started this one to exit, and ends it if it does not. That process
    /// holds its profile's single-instance claim and saves its settings while it closes. A start that continued while it runs
    /// would hand over to it and exit, leaving no instance once it has closed.
    /// </summary>
    internal static void WaitForPreviousExit(int processId, string ownPath, DateTime ownStartUtc)
    {
        try
        {
            using var previous = Process.GetProcessById(processId);

            // Keeps one handle open for the checks below, so they all act on the same process even if its id is reused meanwhile.
            _ = previous.SafeHandle;

            // The installing process was started from this process's path and before it. Any other process has reused its id,
            // or the id comes from a repeated command line, and must be left alone. The process name would not match: it
            // follows the file, which the update renamed.
            var startedFromOwnPath = string.Equals(previous.MainModule?.FileName, ownPath, StringComparison.OrdinalIgnoreCase);
            if (!startedFromOwnPath || previous.StartTime.ToUniversalTime() > ownStartUtc) return;
            if (previous.WaitForExit(PreviousExitTimeoutMilliseconds)) return;

            previous.Kill();
            previous.WaitForExit();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The process has exited, or it is another user's process that has reused the id.
        }
    }

    public sealed class PreparedUpdate(FileStream updateLock, string directory, string target) : IDisposable
    {
        /// <summary>Replaces the target with the prepared executable, then runs <paramref name="start"/> to start it.</summary>
        public void Install(Action start)
        {
            var candidate = Path.Combine(directory, CandidateFile);
            var backup = Path.Combine(directory, BackupPrefix + Guid.NewGuid().ToString("N") + BackupExtension);
            Swap(candidate, target, backup, start);
        }

        public void Dispose() => Discard(directory, updateLock);
    }

    /// <summary>
    /// Moves the target to the backup path and the candidate into its place, then runs <paramref name="start"/>. If the
    /// replacement or the start fails, the backup is moved back before the failure is rethrown.
    /// </summary>
    internal static void Swap(
        string candidate,
        string target,
        string backup,
        Action start)
    {
        // Windows lets a running executable be renamed but not overwritten or deleted. Processes running it, including those of
        // other profiles, continue from the renamed file.
        Move(source: target, destination: backup, overwrite: false);

        try
        {
            Move(source: candidate, destination: target, overwrite: false);
            start();
        }
        catch (Exception failure)
        {
            try { Move(source: backup, destination: target, overwrite: true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                var message = failure.Message + Environment.NewLine + "The previous version could not be put back. It is kept as " + backup;
                throw new UpdateNotRestoredException(message, failure);
            }

            throw;
        }
    }

    private static void Start(string target, string workingDirectory, string[] arguments)
    {
        var start = new ProcessStartInfo(target) { WorkingDirectory = workingDirectory, UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new IOException("Unable to start the updated application.");
    }

    private static void Move(string source, string destination, bool overwrite)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                File.Move(source, destination, overwrite);
                return;
            }
            // Antivirus scanners briefly open new and renamed executables.
            catch (IOException exception) when (IsTransient(exception) && watch.Elapsed < MoveTimeout) { }
            catch (UnauthorizedAccessException) when (watch.Elapsed < MoveTimeout) { }

            Thread.Sleep(RetryDelayMilliseconds);
        }
    }

    /// <summary>
    /// Deletes what earlier updates left in the target's staging directory. Only the updater's own files are deleted, so the
    /// directory stays if it holds anything else.
    /// </summary>
    internal static Task Cleanup(string target) => Task.Run(() =>
    {
        var directory = new DirectoryInfo(target + StagingSuffix);
        if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0) return;

        try
        {
            // Windows releases the executable of the process that installed the update shortly after that process exits, so
            // deletion is retried. A previous executable that another profile is still running stays until a later start.
            var watch = Stopwatch.StartNew();
            while (true)
            {
                // An instance that holds the lock is installing an update and still needs these files. The lock is released
                // between attempts so that an instance can start installing meanwhile.
                if (!TryOpenLock(directory.FullName, out var updateLock)) return;

                bool deleted;
                try { deleted = TryDeleteBackups(directory.FullName); }
                finally { Discard(directory.FullName, updateLock); }

                if (deleted || watch.Elapsed >= CleanupTimeout) return;

                Thread.Sleep(RetryDelayMilliseconds);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    });

    /// <summary>Claims the staging directory for this process. Returns false while another process holds it.</summary>
    private static bool TryOpenLock(string directory, out FileStream updateLock)
    {
        try
        {
            updateLock = new FileStream(
                path: Path.Combine(directory, LockFile),
                mode: FileMode.OpenOrCreate,
                access: FileAccess.ReadWrite,
                share: FileShare.None,
                bufferSize: UnbufferedSize,
                options: FileOptions.DeleteOnClose);

            return true;
        }
        catch (IOException exception) when ((exception.HResult & Win32ErrorMask) == SharingViolationError)
        {
            updateLock = null;
            return false;
        }
    }

    /// <summary>Deletes the download and its decoded executable, releases the lock and removes the staging directory if it is empty.</summary>
    private static void Discard(string directory, FileStream updateLock)
    {
        TryDelete(Path.Combine(directory, PayloadFile));
        TryDelete(Path.Combine(directory, CandidateFile));
        updateLock.Dispose();

        try { Directory.Delete(directory); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool TryDeleteBackups(string directory)
    {
        var deleted = true;
        foreach (var backup in Directory.GetFiles(directory, BackupPrefix + "*" + BackupExtension)) deleted &= TryDelete(backup);

        return deleted;
    }

    private static bool TryDelete(string file)
    {
        try
        {
            // A read-only executable keeps that attribute when it is moved aside, and could then never be deleted.
            var info = new FileInfo(file);
            if (info is { Exists: true, IsReadOnly: true }) info.IsReadOnly = false;

            info.Delete();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsTransient(IOException exception) =>
        (exception.HResult & Win32ErrorMask) is SharingViolationError or LockViolationError;

    /// <summary>The update failed and the previous executable is not back in its place.</summary>
    private sealed class UpdateNotRestoredException(string message, Exception innerException) : IOException(message, innerException);
}

internal sealed record ReleaseNotice(string VersionText, string Title, Uri Url)
{
    internal static ReleaseNotice ForVersion(Version version, string name, string url)
    {
        var text = version.ToString(version.Revision > 0 ? 4 : 3);
        var title = string.Concat((name ?? "").Select(character => char.IsWhiteSpace(character) ? ' ' : character)
            .Where(character => !char.IsControl(character) && char.GetUnicodeCategory(character) != System.Globalization.UnicodeCategory.Format)).Trim();
        var prefix = title.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? "v" + text : text;
        if (title.Equals(prefix, StringComparison.OrdinalIgnoreCase)) title = "";
        else if (title.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase)) title = title[prefix.Length..].TrimStart(' ', '-', '–', '—', '·', ':');

        const string ReleasePath = "/laurencee/Livestream.Monitor/releases/tag/";
        var valid = Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
            uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
            uri.AbsolutePath.StartsWith(ReleasePath, StringComparison.OrdinalIgnoreCase) &&
            Version.TryParse(uri.AbsolutePath[ReleasePath.Length..], out var tag) && AppUpdate.Normalize(tag) == AppUpdate.Normalize(version);

        // Without the installed version's release page, the name is not known to be that release's either.
        return new ReleaseNotice(text, valid ? title : "", valid ? uri : null);
    }
}
