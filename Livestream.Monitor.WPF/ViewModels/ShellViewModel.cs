using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Caliburn.Micro;
using ExternalAPIs.GitHub;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.Monitoring;
using Livestream.Monitor.WPF.Core;
using Livestream.Monitor.WPF.Core.UI;
using Livestream.Monitor.WPF.Views;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using Application = System.Windows.Application;
using INavigationService = Livestream.Monitor.WPF.Core.INavigationService;

namespace Livestream.Monitor.WPF.ViewModels
{
    public class ShellViewModel : Conductor<IScreen>.Collection.OneActive, IHandle<ActivateScreen>
    {
        private readonly MainViewModel mainViewModel;
        private readonly INavigationService navigationService;
        private readonly IMonitorStreamsModel monitorStreamsModel;
        private readonly ISettingsHandler settingsHandler;
        private readonly PopularLivestreamWatcher popularLivestreamWatcher;

        private readonly Version currentAppVersion;
        private TrayIconService taskbarIcon;
        private bool firstMinimize = true;

        public ShellViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");

            Settings = new SettingsViewModel();
            ActiveItem = new MainViewModel();
        }

        public ShellViewModel(
            SettingsViewModel settingsViewModel,
            MainViewModel mainViewModel,
            IEventAggregator eventAggregator,
            INavigationService navigationService,
            IMonitorStreamsModel monitorStreamsModel,
            ISettingsHandler settingsHandler,
            PopularLivestreamWatcher popularLivestreamWatcher)
        {
            Settings = settingsViewModel ?? throw new ArgumentNullException(nameof(settingsViewModel));
            this.mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
            this.navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            this.monitorStreamsModel = monitorStreamsModel ?? throw new ArgumentNullException(nameof(monitorStreamsModel));
            this.settingsHandler = settingsHandler ?? throw new ArgumentNullException(nameof(settingsHandler));
            this.popularLivestreamWatcher = popularLivestreamWatcher ?? throw new ArgumentNullException(nameof(popularLivestreamWatcher));

            ActiveItem = mainViewModel;

            eventAggregator.SubscribeOnUIThread(this);
            Settings.ActivateWith(this);

            currentAppVersion = GetType().Assembly.GetName().Version;
            DisplayName = $"LIVESTREAM MONITOR V{currentAppVersion.Major}.{currentAppVersion.Minor}.{currentAppVersion.Build}";
#if DEBUG
            // helps to know that we're running the debug rather than release version of the exe
            DisplayName += " (DEBUG)";
#endif
        }

        public override string DisplayName { get; set; }

        public SettingsViewModel Settings { get; set; }

        public WindowState WindowState
        {
            get;
            set
            {
                if (value == field) return;
                field = value;
                NotifyOfPropertyChange(() => WindowState);
                if (field == WindowState.Minimized)
                    WindowMinimized();
            }
        } = WindowState.Normal;

        public bool IsSettingsOpen
        {
            get;
            set => Set(ref field, value);
        }

        public void ShowWindow()
        {
            Application.Current.MainWindow.Show();
            WindowState = WindowState.Normal;
            Application.Current.MainWindow.Activate();
        }

        public void ShowSettings()
        {
            IsSettingsOpen = true;
        }

        public void GotoTopStreams()
        {
            navigationService.NavigateTo<TopStreamsViewModel>();
        }

        public void GotoVodViewer()
        {
            navigationService.NavigateTo<VodListViewModel>();
        }

        public async Task HandleAsync(ActivateScreen message, CancellationToken cancellationToken)
        {
            if (ActiveItem.GetType() == message.Screen.GetType())
                return;

            // Closing the active page first would show the main page in passing, reloading its views mid-change.
            var previousScreen = ActiveItem;
            Items.Add(message.Screen);
            await ActivateItemAsync(message.Screen, cancellationToken);

            if (previousScreen != mainViewModel)
                await DeactivateItemAsync(previousScreen, true, cancellationToken);
        }

        protected override async void OnViewLoaded(object view)
        {
            if (Execute.InDesignMode) return;

            taskbarIcon = new TrayIconService(ShowWindow, () => TryCloseAsync(true));
            if (!Debugger.IsAttached && settingsHandler.Settings.CheckForNewVersions) StartVersionCheck();
            await InitializeMonitorStreamsModel();
            popularLivestreamWatcher.StartWatching();
            base.OnViewLoaded(view);
        }

        protected override async Task OnDeactivateAsync(bool close, CancellationToken cancellationToken)
        {
            popularLivestreamWatcher.StopWatching();
            taskbarIcon?.Dispose(); // this will be cleaned up on app close anyway but this is a bit cleaner
            await base.OnDeactivateAsync(close, cancellationToken);
        }

        private void WindowMinimized()
        {
            Application.Current.MainWindow.Hide();

            // Prevent minimizing notification windows
            // I just don't want notifications to minimize in the first place but can't find any other way to do this...
            foreach (var window in Application.Current.Windows)
            {
                var notificationWindow = window as NotificationView;
                notificationWindow?.Show();
            }

            var showMinimizeNotification = firstMinimize && !settingsHandler.Settings.DisableMinimizeToTrayNotification;
            firstMinimize = false; // only show the notification max one time
            if (showMinimizeNotification)
                taskbarIcon.NotifyMinimized();
        }

        private async Task InitializeMonitorStreamsModel()
        {
            await Dispatcher.Yield(DispatcherPriority.Background);

            const int StartupFadeMilliseconds = 50;
            var fade = new Storyboard();
            var animation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(StartupFadeMilliseconds));
            Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.OpacityProperty));
            fade.Children.Add(animation);
            // MahApps 1.6.5 looks up this resource when closing a dialog.
            // https://github.com/MahApps/MahApps.Metro/blob/1.6.5/src/MahApps.Metro/Controls/Dialogs/BaseMetroDialog.cs
            var dialogSettings = new MetroDialogSettings
            {
                CustomResourceDictionary = new ResourceDictionary { ["DialogCloseStoryboard"] = fade },
            };
            var window = (MetroWindow)GetView();
            var originalFadeIn = window.OverlayFadeIn;
            var originalFadeOut = window.OverlayFadeOut;
            Exception initializationError = null;
            try
            {
                window.OverlayFadeIn = fade;
                window.OverlayFadeOut = fade;
                var dialogController = await this.ShowProgressAsync(
                    "Initializing", "Initializing App...", dialogSettings: dialogSettings);
                await Dispatcher.Yield(DispatcherPriority.Background);

                try
                {
                    await monitorStreamsModel.Initialize(new WpfDialogs(this));
                }
                catch (Exception ex)
                {
                    initializationError = ex;
                }

                if (dialogController.IsOpen) await dialogController.CloseAsync();
            }
            finally
            {
                // The faster fades apply only to initialization, including when it fails.
                window.OverlayFadeIn = originalFadeIn;
                window.OverlayFadeOut = originalFadeOut;
            }

            if (initializationError != null)
            {
                await this.ShowMessageAsync("Error",
                    $"An error occurred initializing the app.{Environment.NewLine}{initializationError.ExtractErrorMessage()}");
            }
        }

        private void StartVersionCheck()
        {
            Task.Run(async () =>
            {
                try
                {
                    var githubClient = new GitHubClient();
                    var latestRelease = await githubClient.GetLatestRelease();

                    if (latestRelease == null || !Uri.IsWellFormedUriString(latestRelease.HtmlUrl, UriKind.Absolute))
                        throw new Exception($"Invalid HtmlUrl provided by GitHub API for latest release: {latestRelease?.HtmlUrl ?? "<null>"}");

                    if (IsNewerVersion(latestRelease))
                    {
                        await Execute.OnUIThreadAsync(async () =>
                        {
                            var dialogResult = await this.ShowMessageAsync("New version available",
                                "There is a newer version available. Go to download page?",
                                MessageDialogStyle.AffirmativeAndNegative);

                            if (dialogResult == MessageDialogResult.Affirmative)
                            {
                                Process.Start(new ProcessStartInfo(latestRelease.HtmlUrl) { UseShellExecute = true });
                            }
                        });
                    }
                }
                catch (OperationCanceledException) { } // doesn't matter
                catch (Exception ex)
                {
                    await Execute.OnUIThreadAsync(async () =>
                    {
                        await this.ShowMessageAsync("Error",
                            $"An error occurred while checking for a newer version.{Environment.NewLine}{ex.ExtractErrorMessage()}");
                    });
                }
            });
        }

        private bool IsNewerVersion(Release latestRelease)
        {
            if (string.IsNullOrWhiteSpace(latestRelease?.TagName)) return false;

            try
            {
                var releaseVersion = new Version(latestRelease.TagName);
                return releaseVersion > currentAppVersion;
            }
            catch
            {
                // failed to convert the tagname to a version for some reason
                return false;
            }
        }
    }
}
