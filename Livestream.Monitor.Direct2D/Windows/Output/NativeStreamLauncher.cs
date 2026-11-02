using System.Diagnostics;
using System.Globalization;
using Livestream.Monitor.Core.UI;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Core.Utility;
using Livestream.Monitor.Direct2D.Widgets;
using Livestream.Monitor.Model;

namespace Livestream.Monitor.Direct2D.Windows.Output;

internal sealed class NativeStreamLauncher(
    nint owner,
    ISettingsHandler settingsHandler,
    UiDispatcher dispatcher,
    Func<ThemePalette>? theme = null) : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<OutputWindow> outputs = [];
    private readonly Dictionary<UniqueStreamKey, int> watching = [];

    public bool IsWatching(LivestreamModel stream) => watching.ContainsKey(stream.UniqueStreamKey);

    public async Task OpenStream(LivestreamModel stream, bool chat)
    {
        if (!chat && !stream.Live || chat && !stream.ApiClient.HasChatSupport) return;

        var url = chat ? await stream.GetChatUrl() : await stream.GetStreamUrl();
        var platform = settingsHandler.Settings.GetPlatformSettings(stream.ApiClient.ApiName);
        var command = chat ? platform.ChatCommand : platform.StreamCommand;
        var title = chat ? $"Chat '{stream.DisplayName}'" : $"Stream '{stream.DisplayName}'";
        var launching = chat
            ? $"Launching chat for {stream.DisplayName}..."
            : $"Launching {PlatformNames.GetDisplayName(stream.ApiClient.ApiName)} stream {stream.DisplayName}...";

        Launch(
            title,
            launching,
            url,
            stream.ApiClient.ApiName,
            command,
            showOutput: !chat,
            chat ? null : stream.UniqueStreamKey
        );
    }

    public void OpenVod(VodDetails vod)
    {
        if (!Uri.TryCreate(vod.Url, UriKind.Absolute, out _)) throw new InvalidDataException($"Invalid VOD URL: '{vod.Url}'");

        const int MaxTitleLength = 70;
        var title = vod.Title ?? "";
        if (title.Length > MaxTitleLength) title = title[..MaxTitleLength] + "...";

        var platform = settingsHandler.Settings.GetPlatformSettings(vod.ApiClient.ApiName);
        var launching = $"Launching VOD: {title}...";
        Launch(title, launching, vod.Url, vod.ApiClient.ApiName, platform.VodCommand, showOutput: true);
    }

    public void Dispose()
    {
        lifetime.Cancel();
        foreach (var output in outputs.ToArray()) output.Dispose();

        lifetime.Dispose();
    }

    private void Launch(
        string title,
        string launching,
        string url,
        string api,
        ExecCommand command,
        bool showOutput,
        UniqueStreamKey? streamKey = null)
    {
        var replacements = settingsHandler.Settings.GetLaunchReplacements(api, url);
        var arguments = command.Args;
        foreach (var (key, value) in replacements) arguments = arguments.Replace(key, value);

        arguments = arguments.Trim();
        var output = new OutputWindow(owner: owner, title: title, settings: settingsHandler, theme: theme);
        outputs.Add(output);
        output.Dismissed += () =>
        {
            outputs.Remove(output);
            output.Dispose();
        };

        output.Append(launching);
        output.Append($"{command.FilePath} {command.Args}");
        if (settingsHandler.Settings.DebugMode) output.Append($"[DEBUG] {command.FilePath} {arguments}");

        // As in WPF, a command that cannot be started is reported in its output window, which stays open.
        if (!WindowsCommandResolver.TryResolveExecutable(command.FilePath, out var executable))
        {
            output.Append($"[ERROR] Invalid FileName: {command.FilePath}");
            output.Completed = true;
            output.ShowInitial();
            return;
        }

        if (streamKey != null)
        {
            watching.TryGetValue(streamKey, out var count);
            watching[streamKey] = count + 1;
        }

        if (showOutput) output.ShowInitial();

        var start = new ProcessStartInfo(executable, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = command.CaptureStandardOutput,
            RedirectStandardError = command.CaptureErrorOutput,
        };

        var cancellationToken = lifetime.Token;
        _ = Task.Run(async () =>
        {
            var process = new Process();

            using var observationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            process.StartInfo = start;
            var observation = Task.CompletedTask;
            var failed = false;
            var capture = Task.CompletedTask;
            try
            {
                process.Start();
                // Independent drains prevent one continuously readable pipe from starving the other.
                var outputReader = command.CaptureStandardOutput ? process.StandardOutput : null;
                var errorReader = command.CaptureErrorOutput ? process.StandardError : null;
                capture = Task.WhenAll(Drain(outputReader), Drain(errorReader));

                if (command is { CaptureStandardOutput: false, CaptureErrorOutput: false })
                    dispatcher.Post(output.Hide);
                else observation = Observe(process, observationCancellation.Token);

                await process.WaitForExitAsync(cancellationToken);
                await capture;

                if (process.ExitCode != 0)
                {
                    failed = true;
                    Append($"ERROR: Process exited with code {process.ExitCode.ToString(CultureInfo.InvariantCulture)}.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                failed = true;
                Append(exception.Message);
            }
            finally
            {
                await observationCancellation.CancelAsync();
                await observation;

                // Disposed before canceled reads are awaited; closing the pipe handles completes them.
                process.Dispose();

                if (cancellationToken.IsCancellationRequested)
                {
                    try { await capture; }
                    catch (OperationCanceledException) { }
                    catch (ObjectDisposedException) { }
                    catch (IOException) { }
                }
            }

            if (failed)
            {
                Append("");
                Append("ERROR: Manually close this window when you've finished reading output.");
            }

            if (cancellationToken.IsCancellationRequested) return;

            dispatcher.Post(() =>
            {
                if (streamKey != null)
                {
                    var remaining = watching[streamKey] - 1;

                    if (remaining == 0) watching.Remove(streamKey);
                    else watching[streamKey] = remaining;
                }

                output.Completed = true;

                if (failed) output.ShowFinal();
                else output.Close();
            });

            async Task Observe(Process launched, CancellationToken observationToken)
            {
                try
                {
                    var started = launched.StartTime.ToUniversalTime().ToFileTimeUtc();
                    var observer = new ProcessWindowObserver(new WindowsProcessWindowSource(), launched.Id, started);
                    var found = await observer.WaitForWindow(
                        observationToken,
                        () => cancellationToken.IsCancellationRequested
                    );

                    if (found && !observationToken.IsCancellationRequested)
                    {
                        dispatcher.Post(() =>
                        {
                            if (settingsHandler.Settings.HideStreamOutputMessageBoxOnLoad) output.Hide();
                        });
                    }
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }

            Task Drain(StreamReader? reader) => reader == null
                ? Task.CompletedTask
                : Task.Run(() => Capture(reader, output.AppendChunk, cancellationToken), CancellationToken.None);

            void Append(string? text)
            {
                if (text == null || cancellationToken.IsCancellationRequested) return;

                output.Append(text);
            }
        }, CancellationToken.None);
    }

    internal delegate void AppendOutput(ReadOnlySpan<char> text);

    internal static async Task Capture(StreamReader reader, AppendOutput append, CancellationToken cancellationToken)
    {
        const int ReadSize = 4096;
        var input = new char[ReadSize];
        var normalized = new char[ReadSize];
        var previousCarriageReturn = false;

        while (true)
        {
            var count = await reader.ReadAsync(input.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0) return;

            var written = 0;

            for (var i = 0; i < count; i++)
            {
                var character = input[i];

                if (character == '\r' || character == '\n' && !previousCarriageReturn)
                {
                    // Rich Edit uses one character position per paragraph break.
                    normalized[written++] = '\n';
                }
                else if (character != '\n')
                {
                    normalized[written++] = character;
                }

                previousCarriageReturn = character == '\r';
            }

            append(normalized.AsSpan(0, written));
        }
    }
}
