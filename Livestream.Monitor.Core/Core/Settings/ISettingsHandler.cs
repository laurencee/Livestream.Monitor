namespace Livestream.Monitor.Core.Settings
{
    public interface ISettingsHandler
    {
        Settings Settings { get; }

        void SaveSettings();

        /// <summary>Persists a platform's settings before applying them to the running app.</summary>
        bool TrySavePlatformSettings(ApiPlatformSettings platform);
    }
}