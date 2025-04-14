using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using MahApps.Metro;
using Newtonsoft.Json;

namespace Livestream.Monitor.Core
{
    // TODO don't like this looking back at it, should just have load and save methods.
    // Settings Load(); Save(Settings settings);
    // A single settings instance created from this could just directly be passed around instead, except places that actually needed to save
    public class SettingsHandler : ISettingsHandler
    {
        private const string SettingsFileName = "settings.json";
        private bool settingsLoaded;
        private Settings settings;

        public Settings Settings
        {
            get
            {
                if (!settingsLoaded) LoadSettings();
                return settings;
            }
        }

        public void SaveSettings()
        {
            try
            {
                File.WriteAllText(SettingsFileName, JsonConvert.SerializeObject(settings, Formatting.Indented));
            }
            catch (Exception)
            {
                // can't do much...
            }
        }

        private void LoadSettings()
        {
            if (settingsLoaded) return;
            try
            {
                bool saveSettings;
                if (File.Exists(SettingsFileName))
                {
                    settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(SettingsFileName));
                }

                if (settings == null) // init
                {
                    settings = new Settings()
                    {
                        SettingsVersion = Settings.CurrentSettingsVersion,
                        CheckForNewVersions = true,
                        MinimumEventViewers = Settings.DefaultMinimumPopularEventViewers,
                    };

                    if (File.Exists(Settings.DefaultStreamlinkFullPath))
                        settings.LivestreamerFullPath = Settings.DefaultStreamlinkFullPath;
                    else if (File.Exists(Settings.DefaultStreamlinkX86FullPath))
                        settings.LivestreamerFullPath = Settings.DefaultStreamlinkX86FullPath;
                    else if (File.Exists(Settings.DefaultLivestreamerFullPath))
                        settings.LivestreamerFullPath = Settings.DefaultLivestreamerFullPath;
                    else
                        settings.LivestreamerFullPath = Settings.DefaultStreamlinkFullPath;

                    if (File.Exists(Settings.DefaultChromeFullPath))
                        settings.ChatCommandLine = Settings.DefaultChromeCommand;
                    else
                        settings.ChatCommandLine = Settings.DefaultEdgeChatCommand;

                    saveSettings = true;
                }
                else
                {
                    saveSettings = ExcludeNotifyJsonConverter.SaveRequired;
                    saveSettings = MigrateSettingsVersion(saveSettings);
                }

                if (saveSettings) SaveSettings();

                settings.PropertyChanged += SettingsOnPropertyChanged;
                settings.ExcludeFromNotifying.CollectionChanged += (sender, args) => SaveSettings();
                settingsLoaded = true;
            }
            catch (Exception)
            {
                settings = new Settings();
                // probably should log error
            }
        }

        private bool MigrateSettingsVersion(bool saveSettings)
        {
            if (settings.SettingsVersion >= Settings.CurrentSettingsVersion) return saveSettings;

            switch (settings.SettingsVersion)
            {
                case 0:
                    settings.CheckForNewVersions = true;
                    break;
                case 1:
                    // twitch changed their scope requirements so we must force re-authentication
                    settings.TwitchAuthToken = null;
                    break;
                case 2:
                    // we were storing null values in exclusions, this cleans any up
                    for (var i = settings.ExcludeFromNotifying.Count - 1; i >= 0; i--)
                    {
                        var uniqueStreamKey = settings.ExcludeFromNotifying[i];
                        if (uniqueStreamKey.StreamId == null || uniqueStreamKey.ApiClientName == null)
                            settings.ExcludeFromNotifying.Remove(uniqueStreamKey);
                    }
                    break;
            }

            settings.SettingsVersion = Settings.CurrentSettingsVersion;
            return true;
        }

        private void SettingsOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(settings.MetroThemeBaseColour))
            {
                var currentTheme = ThemeManager.DetectAppStyle(Application.Current.MainWindow);
                var baseTheme = ThemeManager.GetAppTheme(settings.MetroThemeBaseColour.ToString());
                ChangeTheme(baseTheme, currentTheme.Item2);
                SaveSettings();
            }
            else if (e.PropertyName == nameof(settings.MetroThemeAccentColour))
            {
                var currentTheme = ThemeManager.DetectAppStyle(Application.Current.MainWindow);
                var accent = ThemeManager.GetAccent(settings.MetroThemeAccentColour.ToString());
                ChangeTheme(currentTheme.Item1, accent);
                SaveSettings();
            }
        }

        private void ChangeTheme(AppTheme baseColour, Accent accentColour)
        {
            // change the theme for the main window so the update is immediate
            ThemeManager.ChangeAppStyle(Application.Current.MainWindow, accentColour, baseColour);

            // change the default theme for all future windows opened
            ThemeManager.ChangeAppStyle(Application.Current, accentColour, baseColour);
        }
    }
}
