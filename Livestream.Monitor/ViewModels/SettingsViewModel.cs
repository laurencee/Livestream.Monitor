using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Microsoft.Win32;

namespace Livestream.Monitor.ViewModels
{
    public class SettingsViewModel : Screen, INotifyDataErrorInfo
    {
        private readonly Dictionary<string, List<string>> errors = new();
        private readonly ISettingsHandler settingsHandler;
        private int minimumEventViewers;
        private bool disableNotifications, hideStreamOutputOnLoad, passthroughClientId, checkForNewVersions, disableRefreshErrorDialogs;

        public SettingsViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");

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
                return MinimumEventViewers != settingsHandler.Settings.MinimumEventViewers ||
                       DisableNotifications != settingsHandler.Settings.DisableNotifications ||
                       HideStreamOutputOnLoad != settingsHandler.Settings.HideStreamOutputMessageBoxOnLoad ||
                       PassthroughClientId != settingsHandler.Settings.Twitch.PassthroughClientId ||
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

        public async Task OpenSettings()
        {
            try
            {
                Process.Start(SettingsHandler.SettingsFileName);
            }
            catch (Exception e)
            {
                await this.ShowMessageAsync("Error",
                    $"Failed to open settings file '{SettingsHandler.SettingsFileName}' {Environment.NewLine}{e}");
            }
        }

        public void Save()
        {
            if (!CanSave) return;

            settingsHandler.Settings.PropertyChanged -= SettingsOnPropertyChanged;

            settingsHandler.Settings.MinimumEventViewers = MinimumEventViewers;
            settingsHandler.Settings.DisableNotifications = DisableNotifications;
            settingsHandler.Settings.HideStreamOutputMessageBoxOnLoad = HideStreamOutputOnLoad;
            settingsHandler.Settings.Twitch.PassthroughClientId = PassthroughClientId;
            settingsHandler.Settings.CheckForNewVersions = CheckForNewVersions;
            settingsHandler.Settings.DisableRefreshErrorDialogs = DisableRefreshErrorDialogs;
            settingsHandler.SaveSettings();

            settingsHandler.Settings.PropertyChanged += SettingsOnPropertyChanged;

            NotifyOfPropertyChange(() => CanSave);
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
            MinimumEventViewers = settingsHandler.Settings.MinimumEventViewers;
            DisableNotifications = settingsHandler.Settings.DisableNotifications;
            HideStreamOutputOnLoad = settingsHandler.Settings.HideStreamOutputMessageBoxOnLoad;
            PassthroughClientId = settingsHandler.Settings.Twitch.PassthroughClientId;
            CheckForNewVersions = settingsHandler.Settings.CheckForNewVersions;
            DisableRefreshErrorDialogs = settingsHandler.Settings.DisableRefreshErrorDialogs;

            settingsHandler.Settings.PropertyChanged += SettingsOnPropertyChanged;
            base.OnActivate();
        }

        private void SettingsOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.MinimumEventViewers))
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