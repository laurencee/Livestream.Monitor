namespace Livestream.Monitor.Core
{
    public interface ISettingsHandler
    {
        Settings Settings { get; }

        void SaveSettings();
    }
}