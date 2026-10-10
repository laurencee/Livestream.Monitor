namespace Livestream.Monitor.Core.Settings
{
    public interface ISettingsHandler
    {
        Settings Settings { get; }

        void SaveSettings();
    }
}