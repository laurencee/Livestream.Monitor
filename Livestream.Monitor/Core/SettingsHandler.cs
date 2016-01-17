using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using MahApps.Metro;
using Newtonsoft.Json;

namespace Livestream.Monitor.Core
{
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

        private void LoadSettings()
        {
            if (settingsLoaded) return;
            try
            {
                bool saveSettings = false;
                if (!File.Exists(SettingsFileName))
                {
                    settings = new Settings();
                    saveSettings = true;
                }
                else
                    settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(SettingsFileName));

                // guards against the paths being set to null/empty values
                if (settings.ChromeFullPath == null)
                {
                    settings.ChromeFullPath = Settings.DEFAULT_CHROME_FULL_PATH;
                    saveSettings = true;
                }

                if (string.IsNullOrWhiteSpace(settings.LivestreamerFullPath))
                {
                    settings.LivestreamerFullPath = Settings.DEFAULT_LIVESTREAMER_FULL_PATH;
                    saveSettings = true;
                }
                
                if (saveSettings) SaveSettings();

                settings.PropertyChanged += SettingsOnPropertyChanged;
                settings.ExcludeFromNotifying.CollectionChanged += (sender, args) => SaveSettings();
                settingsLoaded = true;
            }
            catch (Exception)
            {
                settings = new Settings();
                // log error
            }
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

        public void SaveSettings()
        {
            try
            {
                File.WriteAllText(SettingsFileName, JsonConvert.SerializeObject(settings));
            }
            catch (Exception)
            {
                // can't do much...
            }
        }
    }
}
