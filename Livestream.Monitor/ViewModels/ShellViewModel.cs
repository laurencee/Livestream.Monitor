using System;
using System.Windows;
using Caliburn.Micro;
using Hardcodet.Wpf.TaskbarNotification;
using MahApps.Metro.Controls;

namespace Livestream.Monitor.ViewModels
{
    public class ShellViewModel : Conductor<Screen>.Collection.AllActive
    {
        public const string TrayIconControlName = "TrayIcon";
        private WindowState windowState = WindowState.Normal;
        private TaskbarIcon taskbarIcon;
        private bool firstMinimize = true;

        public ShellViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");

            ThemeSelector = new ThemeSelectorViewModel();
            Header = new HeaderViewModel();
            ChannelList = new ChannelListViewModel();
        }

        public ShellViewModel(
            ThemeSelectorViewModel themeSelector,
            HeaderViewModel header,
            ChannelListViewModel channelList)
        {
            if (themeSelector == null) throw new ArgumentNullException(nameof(themeSelector));
            if (header == null) throw new ArgumentNullException(nameof(header));
            
            ThemeSelector = themeSelector;
            Header = header;
            ChannelList = channelList;
            Items.AddRange(new Screen[] { Header, ChannelList, ThemeSelector });
        }

        public override string DisplayName { get; set; } = "Livestream Monitor";

        public ThemeSelectorViewModel ThemeSelector { get; set; }
        public HeaderViewModel Header { get; set; }
        public ChannelListViewModel ChannelList { get; set; }

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

        public void ShowWindow()
        {
            Application.Current.MainWindow.Show();
            WindowState = WindowState.Normal;
            Application.Current.MainWindow.Activate();
        }

        private void WindowMinimized()
        {
            throw new InvalidOperationException("Test exception");
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