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
        private string livestreamerFullPath, chatCommandLine;
        private int minimumEventViewers;
        private bool disableNotifications, hideStreamOutputOnLoad, passthroughClientId, checkForNewVersions, disableRefreshErrorDialogs;

        public SettingsViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");

            LivestreamerFullPath = "Livestreamer/Streamlink path - design time";
            ChatCommandLine = "Chat command - design time";
            MinimumEventViewers = 30000;
        }

        public SettingsViewModel(
            ISettingsHandler settingsHandler,
            ThemeSelectorViewModel themeSelectorViewModel)
        {
            this.settingsHandler = settingsHandler ?? throw new ArgumentNullException(nameof(settingsHandler));
            ThemeSelector = themeSelectorViewModel ?? throw new ArgumentNullException(nameof(themeSelectorViewModel));

            ThemeSelector.ActivateWith(this);
        }

        public ThemeSelectorViewModel ThemeSelector { get; set; }

        public string LivestreamerFullPath
        {
            get => livestreamerFullPath;
            set
            {
                if (value == livestreamerFullPath) return;

                if (string.IsNullOrWhiteSpace(value))
                    AddError(nameof(LivestreamerFullPath), "Livestreamer/Streamlink path must not be empty");
                else if (!File.Exists(value))
                    AddError(nameof(LivestreamerFullPath), "File not found");
                else
                    RemoveErrors(nameof(LivestreamerFullPath));

                livestreamerFullPath = value;
                NotifyOfPropertyChange(() => LivestreamerFullPath);
                NotifyOfPropertyChange(() => CanSave);
            }
        }

        public string ChatCommandLine
        {
            get => chatCommandLine;
            set
            {
                if (value == chatCommandLine) return;

                if (!string.IsNullOrEmpty(value) && !value.Contains(Settings.CHAT_URL_REPLACEMENT_TOKEN))
                    AddError(nameof(ChatCommandLine),
                        $"Chat command line must include a {Settings.CHAT_URL_REPLACEMENT_TOKEN} token so the chat url can be passed to the command");
                else
                    RemoveErrors(nameof(ChatCommandLine));

                chatCommandLine = value;
                NotifyOfPropertyChange(() => ChatCommandLine);
                NotifyOfPropertyChange(() => CanSave);
            }
        }

        public int MinimumEventViewers
        {
            get => minimumEventViewers;
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
            get => disableNotifications;
            set
            {
                if (value == disableNotifications) return;
                disableNotifications = value;
                NotifyOfPropertyChange(() => DisableNotifications);
                NotifyOfPropertyChange(() => CanSave);
            }
        }

        public bool HideStreamOutputOnLoad
        {
            get => hideStreamOutputOnLoad;
            set
            {
                if (value == hideStreamOutputOnLoad) return;
                hideStreamOutputOnLoad = value;
                NotifyOfPropertyChange(() => HideStreamOutputOnLoad);
                NotifyOfPropertyChange(() => CanSave);
            }
        }

        public bool PassthroughClientId
        {
            get => passthroughClientId;
            set
            {
                if (value == passthroughClientId) return;
                passthroughClientId = value;
                NotifyOfPropertyChange(() => PassthroughClientId);
                NotifyOfPropertyChange(() => CanSave);
            }
        }

        public bool CheckForNewVersions
        {
            get => checkForNewVersions;
            set
            {
                if (value == checkForNewVersions) return;
                checkForNewVersions = value;
                NotifyOfPropertyChange(() => CheckForNewVersions);
                NotifyOfPropertyChange(() => CanSave);
            }
        }

        public bool DisableRefreshErrorDialogs
        {
            get => disableRefreshErrorDialogs;
            set
            {
                if (value == disableRefreshErrorDialogs) return;
                disableRefreshErrorDialogs = value;
                NotifyOfPropertyChange(() => DisableRefreshErrorDialogs);
                NotifyOfPropertyChange(() => CanSave);
            }
        }

        public bool CanSave
        {
            get
            {
                if (string.IsNullOrWhiteSpace(LivestreamerFullPath)) return false;
                if (!string.IsNullOrEmpty(ChatCommandLine) &&
                    !ChatCommandLine.Contains(Settings.CHAT_URL_REPLACEMENT_TOKEN)) return false;
                if (!File.Exists(LivestreamerFullPath)) return false;

                return ChatCommandLine != settingsHandler.Settings.ChatCommandLine ||
                       LivestreamerFullPath != settingsHandler.Settings.LivestreamerFullPath ||
                       MinimumEventViewers != settingsHandler.Settings.MinimumEventViewers ||
                       DisableNotifications != settingsHandler.Settings.DisableNotifications ||
                       HideStreamOutputOnLoad != settingsHandler.Settings.HideStreamOutputMessageBoxOnLoad ||
                       PassthroughClientId != settingsHandler.Settings.PassthroughClientId ||
                       CheckForNewVersions != settingsHandler.Settings.CheckForNewVersions ||
                       DisableRefreshErrorDialogs != settingsHandler.Settings.DisableRefreshErrorDialogs;
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

            settingsHandler.Settings.ChatCommandLine = ChatCommandLine;
            settingsHandler.Settings.LivestreamerFullPath = LivestreamerFullPath;
            settingsHandler.Settings.MinimumEventViewers = MinimumEventViewers;
            settingsHandler.Settings.DisableNotifications = DisableNotifications;
            settingsHandler.Settings.HideStreamOutputMessageBoxOnLoad = HideStreamOutputOnLoad;
            settingsHandler.Settings.PassthroughClientId = PassthroughClientId;
            settingsHandler.Settings.CheckForNewVersions = CheckForNewVersions;
            settingsHandler.Settings.DisableRefreshErrorDialogs = DisableRefreshErrorDialogs;
            settingsHandler.SaveSettings();

            settingsHandler.Settings.PropertyChanged += SettingsOnPropertyChanged;

            NotifyOfPropertyChange(() => CanSave);
        }

        public void SetLivestreamerFilePath()
        {
            var startingPath = settingsHandler.Settings.LivestreamerFullPath;
            if (string.IsNullOrWhiteSpace(startingPath))
                startingPath = Settings.DEFAULT_STREAMLINK_FULL_PATH;

            var livestreamerFilePath = SelectFile("Streamlink|streamlink.exe|Livestreamer|livestreamer.exe", startingPath);
            if (!string.IsNullOrWhiteSpace(livestreamerFilePath))
            {
                LivestreamerFullPath = livestreamerFilePath;
            }
        }

        public void Chrome()
        {
            var startingPath = Settings.DEFAULT_CHROME_FULL_PATH;

            var chromeFilePath = SelectFile("Chrome|*.exe", startingPath);
            if (!string.IsNullOrWhiteSpace(chromeFilePath))
            {
                ChatCommandLine = $"\"{chromeFilePath}\" {Settings.CHROME_ARGS}";
            }
        }

        public void Edge()
        {
            ChatCommandLine = $"start microsoft-edge:{Settings.CHAT_URL_REPLACEMENT_TOKEN}";
        }

        public void Firefox()
        {
            var startingPath = Settings.DEFAULT_FIREFOX_FULL_PATH;

            var firefoxFilePath = SelectFile("Firefox|*.exe", startingPath);
            if (!string.IsNullOrWhiteSpace(firefoxFilePath))
            {
                ChatCommandLine = $"\"{firefoxFilePath}\" {Settings.FIREFOX_ARGS}";
            }
        }

        private string SelectFile(string filter, string startingPath)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            if (startingPath == null) throw new ArgumentNullException(nameof(startingPath));

            var openFileDialog = new OpenFileDialog { Filter = filter };

            string initialDir = null;
            try
            {
                var directoryInfo = new DirectoryInfo(Path.GetDirectoryName(startingPath));
                while (directoryInfo.Parent != null && !directoryInfo.Exists)
                {
                    directoryInfo = directoryInfo.Parent;
                }
                initialDir = directoryInfo.FullName;
            }
            // most likely invalid path defined as starting path, can't do anything about that
            catch
            {
            }

            if (initialDir != null) openFileDialog.InitialDirectory = initialDir;
            var showDialog = openFileDialog.ShowDialog();

            return showDialog == true ? openFileDialog.FileName : null;
        }

        protected override void OnActivate()
        {
            // We need to keep these as isolated properties so we can determine if a valid change has been made 
            LivestreamerFullPath = settingsHandler.Settings.LivestreamerFullPath;
            ChatCommandLine = settingsHandler.Settings.ChatCommandLine;
            MinimumEventViewers = settingsHandler.Settings.MinimumEventViewers;
            DisableNotifications = settingsHandler.Settings.DisableNotifications;
            HideStreamOutputOnLoad = settingsHandler.Settings.HideStreamOutputMessageBoxOnLoad;
            PassthroughClientId = settingsHandler.Settings.PassthroughClientId;
            CheckForNewVersions = settingsHandler.Settings.CheckForNewVersions;
            DisableRefreshErrorDialogs = settingsHandler.Settings.DisableRefreshErrorDialogs;

            settingsHandler.Settings.PropertyChanged += SettingsOnPropertyChanged;
            base.OnActivate();
        }

        private void SettingsOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.LivestreamerFullPath))
                LivestreamerFullPath = settingsHandler.Settings.LivestreamerFullPath;
            else if (e.PropertyName == nameof(Settings.ChatCommandLine))
                ChatCommandLine = settingsHandler.Settings.ChatCommandLine;
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