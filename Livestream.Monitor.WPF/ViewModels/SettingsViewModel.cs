using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Caliburn.Micro;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Core.UI;
using Livestream.Monitor.Core.Utility;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.WPF.Core;
using Livestream.Monitor.WPF.Core.UI;
using Livestream.Monitor.WPF.Views;

namespace Livestream.Monitor.WPF.ViewModels
{
    public class SettingsViewModel : Screen
    {
        private readonly ISettingsHandler settingsHandler;
        private readonly IApiClientFactory apiClientFactory;
        private TwitchSettings observedTwitch;

        public SettingsViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");

            ThemeSelector = new ThemeSelectorViewModel();
            StreamQualities = new ApiClientsQualitiesViewModel();
            Twitch = new TwitchSettingsEditor();
            Kick = new ApiPlatformSettingsEditor("Kick");
            YouTube = new ApiPlatformSettingsEditor("YouTube");

            ViewerThresholdText = "30000";
            Twitch.LoadFrom(new TwitchSettings());
            Kick.LoadFrom(new KickSettings());
            YouTube.LoadFrom(new YouTubeSettings());
        }

        public SettingsViewModel(
            ISettingsHandler settingsHandler,
            ThemeSelectorViewModel themeSelectorViewModel,
            IApiClientFactory apiClientFactory)
        {
            this.settingsHandler = settingsHandler ?? throw new ArgumentNullException(nameof(settingsHandler));
            ThemeSelector = themeSelectorViewModel ?? throw new ArgumentNullException(nameof(themeSelectorViewModel));
            this.apiClientFactory = apiClientFactory ?? throw new ArgumentNullException(nameof(apiClientFactory));

            Twitch = new TwitchSettingsEditor();
            Kick = new ApiPlatformSettingsEditor("Kick");
            YouTube = new ApiPlatformSettingsEditor("YouTube");

            StreamQualities = new ApiClientsQualitiesViewModel(settingsHandler.Settings);
            StreamQualities.PropertyChanged += (_, _) => NotifyOfPropertyChange(() => CanSave);
            ThemeSelector.ActivateWith(this);
        }

        public ThemeSelectorViewModel ThemeSelector { get; }

        public ApiClientsQualitiesViewModel StreamQualities { get; }

        public TwitchSettingsEditor Twitch { get; }

        public ApiPlatformSettingsEditor Kick { get; }

        public ApiPlatformSettingsEditor YouTube { get; }

        public string ViewerThresholdText
        {
            get;
            set
            {
                if (Set(ref field, value)) NotifyOfPropertyChange(() => ViewerThresholdError);
            }
        }

        public string ViewerThresholdError => TryReadViewerThreshold(out _) ? "" : "Enter a viewer threshold, or 0 to turn it off.";

        public bool DisableNotifications
        {
            get => settingsHandler?.Settings.DisableNotifications ?? false;
            set
            {
                if (value != DisableNotifications) SavePreference(settings => settings.DisableNotifications = value);
            }
        }

        public bool EnableCategoryChangeNotifications
        {
            get => settingsHandler?.Settings.EnableCategoryChangeNotifications ?? false;
            set
            {
                if (value != EnableCategoryChangeNotifications) SavePreference(settings => settings.EnableCategoryChangeNotifications = value);
            }
        }

        private StreamNotificationImage StreamNotificationImage =>
            settingsHandler?.Settings.StreamNotificationImage ?? StreamNotificationImage.ChannelIcon;

        public bool ChannelIconSelected
        {
            get => StreamNotificationImage == StreamNotificationImage.ChannelIcon;
            set
            {
                if (value && !ChannelIconSelected)
                    SavePreference(settings => settings.StreamNotificationImage = StreamNotificationImage.ChannelIcon);
            }
        }

        public bool LiveThumbnailSelected
        {
            get => StreamNotificationImage == StreamNotificationImage.LiveThumbnail;
            set
            {
                if (value && !LiveThumbnailSelected)
                    SavePreference(settings => settings.StreamNotificationImage = StreamNotificationImage.LiveThumbnail);
            }
        }

        public bool HideStreamOutputOnLoad
        {
            get => settingsHandler?.Settings.HideStreamOutputMessageBoxOnLoad ?? false;
            set
            {
                if (value != HideStreamOutputOnLoad) SavePreference(settings => settings.HideStreamOutputMessageBoxOnLoad = value);
            }
        }

        public bool CheckForNewVersions
        {
            get => settingsHandler?.Settings.CheckForNewVersions ?? true;
            set
            {
                if (value != CheckForNewVersions) SavePreference(settings => settings.CheckForNewVersions = value);
            }
        }

        public bool DisableRefreshErrorDialogs
        {
            get => settingsHandler?.Settings.DisableRefreshErrorDialogs ?? false;
            set
            {
                if (value != DisableRefreshErrorDialogs) SavePreference(settings => settings.DisableRefreshErrorDialogs = value);
            }
        }

        public bool DisableMinimizeToTrayNotification
        {
            get => settingsHandler?.Settings.DisableMinimizeToTrayNotification ?? false;
            set
            {
                if (value != DisableMinimizeToTrayNotification) SavePreference(settings => settings.DisableMinimizeToTrayNotification = value);
            }
        }

        public bool CanSave => StreamQualities.HasChanges;
        public bool IsDarkTheme => settingsHandler?.Settings.MetroThemeBaseColour == MetroThemeBaseColour.BaseDark;

        public void CommitViewerThreshold()
        {
            if (settingsHandler == null || !TryReadViewerThreshold(out var minimum)) return;
            if (settingsHandler.Settings.MinimumEventViewers == minimum) return;

            SavePreference(settings => settings.MinimumEventViewers = minimum);
        }

        public bool RevealPendingPlatform()
        {
            Twitch.SyncAuthToken();
            var pending = Twitch.HasChanges ? Twitch : Kick.HasChanges ? Kick : YouTube.HasChanges ? YouTube : null;
            if (pending == null) return false;

            pending.RefreshPathStates();
            if (GetView() is SettingsView view) view.RevealPlatformActions(pending);
            return true;
        }

        public async Task<bool> ConfirmDiscardQualities()
        {
            var dialogs = new WpfDialogs(this);
            var discard = await dialogs.Confirm(
                title: "Unsaved quality changes",
                message: "Discard quality changes and close Settings?",
                affirmative: "Discard",
                negative: "Keep editing");
            if (discard) StreamQualities.Reload();

            return discard;
        }

        public async Task OpenSettings()
        {
            try
            {
                Process.Start(new ProcessStartInfo(SettingsHandler.SettingsFileName) { UseShellExecute = true });
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

            StreamQualities.SaveChanges();
            settingsHandler.SaveSettings();
            NotifyOfPropertyChange(() => CanSave);
        }

        public async Task SavePlatform(ApiPlatformSettingsEditor editor)
        {
            if (editor == Twitch) Twitch.SyncAuthToken();
            if (!editor.HasChanges) return;

            editor.RefreshPathStates();
            if (!editor.CanSave) return;

            var updated = editor switch
            {
                _ when editor == Twitch => (ApiPlatformSettings)new TwitchSettings(),
                _ when editor == Kick => new KickSettings(),
                _ when editor == YouTube => new YouTubeSettings(),
                _ => throw new ArgumentOutOfRangeException(nameof(editor)),
            };

            editor.CopyTo(updated);

            var authChanged = updated is TwitchSettings twitch &&
                twitch.AuthToken != settingsHandler.Settings.Twitch?.AuthToken;

            if (!settingsHandler.TrySavePlatformSettings(updated))
            {
                await this.ShowMessageAsync("Unable to save settings", "Check that settings.json is writable and try again.");
                return;
            }

            editor.LoadFrom(updated);
            if (authChanged) await apiClientFactory.Get<TwitchApiClient>().Initialize();
        }

        public void LoadDrafts()
        {
            if (settingsHandler == null) return;

            var settings = settingsHandler.Settings;
            Twitch.LoadFrom(settings.Twitch ?? new TwitchSettings());
            ObserveTwitch(settings.Twitch);
            Kick.LoadFrom(settings.Kick ?? new KickSettings());
            YouTube.LoadFrom(settings.YouTube ?? new YouTubeSettings());
            ViewerThresholdText = settings.MinimumEventViewers.ToString(CultureInfo.InvariantCulture);
        }

        protected override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            if (settingsHandler == null)
            {
                await base.OnActivateAsync(cancellationToken);
                return;
            }

            StreamQualities.Reload();
            LoadDrafts();
            settingsHandler.Settings.PropertyChanged += SettingsOnPropertyChanged;
            await base.OnActivateAsync(cancellationToken);
        }

        protected override async Task OnDeactivateAsync(bool close, CancellationToken cancellationToken)
        {
            if (settingsHandler != null)
                settingsHandler.Settings.PropertyChanged -= SettingsOnPropertyChanged;

            ObserveTwitch(null);
            await base.OnDeactivateAsync(close, cancellationToken);
        }

        private bool TryReadViewerThreshold(out int minimum) =>
            int.TryParse(ViewerThresholdText, NumberStyles.None, CultureInfo.InvariantCulture, out minimum);

        private void SavePreference(Action<Settings> change)
        {
            if (settingsHandler == null) return;

            change(settingsHandler.Settings);
            settingsHandler.SaveSettings();
        }

        private void ObserveTwitch(TwitchSettings settings)
        {
            if (ReferenceEquals(observedTwitch, settings)) return;

            if (observedTwitch != null) observedTwitch.PropertyChanged -= TwitchOnPropertyChanged;
            observedTwitch = settings;
            if (observedTwitch != null) observedTwitch.PropertyChanged += TwitchOnPropertyChanged;
        }

        private void TwitchOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(TwitchSettings.AuthToken) || Application.Current == null) return;

            _ = Application.Current.Dispatcher.InvokeAsync(Twitch.SyncAuthToken);
        }

        private void SettingsOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(Settings.Twitch):
                    ObserveTwitch(settingsHandler.Settings.Twitch);
                    break;
                case nameof(Settings.MetroThemeBaseColour):
                    NotifyOfPropertyChange(() => IsDarkTheme);
                    break;
                case nameof(Settings.MinimumEventViewers):
                    ViewerThresholdText = settingsHandler.Settings.MinimumEventViewers.ToString(CultureInfo.InvariantCulture);
                    break;
                case nameof(Settings.StreamNotificationImage):
                    NotifyOfPropertyChange(() => ChannelIconSelected);
                    NotifyOfPropertyChange(() => LiveThumbnailSelected);
                    break;
                case nameof(Settings.HideStreamOutputMessageBoxOnLoad):
                    NotifyOfPropertyChange(() => HideStreamOutputOnLoad);
                    break;
                case nameof(Settings.DisableNotifications):
                case nameof(Settings.EnableCategoryChangeNotifications):
                case nameof(Settings.CheckForNewVersions):
                case nameof(Settings.DisableRefreshErrorDialogs):
                case nameof(Settings.DisableMinimizeToTrayNotification):
                    NotifyOfPropertyChange(e.PropertyName);
                    break;
            }
        }
    }

    public class ApiPlatformSettingsEditor : PropertyChangedBase
    {
        protected ApiPlatformSettings Saved { get; private set; }

        public ApiPlatformSettingsEditor(string displayName)
        {
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            StreamCommand.PropertyChanged += CommandOnPropertyChanged;
            VodCommand.PropertyChanged += CommandOnPropertyChanged;
            ChatCommand.PropertyChanged += CommandOnPropertyChanged;
        }

        public string DisplayName { get; }
        public string SectionTitle => HasChanges ? $"{DisplayName} (unsaved)" : DisplayName;
        public ExecCommandEditor StreamCommand { get; } = new("Stream command");
        public ExecCommandEditor VodCommand { get; } = new("VOD command");
        public ExecCommandEditor ChatCommand { get; } = new("Chat command");
        public bool HasErrors => StreamCommand.HasErrors || VodCommand.HasErrors || ChatCommand.HasErrors;
        public bool HasChanges => Saved != null && !Matches(Saved);
        public bool CanSave => HasChanges && !HasErrors;

        public string StatusText => !HasChanges ? $"{DisplayName} settings saved." :
            HasErrors ? $"{DisplayName} has an invalid command path. Fix it above or revert." : $"{DisplayName} has unsaved changes.";

        public void LoadFrom(ApiPlatformSettings settings)
        {
            Saved = settings ?? throw new ArgumentNullException(nameof(settings));
            ReadFields(settings);
            RefreshPathStates();
            NotifyDraftChanged();
        }

        public void Revert()
        {
            if (HasChanges) LoadFrom(Saved);
        }

        protected virtual void ReadFields(ApiPlatformSettings settings)
        {
            StreamCommand.LoadFrom(settings.StreamCommand ?? new ExecCommand());
            VodCommand.LoadFrom(settings.VodCommand ?? new ExecCommand());
            ChatCommand.LoadFrom(settings.ChatCommand ?? new ExecCommand());
        }

        public virtual void CopyTo(ApiPlatformSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            StreamCommand.CopyTo(settings.StreamCommand ??= new ExecCommand());
            VodCommand.CopyTo(settings.VodCommand ??= new ExecCommand());
            ChatCommand.CopyTo(settings.ChatCommand ??= new ExecCommand());
        }

        protected virtual bool Matches(ApiPlatformSettings settings) =>
            StreamCommand.Matches(settings.StreamCommand) &&
            VodCommand.Matches(settings.VodCommand) &&
            ChatCommand.Matches(settings.ChatCommand);

        public void RefreshPathStates()
        {
            StreamCommand.RefreshFilePathState();
            VodCommand.RefreshFilePathState();
            ChatCommand.RefreshFilePathState();
        }

        protected void NotifyDraftChanged()
        {
            NotifyOfPropertyChange(() => HasChanges);
            NotifyOfPropertyChange(() => CanSave);
            NotifyOfPropertyChange(() => SectionTitle);
            NotifyOfPropertyChange(() => StatusText);
        }

        private void CommandOnPropertyChanged(object sender, PropertyChangedEventArgs e) => NotifyDraftChanged();
    }

    public sealed class TwitchSettingsEditor() : ApiPlatformSettingsEditor("Twitch")
    {
        private string savedAuthToken;

        public void SyncAuthToken()
        {
            if (Saved is not TwitchSettings twitch || savedAuthToken == twitch.AuthToken) return;

            // Browser authorization can update the saved token while Settings is open.
            var edited = (AuthToken ?? "") != (savedAuthToken ?? "");
            savedAuthToken = twitch.AuthToken;
            if (!edited) AuthToken = savedAuthToken;

            NotifyDraftChanged();
        }

        public string AuthToken
        {
            get;
            set
            {
                if (Set(ref field, value)) NotifyDraftChanged();
            }
        }

        public bool PassthroughClientId
        {
            get;
            set
            {
                if (Set(ref field, value)) NotifyDraftChanged();
            }
        }

        protected override void ReadFields(ApiPlatformSettings settings)
        {
            base.ReadFields(settings);
            var twitch = (TwitchSettings)settings;
            savedAuthToken = twitch.AuthToken;
            AuthToken = savedAuthToken;
            PassthroughClientId = twitch.PassthroughClientId;
        }

        public override void CopyTo(ApiPlatformSettings settings)
        {
            base.CopyTo(settings);
            var twitch = (TwitchSettings)settings;
            twitch.AuthToken = AuthToken;
            twitch.PassthroughClientId = PassthroughClientId;
        }

        protected override bool Matches(ApiPlatformSettings settings) =>
            base.Matches(settings) && settings is TwitchSettings twitch &&
            string.Equals(AuthToken ?? "", twitch.AuthToken ?? "", StringComparison.Ordinal) &&
            PassthroughClientId == twitch.PassthroughClientId;
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

        public bool Matches(ExecCommand command) =>
            string.Equals(FilePath ?? "", command?.FilePath ?? "", StringComparison.Ordinal) &&
            string.Equals(Args ?? "", command?.Args ?? "", StringComparison.Ordinal) &&
            CaptureStandardOutput == (command?.CaptureStandardOutput ?? false) &&
            CaptureErrorOutput == (command?.CaptureErrorOutput ?? false);

        public void RefreshFilePathState()
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
