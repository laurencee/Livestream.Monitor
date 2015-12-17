using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Caliburn.Micro;
using Hardcodet.Wpf.TaskbarNotification;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.UI;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using Octokit;
using Application = System.Windows.Application;

namespace Livestream.Monitor.ViewModels
{
    public class ShellViewModel : Conductor<Screen>.Collection.OneActive, IHandle<ActivateScreen>
    {
        public const string TrayIconControlName = "TrayIcon";

        private readonly Version currentAppVersion;
        private WindowState windowState = WindowState.Normal;
        private TaskbarIcon taskbarIcon;
        private bool firstMinimize = true;
        private bool isSettingsOpen;

        public ShellViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");

            ThemeSelector = new ThemeSelectorViewModel();
            Settings = new SettingsViewModel();
            ActiveItem = new MainViewModel();
        }

        public ShellViewModel(
            ThemeSelectorViewModel themeSelector,
            SettingsViewModel settingsViewModel,
            MainViewModel mainViewModel,
            IEventAggregator eventAggregator)
        {
            if (themeSelector == null) throw new ArgumentNullException(nameof(themeSelector));
            if (settingsViewModel == null) throw new ArgumentNullException(nameof(settingsViewModel));
            if (mainViewModel == null) throw new ArgumentNullException(nameof(mainViewModel));

            ThemeSelector = themeSelector;
            Settings = settingsViewModel;
            ActiveItem = mainViewModel;

            eventAggregator.Subscribe(this);
            Settings.ActivateWith(this);
            ThemeSelector.ActivateWith(this);

            currentAppVersion = GetType().Assembly.GetName().Version;
            DisplayName = $"LIVESTREAM MONITOR V{currentAppVersion.Major}.{currentAppVersion.Minor}.{currentAppVersion.Build} (BETA)";
        }

        public override string DisplayName { get; set; }

        public ThemeSelectorViewModel ThemeSelector { get; set; }

        public SettingsViewModel Settings { get; set; }

        public WindowState WindowState
        {
            get { return windowState; }
            set
            {
                if (value == windowState) return;
                windowState = value;
                NotifyOfPropertyChange(() => WindowState);
                if (windowState == WindowState.Minimized)
                    WindowMinimized();
            }
        }

        public bool IsSettingsOpen
        {
            get { return isSettingsOpen; }
            set
            {
                if (value == isSettingsOpen) return;
                isSettingsOpen = value;
                NotifyOfPropertyChange(() => IsSettingsOpen);
            }
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

        private void WindowMinimized()
        {
            Application.Current.MainWindow.Hide();
            if (firstMinimize) // only show the notification one time
            {
                taskbarIcon.ShowBalloonTip("Livestream Monitor", "Livestream Monitored minimized to tray", BalloonIcon.Info);
                firstMinimize = false;
            }
        }

        protected override async void OnViewLoaded(object view)
        {
            if (Execute.InDesignMode) return;
            
            taskbarIcon = Application.Current.MainWindow.FindChild<TaskbarIcon>(TrayIconControlName);
            if (!Debugger.IsAttached) await CheckForNewVersion();
            base.OnViewLoaded(view);
        }

        protected override void OnDeactivate(bool close)
        {
            taskbarIcon?.Dispose(); // this will be cleaned up on app close anyway but this is a bit cleaner
            base.OnDeactivate(close);
        }

        private async Task CheckForNewVersion()
        {
            var githubClient =
                new GitHubClient(new ProductHeaderValue("Livestream.Monitor",
                    $"{currentAppVersion.Major}.{currentAppVersion.Minor}.{currentAppVersion.Build}"));

            const string githubRepository = "Livestream.Monitor";
            const string githubUsername = "laurencee";

            var dialogController = await this.ShowProgressAsync("Update Check", "Checking for newer version...");
            try
            {
                var releases = await githubClient.Release.GetAll(githubUsername, githubRepository);
                var latestRelease = releases.FirstOrDefault();
                if (latestRelease != null)
                {
                    if (IsNewerVersion(latestRelease))
                    {
                        await dialogController.CloseAsync();
                        var dialogResult = await this.ShowMessageAsync("New version available",
                            "There is a newer version available. Go to download page?",
                            MessageDialogStyle.AffirmativeAndNegative);

                        if (dialogResult == MessageDialogResult.Affirmative)
                        {
                            System.Diagnostics.Process.Start(latestRelease.HtmlUrl);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (dialogController.IsOpen) await dialogController.CloseAsync();
                await this.ShowMessageAsync("Error",
                    $"An error occured while checking for a newer version.{Environment.NewLine}{ex.Message}");
            }

            if (dialogController.IsOpen) await dialogController.CloseAsync();
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

        public void Handle(ActivateScreen message)
        {
            Items.Add(message.Screen);
            ActivateItem(message.Screen);
        }
    }
}