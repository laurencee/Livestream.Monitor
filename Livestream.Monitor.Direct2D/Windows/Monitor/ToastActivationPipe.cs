using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace Livestream.Monitor.Direct2D.Windows.Monitor;

/// <summary>Passes a toast target to the primary instance if COM launches a second process.</summary>
internal sealed class ToastActivationPipe : IDisposable
{
    private const byte Accepted = 1;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly string PipeName = GetPipeName();
    private readonly CancellationTokenSource stopping = new();
    private readonly Task listener;

    public ToastActivationPipe(Func<ToastActivation, CancellationToken, Task<bool>> activate, string? pipeName = null) =>
        listener = Listen(pipeName ?? PipeName, activate, stopping.Token);

    public static void Forward(ToastActivation target, string? pipeName = null)
    {
        using var timeout = new CancellationTokenSource(RequestTimeout);
        ForwardToPrimary(pipeName ?? PipeName, target, timeout.Token).GetAwaiter().GetResult();
    }

    private static async Task ForwardToPrimary(string pipeName, ToastActivation target, CancellationToken cancellation)
    {
        var options = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;
        await using var pipe = new NamedPipeClientStream(serverName: ".", pipeName: pipeName, direction: PipeDirection.InOut, options: options);
        await pipe.ConnectAsync(cancellation).ConfigureAwait(false);

        var payload = Encoding.UTF8.GetBytes(target.Encode());
        await pipe.WriteAsync(BitConverter.GetBytes(payload.Length), cancellation).ConfigureAwait(false);
        await pipe.WriteAsync(payload, cancellation).ConfigureAwait(false);

        var reply = new byte[1];
        await pipe.ReadExactlyAsync(reply, cancellation).ConfigureAwait(false);
        if (reply[0] != Accepted) throw new IOException("The running Direct2D instance rejected the notification target.");
    }

    private static async Task Listen(
        string pipeName,
        Func<ToastActivation, CancellationToken, Task<bool>> activate,
        CancellationToken stopping)
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                var options = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;
                await using var pipe = new NamedPipeServerStream(
                    pipeName: pipeName,
                    direction: PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    transmissionMode: PipeTransmissionMode.Byte,
                    options: options);

                await pipe.WaitForConnectionAsync(stopping).ConfigureAwait(false);

                using var request = CancellationTokenSource.CreateLinkedTokenSource(stopping);
                request.CancelAfter(RequestTimeout);
                try
                {
                    await Receive(pipe, activate, request.Token).ConfigureAwait(false);
                }
                catch (Exception exception) when (!stopping.IsCancellationRequested && exception is IOException or OperationCanceledException)
                {
                    StreamNotifications.LogFailure(exception);
                }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StreamNotifications.LogFailure(exception);
        }
    }

    private static async Task Receive(
        Stream pipe,
        Func<ToastActivation, CancellationToken, Task<bool>> activate,
        CancellationToken cancellation)
    {
        var lengthBytes = new byte[sizeof(int)];
        await pipe.ReadExactlyAsync(lengthBytes, cancellation).ConfigureAwait(false);
        var length = BitConverter.ToInt32(lengthBytes);
        if (length is <= 0 or > StreamNotificationTarget.MaxArgumentLength) return;

        var payload = new byte[length];
        await pipe.ReadExactlyAsync(payload, cancellation).ConfigureAwait(false);
        if (!ToastActivation.TryDecode(Encoding.UTF8.GetString(payload), out var target)) return;

        // A queued request can be abandoned when the primary closes. Acknowledge only UI acceptance.
        var accepted = await activate(target, cancellation).ConfigureAwait(false);
        if (!accepted) return;

        byte[] reply = [Accepted];
        await pipe.WriteAsync(reply, cancellation).ConfigureAwait(false);
    }

    private static string GetPipeName()
    {
        using var process = Process.GetCurrentProcess();
        return $"Livestream.Monitor.Direct2D.Toast.{process.SessionId}.{Environment.UserName}";
    }

    public void Dispose()
    {
        stopping.Cancel();
        listener.GetAwaiter().GetResult();
        stopping.Dispose();
    }
}
