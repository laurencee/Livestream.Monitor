using System;
using System.Threading.Tasks;
using Livestream.Monitor.Core;

namespace Livestream.Monitor.Model.Monitoring;

/// <summary>Confirms removal of a followed stream and reports failures.</summary>
public static class StreamRemoval
{
    public static Task<bool> ConfirmRemoval(this IUserDialogs dialogs, LivestreamModel stream)
    {
        var question = $"Are you sure you want to remove livestream '{stream.DisplayName}'?";
        return dialogs.Confirm("Remove livestream", question, affirmative: "Remove");
    }

    /// <summary>Removes the stream, telling the user instead of throwing when that fails.</summary>
    public static async Task RemoveOrReport(
        this IMonitorStreamsModel streams,
        LivestreamModel stream,
        IUserDialogs dialogs)
    {
        try
        {
            await streams.RemoveLivestream(stream.ChannelIdentifier);
        }
        catch (Exception exception)
        {
            const string Tip =
                "TIP: You may have to remove the livestream directly from the livestreams.json file... :(";

            var message = $"Error removing '{stream.DisplayName}': {exception.ExtractErrorMessage()}" +
                          Environment.NewLine + Environment.NewLine + Tip;

            await dialogs.ShowMessage("Error Removing Stream", message);
        }
    }
}
