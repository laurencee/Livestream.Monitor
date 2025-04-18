using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using MahApps.Metro;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
                var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(SettingsFileName, json);
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
                var fullSettingsRaw = string.Empty;
                bool saveSettings;
                if (File.Exists(SettingsFileName))
                {
                    fullSettingsRaw = File.ReadAllText(SettingsFileName);
                    settings = JsonConvert.DeserializeObject<Settings>(fullSettingsRaw);
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

                    string chatCommandFilePath;
                    string chatCommandFileArgs;
                    if (File.Exists(Settings.DefaultChromeFullPath))
                    {
                        settings.ChatCommandLine = Settings.DefaultChromeCommand;
                        chatCommandFilePath = Settings.DefaultChromeFullPath;
                        chatCommandFileArgs = Settings.DefaultChromeArgs;
                    }
                    else
                    {
                        settings.ChatCommandLine = Settings.DefaultEdgeChatCommand;
                        chatCommandFilePath = Settings.DefaultEdgePath;
                        chatCommandFileArgs = Settings.UrlReplacementToken;
                    }

                    settings.Twitch.ChatCommand.FilePath = chatCommandFilePath;
                    settings.Twitch.ChatCommand.Args = chatCommandFileArgs;
                    settings.Kick.ChatCommand.FilePath = chatCommandFilePath;
                    settings.Kick.ChatCommand.Args = chatCommandFileArgs;
                    settings.YouTube.ChatCommand.FilePath = chatCommandFilePath;
                    settings.YouTube.ChatCommand.Args = chatCommandFileArgs;

                    saveSettings = true;
                }
                else
                {
                    saveSettings = ExcludeNotifyJsonConverter.SaveRequired;
                    saveSettings = MigrateSettingsVersion(fullSettingsRaw, saveSettings);
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

        /// <param name="fullSettingsRaw">If we changed the format we need the raw text to convert settings</param>
        /// <param name="saveSettings">Current flag to passthrough if we don't change anything</param>
        private bool MigrateSettingsVersion(string fullSettingsRaw, bool saveSettings)
        {
            if (settings.SettingsVersion >= Settings.CurrentSettingsVersion) return saveSettings;

            // apply migrations 1 version at a time
            while (settings.SettingsVersion < Settings.CurrentSettingsVersion)
            {
                switch (settings.SettingsVersion)
                {
                    case 0:
                        settings.CheckForNewVersions = true;
                        break;
                    case 1: // property that needed to be reset no longer exists, see commit history if you care enough
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
                    case 3:
                        dynamic json = JObject.Parse(fullSettingsRaw);
                        bool shouldPassthrough = json.PassthroughClientId;
                        string authToken = json.TwitchAuthToken;
                        string chatCommandLine = json.ChatCommandLine;

                        settings.Twitch.PassthroughClientId = shouldPassthrough;
                        settings.Twitch.AuthToken = authToken;

                        if (!string.IsNullOrWhiteSpace(chatCommandLine))
                        {
                            bool filePathQuoted = chatCommandLine.StartsWith("\"");
                            string filePathEnd = filePathQuoted ? ".exe\"" : ".exe";

                            int endOfFilePathIndex = chatCommandLine.IndexOf(filePathEnd, StringComparison.Ordinal) + filePathEnd.Length;
                            if (endOfFilePathIndex < 0) // might be using a custom command off the env path
                            {
                                if (!filePathQuoted) endOfFilePathIndex = chatCommandLine.IndexOf(' ');
                                else
                                {
                                    var enclosingDoubleQuoteIndex = chatCommandLine.IndexOf('"', 1);
                                    endOfFilePathIndex = chatCommandLine.IndexOf(' ', enclosingDoubleQuoteIndex);
                                }
                            }

                            var filePath = chatCommandLine.Substring(0, endOfFilePathIndex).Trim();
                            var args = chatCommandLine.Substring(endOfFilePathIndex).Trim();

                            settings.Twitch.ChatCommand.FilePath = filePath;
                            settings.Twitch.ChatCommand.Args = args;
                            settings.YouTube.ChatCommand.FilePath = filePath;
                            settings.YouTube.ChatCommand.Args = args;
                            settings.Kick.ChatCommand.FilePath = filePath;
                            settings.Kick.ChatCommand.Args = args;
                        }
                        break;
                }

                settings.SettingsVersion++;
            }

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
