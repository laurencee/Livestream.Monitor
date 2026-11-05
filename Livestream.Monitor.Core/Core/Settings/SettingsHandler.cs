using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace Livestream.Monitor.Core.Settings
{
    // TODO don't like this looking back at it, should just have load and save methods.
    // Settings Load(); Save(Settings settings);
    // A single settings instance created from this could just directly be passed around instead, except places that actually needed to save
    public class SettingsHandler : ISettingsHandler
    {
        public const string SettingsFileName = "settings.json";

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
                var json = JsonSerializer.Serialize(settings, ExternalAPIs.JsonDefaults.IndentedOptions);
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
                    try
                    {
                        fullSettingsRaw = ReadSettingsFile();
                    }
                    catch (Exception e) when (e is IOException or JsonException)
                    {
                        // Another instance saving the file can hold it open or leave it half written,
                        // so retry once before treating it as broken.
                        const int RetryDelayMilliseconds = 200;
                        Thread.Sleep(RetryDelayMilliseconds);
                        fullSettingsRaw = ReadSettingsFile();
                    }
                }

                if (settings == null) // init
                {
                    settings = CreateDefaultSettings();
                    saveSettings = true;
                }
                else
                {
                    saveSettings = ExcludeNotifyJsonConverter.SaveRequired;
                    saveSettings = MigrateSettingsVersion(fullSettingsRaw, saveSettings);
                }

                if (saveSettings) SaveSettings();
            }
            catch (Exception)
            {
                // probably should log error
                // A file still unreadable after the retry is left as is until settings are next saved, so it can be fixed by hand.
                settings = CreateDefaultSettings();
            }

            // Later reads share this instance, so changes made through it are saved for the rest of the session.
            settings.PropertyChanged += SettingsOnPropertyChanged;
            settings.ExcludeFromNotifying.CollectionChanged += (_, _) => SaveSettings();
            settingsLoaded = true;

            string ReadSettingsFile()
            {
                var text = File.ReadAllText(SettingsFileName);
                ExcludeNotifyJsonConverter.SaveRequired = false;
                // An empty file, e.g. left by an interrupted save, starts from defaults like a missing one.
                if (!string.IsNullOrWhiteSpace(text))
                    settings = JsonSerializer.Deserialize<Settings>(text, ExternalAPIs.JsonDefaults.Options);

                return text;
            }
        }

        private static Settings CreateDefaultSettings()
        {
            var defaults = new Settings()
            {
                SettingsVersion = Settings.CurrentSettingsVersion,
                CheckForNewVersions = true,
                MinimumEventViewers = Settings.DefaultMinimumPopularEventViewers,
            };

            string chatCommandFilePath;
            string chatCommandFileArgs;
            if (File.Exists(Settings.DefaultChromeFullPath))
            {
                chatCommandFilePath = Settings.DefaultChromeFullPath;
                chatCommandFileArgs = Settings.DefaultChromeArgs;
            }
            else
            {
                chatCommandFilePath = Settings.DefaultEdgePath;
                chatCommandFileArgs = Settings.UrlReplacementToken;
            }

            defaults.Twitch.ChatCommand.FilePath = chatCommandFilePath;
            defaults.Twitch.ChatCommand.Args = chatCommandFileArgs;
            defaults.Kick.ChatCommand.FilePath = chatCommandFilePath;
            defaults.Kick.ChatCommand.Args = chatCommandFileArgs;
            defaults.YouTube.ChatCommand.FilePath = chatCommandFilePath;
            defaults.YouTube.ChatCommand.Args = chatCommandFileArgs;

            return defaults;
        }

        /// <param name="fullSettingsRaw">If we changed the format we need the raw text to convert settings</param>
        /// <param name="saveSettings">Current flag to passthrough if we don't change anything</param>
        private bool MigrateSettingsVersion(string fullSettingsRaw, bool saveSettings)
        {
            if (settings.SettingsVersion >= Settings.CurrentSettingsVersion) return saveSettings;

            // backup the current version
            File.WriteAllText($"{SettingsFileName}.v{settings.SettingsVersion}.beforemigrate", fullSettingsRaw);

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
                        var documentOptions = new JsonDocumentOptions
                        {
                            AllowTrailingCommas = ExternalAPIs.JsonDefaults.Options.AllowTrailingCommas,
                            CommentHandling = ExternalAPIs.JsonDefaults.Options.ReadCommentHandling,
                        };
                        var json = JsonNode.Parse(fullSettingsRaw, documentOptions: documentOptions);
                        bool shouldPassthrough = json["PassthroughClientId"]?.GetValue<bool>() ?? false;
                        string authToken = json["TwitchAuthToken"]?.GetValue<string>();
                        string chatCommandLine = json["ChatCommandLine"]?.GetValue<string>();

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

                            var filePath = chatCommandLine.Substring(0, endOfFilePathIndex).Trim().Trim('"');
                            var args = chatCommandLine.Substring(endOfFilePathIndex).Trim().Trim('"');

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
            if (e.PropertyName != nameof(settings.MetroThemeBaseColour) &&
                e.PropertyName != nameof(settings.MetroThemeAccentColour) &&
                e.PropertyName != nameof(settings.ShowOnlineOnly))
            {
                return;
            }

            SaveSettings();
        }
    }
}
