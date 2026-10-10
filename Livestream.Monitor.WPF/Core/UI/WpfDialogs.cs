using System.Threading.Tasks;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using MahApps.Metro.Controls.Dialogs;

namespace Livestream.Monitor.WPF.Core.UI;

public sealed class WpfDialogs(IViewAware screen) : IUserDialogs
{
    public async Task ShowMessage(string title, string message) => await screen.ShowMessageAsync(title, message);

    public async Task<bool> Confirm(
        string title,
        string message,
        string affirmative = "OK",
        string negative = "Cancel")
    {
        var buttons = new MetroDialogSettings { AffirmativeButtonText = affirmative, NegativeButtonText = negative };
        var result = await screen.ShowMessageAsync(title, message, MessageDialogStyle.AffirmativeAndNegative, buttons);
        return result == MessageDialogResult.Affirmative;
    }

    public Task<string> Prompt(string title, string message) => screen.ShowDialogAsync(title, message);
}
