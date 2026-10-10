using System.Threading.Tasks;

namespace Livestream.Monitor.Core;

public interface IUserDialogs
{
    Task ShowMessage(string title, string message);

    /// <summary>True when the user chooses the affirmative button.</summary>
    Task<bool> Confirm(
        string title,
        string message,
        string affirmative = "OK",
        string negative = "Cancel");

    Task<string> Prompt(string title, string message);
}
