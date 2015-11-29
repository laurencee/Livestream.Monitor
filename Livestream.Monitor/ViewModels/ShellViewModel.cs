using System;
using System.Windows;
using Caliburn.Micro;
using Hardcodet.Wpf.TaskbarNotification;
using MahApps.Metro.Controls;

namespace Livestream.Monitor.ViewModels
{
    public class ShellViewModel : Conductor<Screen>.Collection.OneActive
    {
        public const string TrayIconControlName = "TrayIcon";
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
            MainViewModel mainViewModel)
        {
            if (themeSelector == null) throw new ArgumentNullException(nameof(themeSelector));
            if (settingsViewModel == null) throw new ArgumentNullException(nameof(settingsViewModel));
            if (mainViewModel == null) throw new ArgumentNullException(nameof(mainViewModel));

            ThemeSelector = themeSelector;
            Settings = settingsViewModel;
            ActiveItem = mainViewModel;

            Settings.ActivateWith(this);
            ThemeSelector.ActivateWith(this);

            var assemblyVersion = GetType().Assembly.GetName().Version;
            DisplayName = $"LIVESTREAM MONITOR V{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
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

        protected override void OnViewLoaded(object view)
        {
            base.OnViewLoaded(view);
            taskbarIcon = Application.Current.MainWindow.FindChild<TaskbarIcon>(TrayIconControlName);
        }

        protected override void OnDeactivate(bool close)
        {
            taskbarIcon.Dispose(); // this will be cleaned up on app close anyway but this is a bit cleaner
            base.OnDeactivate(close);
        }
    }
}