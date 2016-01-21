using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Microsoft.Win32;

namespace Livestream.Monitor.ViewModels
{
    public class SettingsViewModel : Screen, INotifyDataErrorInfo
    {
        private readonly Dictionary<string, List<string>> errors = new Dictionary<string, List<string>>();
        private readonly ISettingsHandler settingsHandler;
        private string chromeFullPath;
        private string livestreamerFullPath;
        private int minimumEventViewers;
        private bool disableNotifications;

        public SettingsViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");

            LivestreamerFullPath = "Livestreamer path - design time";
            ChromeFullPath = "Chrome path - design time";
            MinimumEventViewers = 30000;
        }

        public SettingsViewModel(
            ISettingsHandler settingsHandler,
            ThemeSelectorViewModel themeSelectorViewModel)
        {
            if (settingsHandler == null) throw new ArgumentNullException(nameof(settingsHandler));
            if (themeSelectorViewModel == null) throw new ArgumentNullException(nameof(themeSelectorViewModel));

            this.settingsHandler = settingsHandler;
            ThemeSelector = themeSelectorViewModel;

            ThemeSelector.ActivateWith(this);
        }

        public ThemeSelectorViewModel ThemeSelector { get; set; }

        public string LivestreamerFullPath
        {
            get { return livestreamerFullPath; }
            set
            {
                if (value == livestreamerFullPath) return;

                if (string.IsNullOrWhiteSpace(value))
                    AddError(nameof(LivestreamerFullPath), "Livestreamer path must not be empty");
                else if (!File.Exists(value))
                    AddError(nameof(LivestreamerFullPath), "File not found");
                else
                    RemoveErrors(nameof(LivestreamerFullPath));

                livestreamerFullPath = value;
                NotifyOfPropertyChange(() => LivestreamerFullPath);
                NotifyOfPropertyChange(() => CanSave);
            }
        }

        public string ChromeFullPath
        {
            get { return chromeFullPath; }
            set
            {
                if (value == chromeFullPath) return;

                // Must allow chrome full path to be blank as it's an optional component for chat only
                if (!string.IsNullOrWhiteSpace(value) && !File.Exists(value))
                    AddError(nameof(ChromeFullPath), "File not found");
                else
                    RemoveErrors(nameof(ChromeFullPath));

                chromeFullPath = value;
                NotifyOfPropertyChange(() => ChromeFullPath);
                NotifyOfPropertyChange(() => CanSave);
            }
        }

        public int MinimumEventViewers
        {
            get { return minimumEventViewers; }
            set
            {
                if (value == minimumEventViewers) return;
                if (value < 0) value = 0;

                minimumEventViewers = value;
                NotifyOfPropertyChange(() => MinimumEventViewers);
                NotifyOfPropertyChange(() => CanSave);
            }
        }

        public bool DisableNotifications
        {
            get { return disableNotifications; }
            set
            {
                if (value == disableNotifications) return;
                disableNotifications = value;
                NotifyOfPropertyChange(() => DisableNotifications);
                NotifyOfPropertyChange(() => CanSave);
            }
        }

        public bool CanSave
        {
            get
            {
                if (string.IsNullOrWhiteSpace(LivestreamerFullPath)) return false;
                // Must allow chrome full path to be blank as it's an optional component for chat only
                if (!string.IsNullOrWhiteSpace(ChromeFullPath) && !File.Exists(ChromeFullPath)) return false;
                if (!File.Exists(LivestreamerFullPath)) return false;

                return ChromeFullPath != settingsHandler.Settings.ChromeFullPath ||
                       LivestreamerFullPath != settingsHandler.Settings.LivestreamerFullPath ||
                       MinimumEventViewers != settingsHandler.Settings.MinimumEventViewers ||
                       DisableNotifications != settingsHandler.Settings.DisableNotifications;
            }
        }

        public IEnumerable GetErrors(string propertyName)
        {
            List<string> fails;
            if (errors.TryGetValue(propertyName, out fails))
                return fails;

            return Enumerable.Empty<string>();
        }

        public bool HasErrors => errors.Any();

        public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;

        public void Save()
        {
            if (!CanSave) return;

            settingsHandler.Settings.PropertyChanged -= SettingsOnPropertyChanged;

            settingsHandler.Settings.ChromeFullPath = ChromeFullPath;
            settingsHandler.Settings.LivestreamerFullPath = LivestreamerFullPath;
            settingsHandler.Settings.MinimumEventViewers = MinimumEventViewers;
            settingsHandler.Settings.DisableNotifications = DisableNotifications;
            settingsHandler.SaveSettings();

            settingsHandler.Settings.PropertyChanged += SettingsOnPropertyChanged;

            NotifyOfPropertyChange(() => CanSave);
        }

        public void SetLivestreamerFilePath()
        {
            var startingPath = settingsHandler.Settings.LivestreamerFullPath;
            if (string.IsNullOrWhiteSpace(startingPath))
                startingPath = Settings.DEFAULT_LIVESTREAMER_FULL_PATH;

            var livestreamerFilePath = SelectFile("Livestreamer|livestreamer.exe", startingPath);
            if (!string.IsNullOrWhiteSpace(livestreamerFilePath))
            {
                LivestreamerFullPath = livestreamerFilePath;
            }
        }

        public void SetChromeFilePath()
        {
            var startingPath = settingsHandler.Settings.ChromeFullPath;
            if (string.IsNullOrWhiteSpace(startingPath))
                startingPath = Settings.DEFAULT_CHROME_FULL_PATH;

            var chromeFilePath = SelectFile("Chrome|chrome.exe", startingPath);
            if (!string.IsNullOrWhiteSpace(chromeFilePath))
            {
                ChromeFullPath = chromeFilePath;
            }
        }

        private string SelectFile(string filter, string startingPath)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            if (startingPath == null) throw new ArgumentNullException(nameof(startingPath));

            var openFileDialog = new OpenFileDialog { Filter = filter };

            var directoryInfo = new DirectoryInfo(Path.GetDirectoryName(startingPath));
            while (directoryInfo.Parent != null && !directoryInfo.Exists)
            {
                directoryInfo = directoryInfo.Parent;
            }

            openFileDialog.InitialDirectory = directoryInfo.FullName;
            var showDialog = openFileDialog.ShowDialog();

            return showDialog == true ? openFileDialog.FileName : null;
        }

        protected override void OnActivate()
        {
            // We need to keep these as isolated properties so we can determine if a valid change has been made 
            LivestreamerFullPath = settingsHandler.Settings.LivestreamerFullPath;
            ChromeFullPath = settingsHandler.Settings.ChromeFullPath;
            MinimumEventViewers = settingsHandler.Settings.MinimumEventViewers;
            DisableNotifications = settingsHandler.Settings.DisableNotifications;

            settingsHandler.Settings.PropertyChanged += SettingsOnPropertyChanged;
            base.OnActivate();
        }

        private void SettingsOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.LivestreamerFullPath))
                LivestreamerFullPath = settingsHandler.Settings.LivestreamerFullPath;
            else if (e.PropertyName == nameof(Settings.ChromeFullPath))
                ChromeFullPath = settingsHandler.Settings.ChromeFullPath;
            else if (e.PropertyName == nameof(Settings.MinimumEventViewers))
                MinimumEventViewers = settingsHandler.Settings.MinimumEventViewers;
        }

        private void AddError(string propertyName, string error)
        {
            if (!errors.ContainsKey(propertyName))
                errors[propertyName] = new List<string>();

            if (!errors[propertyName].Contains(error))
            {
                errors[propertyName].Add(error);
                OnErrorsChanged(propertyName);
            }
        }

        private void RemoveErrors(string propertyName)
        {
            if (!errors.ContainsKey(propertyName)) return;

            var propertyErrors = errors[propertyName].ToList();
            foreach (var error in propertyErrors)
            {
                RemoveError(propertyName, error);
            }
        }

        private void RemoveError(string propertyName, string error)
        {
            if (errors.ContainsKey(propertyName) &&
                errors[propertyName].Contains(error))
            {
                errors[propertyName].Remove(error);
                if (errors[propertyName].Count == 0) errors.Remove(propertyName);
                OnErrorsChanged(propertyName);
            }
        }

        protected virtual void OnErrorsChanged(string propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
                throw new ArgumentNullException(nameof(propertyName));

            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        }
    }
}