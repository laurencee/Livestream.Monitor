using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.Utility;

namespace Livestream.Monitor.ViewModels
{
    public class SettingsViewModel : Screen
    {
        private readonly ISettingsHandler settingsHandler;

        public SettingsViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");

            ThemeSelector = new ThemeSelectorViewModel();
            Twitch = new TwitchSettingsEditor();
            Kick = new ApiPlatformSettingsEditor("Kick");
            YouTube = new ApiPlatformSettingsEditor("YouTube");

            HookPlatformEditors();

            MinimumEventViewers = 30000;
            CheckForNewVersions = true;
            Twitch.LoadFrom(new TwitchSettings());
            Kick.LoadFrom(new KickSettings());
            YouTube.LoadFrom(new YouTubeSettings());
        }

        public SettingsViewModel(
            ISettingsHandler settingsHandler,
            ThemeSelectorViewModel themeSelectorViewModel)
        {
            this.settingsHandler = settingsHandler ?? throw new ArgumentNullException(nameof(settingsHandler));
            ThemeSelector = themeSelectorViewModel ?? throw new ArgumentNullException(nameof(themeSelectorViewModel));

            Twitch = new TwitchSettingsEditor();
            Kick = new ApiPlatformSettingsEditor("Kick");
            YouTube = new ApiPlatformSettingsEditor("YouTube");

            HookPlatformEditors();
            ThemeSelector.ActivateWith(this);
        }

        public ThemeSelectorViewModel ThemeSelector { get; }

        public TwitchSettingsEditor Twitch { get; }

        public ApiPlatformSettingsEditor Kick { get; }

        public ApiPlatformSettingsEditor YouTube { get; }

        public int MinimumEventViewers
        {
            get;
            set
            {
                if (value == field) return;
                if (value < 0) value = 0;

                field = value;
                NotifyOfPropertyChange(() => MinimumEventViewers);
                NotifyOfPropertyChange(() => CanSave);
            }
        }

        public bool DisableNotifications
        {
            get;
            set
            {
                if (value == field) return;
                field = value;
                NotifyOfPropertyChange(() => DisableNotifications);
                NotifyOfPropertyChange(() => CanSave);
            }
        }

        public bool HideStreamOutputOnLoad
        {
            get;
            set
            {
                if (value == field) return;
                field = value;
                NotifyOfPropertyChange(() => HideStreamOutputOnLoad);
                NotifyOfPropertyChange(() => CanSave);
            }
        }

        public bool CheckForNewVersions
        {
            get;
            set
            {
                if (value == field) return;
                field = value;
                NotifyOfPropertyChange(() => CheckForNewVersions);
                NotifyOfPropertyChange(() => CanSave);
            }
        }

        public bool DisableRefreshErrorDialogs
        {
            get;
            set
            {
                if (value == field) return;
                field = value;
                NotifyOfPropertyChange(() => DisableRefreshErrorDialogs);
                NotifyOfPropertyChange(() => CanSave);
            }
        }

        public bool DisableMinimizeToTrayNotification
        {
            get;
            set
            {
                if (value == field) return;
                field = value;
                NotifyOfPropertyChange(() => DisableMinimizeToTrayNotification);
                NotifyOfPropertyChange(() => CanSave);
            }
        }

        public bool HasValidationErrors => Twitch.HasErrors || Kick.HasErrors || YouTube.HasErrors;

        public bool CanSave
        {
            get
            {
                if (settingsHandler == null || HasValidationErrors) return false;

                return MinimumEventViewers != settingsHandler.Settings.MinimumEventViewers ||
                       DisableNotifications != settingsHandler.Settings.DisableNotifications ||
                       HideStreamOutputOnLoad != settingsHandler.Settings.HideStreamOutputMessageBoxOnLoad ||
                       CheckForNewVersions != settingsHandler.Settings.CheckForNewVersions ||
                       DisableRefreshErrorDialogs != settingsHandler.Settings.DisableRefreshErrorDialogs ||
                       DisableMinimizeToTrayNotification != settingsHandler.Settings.DisableMinimizeToTrayNotification ||
                       !Twitch.Matches(settingsHandler.Settings.Twitch) ||
                       !Kick.Matches(settingsHandler.Settings.Kick) ||
                       !YouTube.Matches(settingsHandler.Settings.YouTube);
            }
        }

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

            if (settingsHandler.Settings.Twitch == null) settingsHandler.Settings.Twitch = new TwitchSettings();
            if (settingsHandler.Settings.Kick == null) settingsHandler.Settings.Kick = new KickSettings();
            if (settingsHandler.Settings.YouTube == null) settingsHandler.Settings.YouTube = new YouTubeSettings();

            settingsHandler.Settings.MinimumEventViewers = MinimumEventViewers;
            settingsHandler.Settings.DisableNotifications = DisableNotifications;
            settingsHandler.Settings.HideStreamOutputMessageBoxOnLoad = HideStreamOutputOnLoad;
            settingsHandler.Settings.CheckForNewVersions = CheckForNewVersions;
            settingsHandler.Settings.DisableRefreshErrorDialogs = DisableRefreshErrorDialogs;
            settingsHandler.Settings.DisableMinimizeToTrayNotification = DisableMinimizeToTrayNotification;

            Twitch.CopyTo(settingsHandler.Settings.Twitch);
            Kick.CopyTo(settingsHandler.Settings.Kick);
            YouTube.CopyTo(settingsHandler.Settings.YouTube);

            settingsHandler.SaveSettings();
            settingsHandler.Settings.PropertyChanged += SettingsOnPropertyChanged;

            NotifyOfPropertyChange(() => CanSave);
        }

        protected override void OnActivate()
        {
            if (settingsHandler == null)
            {
                base.OnActivate();
                return;
            }

            LoadFromSettings();
            settingsHandler.Settings.PropertyChanged += SettingsOnPropertyChanged;
            base.OnActivate();
        }

        protected override void OnDeactivate(bool close)
        {
            if (settingsHandler != null)
                settingsHandler.Settings.PropertyChanged -= SettingsOnPropertyChanged;

            base.OnDeactivate(close);
        }

        private void HookPlatformEditors()
        {
            HookPlatformEditor(Twitch);
            HookPlatformEditor(Kick);
            HookPlatformEditor(YouTube);
        }

        private void HookPlatformEditor(ApiPlatformSettingsEditor editor)
        {
            editor.PropertyChanged += ChildEditorOnPropertyChanged;
            editor.StreamCommand.PropertyChanged += ChildEditorOnPropertyChanged;
            editor.VodCommand.PropertyChanged += ChildEditorOnPropertyChanged;
            editor.ChatCommand.PropertyChanged += ChildEditorOnPropertyChanged;
        }

        private void ChildEditorOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            NotifyOfPropertyChange(() => HasValidationErrors);
            NotifyOfPropertyChange(() => CanSave);
        }

        private void LoadFromSettings()
        {
            var settings = settingsHandler.Settings;
            var twitchSettings = settings.Twitch ?? new TwitchSettings();
            var kickSettings = settings.Kick ?? new KickSettings();
            var youTubeSettings = settings.YouTube ?? new YouTubeSettings();

            MinimumEventViewers = settings.MinimumEventViewers;
            DisableNotifications = settings.DisableNotifications;
            HideStreamOutputOnLoad = settings.HideStreamOutputMessageBoxOnLoad;
            CheckForNewVersions = settings.CheckForNewVersions;
            DisableRefreshErrorDialogs = settings.DisableRefreshErrorDialogs;
            DisableMinimizeToTrayNotification = settings.DisableMinimizeToTrayNotification;

            Twitch.LoadFrom(twitchSettings);
            Kick.LoadFrom(kickSettings);
            YouTube.LoadFrom(youTubeSettings);

            NotifyOfPropertyChange(() => HasValidationErrors);
            NotifyOfPropertyChange(() => CanSave);
        }

        private void SettingsOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (settingsHandler == null) return;

            switch (e.PropertyName)
            {
                case nameof(Settings.MinimumEventViewers):
                    MinimumEventViewers = settingsHandler.Settings.MinimumEventViewers;
                    break;
                case nameof(Settings.DisableNotifications):
                    DisableNotifications = settingsHandler.Settings.DisableNotifications;
                    break;
                case nameof(Settings.HideStreamOutputMessageBoxOnLoad):
                    HideStreamOutputOnLoad = settingsHandler.Settings.HideStreamOutputMessageBoxOnLoad;
                    break;
                case nameof(Settings.CheckForNewVersions):
                    CheckForNewVersions = settingsHandler.Settings.CheckForNewVersions;
                    break;
                case nameof(Settings.DisableRefreshErrorDialogs):
                    DisableRefreshErrorDialogs = settingsHandler.Settings.DisableRefreshErrorDialogs;
                    break;
                case nameof(Settings.DisableMinimizeToTrayNotification):
                    DisableMinimizeToTrayNotification = settingsHandler.Settings.DisableMinimizeToTrayNotification;
                    break;
            }
        }
    }

    public class ApiPlatformSettingsEditor(string displayName) : PropertyChangedBase
    {
        public string DisplayName { get; } = displayName ?? throw new ArgumentNullException(nameof(displayName));

        public ExecCommandEditor StreamCommand { get; } = new("Stream command");

        public ExecCommandEditor VodCommand { get; } = new("VOD command");

        public ExecCommandEditor ChatCommand { get; } = new("Chat command");

        public virtual bool HasErrors => StreamCommand.HasErrors || VodCommand.HasErrors || ChatCommand.HasErrors;

        public virtual void LoadFrom(ApiPlatformSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            StreamCommand.LoadFrom(settings.StreamCommand ?? new ExecCommand());
            VodCommand.LoadFrom(settings.VodCommand ?? new ExecCommand());
            ChatCommand.LoadFrom(settings.ChatCommand ?? new ExecCommand());
        }

        public virtual void CopyTo(ApiPlatformSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            if (settings.StreamCommand == null) settings.StreamCommand = new ExecCommand();
            if (settings.VodCommand == null) settings.VodCommand = new ExecCommand();
            if (settings.ChatCommand == null) settings.ChatCommand = new ExecCommand();

            StreamCommand.CopyTo(settings.StreamCommand);
            VodCommand.CopyTo(settings.VodCommand);
            ChatCommand.CopyTo(settings.ChatCommand);
        }

        public virtual bool Matches(ApiPlatformSettings settings)
        {
            if (settings == null) return false;

            return StreamCommand.Matches(settings.StreamCommand) &&
                   VodCommand.Matches(settings.VodCommand) &&
                   ChatCommand.Matches(settings.ChatCommand);
        }
    }

    public sealed class TwitchSettingsEditor() : ApiPlatformSettingsEditor("Twitch")
    {
        public string AuthToken
        {
            get;
            set => Set(ref field, value);
        }

        public bool PassthroughClientId
        {
            get;
            set => Set(ref field, value);
        }

        public void LoadFrom(TwitchSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            base.LoadFrom(settings);
            AuthToken = settings.AuthToken;
            PassthroughClientId = settings.PassthroughClientId;
        }

        public override void LoadFrom(ApiPlatformSettings settings)
        {
            LoadFrom((TwitchSettings)settings);
        }

        public void CopyTo(TwitchSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            base.CopyTo(settings);
            settings.AuthToken = AuthToken;
            settings.PassthroughClientId = PassthroughClientId;
        }

        public override void CopyTo(ApiPlatformSettings settings)
        {
            CopyTo((TwitchSettings)settings);
        }

        public bool Matches(TwitchSettings settings)
        {
            if (settings == null) return false;

            return base.Matches(settings) &&
                   string.Equals(AuthToken, settings.AuthToken, StringComparison.Ordinal) &&
                   PassthroughClientId == settings.PassthroughClientId;
        }

        public override bool Matches(ApiPlatformSettings settings)
        {
            return Matches((TwitchSettings)settings);
        }
    }

    public sealed class ExecCommandEditor : PropertyChangedBase
    {
        public ExecCommandEditor(string displayName)
        {
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            RefreshFilePathState();
        }

        public string DisplayName { get; }

        public string FilePath
        {
            get;
            set
            {
                if (!Set(ref field, value)) return;
                RefreshFilePathState();
            }
        }

        public string Args
        {
            get;
            set => Set(ref field, value);
        }

        public bool CaptureStandardOutput
        {
            get;
            set => Set(ref field, value);
        }

        public bool CaptureErrorOutput
        {
            get;
            set => Set(ref field, value);
        }

        public string ResolvedFilePath
        {
            get;
            private set => Set(ref field, value);
        }

        public string FilePathStatusText
        {
            get;
            private set => Set(ref field, value);
        }

        public bool HasValidFilePath
        {
            get;
            private set
            {
                if (!Set(ref field, value)) return;
                NotifyOfPropertyChange(() => HasErrors);
            }
        }

        public bool HasErrors => !HasValidFilePath;

        public void LoadFrom(ExecCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));

            FilePath = command.FilePath;
            Args = command.Args;
            CaptureStandardOutput = command.CaptureStandardOutput;
            CaptureErrorOutput = command.CaptureErrorOutput;
        }

        public void CopyTo(ExecCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));

            command.FilePath = FilePath;
            command.Args = Args;
            command.CaptureStandardOutput = CaptureStandardOutput;
            command.CaptureErrorOutput = CaptureErrorOutput;
        }

        public bool Matches(ExecCommand command)
        {
            if (command == null) return false;

            return string.Equals(FilePath, command.FilePath, StringComparison.Ordinal) &&
                   string.Equals(Args, command.Args, StringComparison.Ordinal) &&
                   CaptureStandardOutput == command.CaptureStandardOutput &&
                   CaptureErrorOutput == command.CaptureErrorOutput;
        }

        private void RefreshFilePathState()
        {
            if (string.IsNullOrWhiteSpace(FilePath))
            {
                ResolvedFilePath = null;
                FilePathStatusText = "Enter a command name or full executable path.";
                HasValidFilePath = false;
                return;
            }

            if (WindowsCommandResolver.TryResolveExecutable(FilePath, out string resolvedPath))
            {
                ResolvedFilePath = resolvedPath;
                FilePathStatusText = $"Resolved: {resolvedPath}";
                HasValidFilePath = true;
                return;
            }

            ResolvedFilePath = null;
            FilePathStatusText = "Unable to resolve this command or executable path.";
            HasValidFilePath = false;
        }
    }
}
