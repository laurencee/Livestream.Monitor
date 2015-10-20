using System;
using System.Windows;
using Caliburn.Micro;

namespace Livestream.Monitor.ViewModels
{
    public class ShellViewModel : Conductor<Screen>.Collection.AllActive
    {
        private readonly IWindowManager windowManager;
        private WindowState windowState = WindowState.Normal;

        public ShellViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");

            ThemeSelector = new ThemeSelectorViewModel();
            Header = new HeaderViewModel();
            ChannelList = new ChannelListViewModel();
        }

        public ShellViewModel(
            IWindowManager windowManager,
            ThemeSelectorViewModel themeSelector,
            HeaderViewModel header,
            ChannelListViewModel channelList)
        {
            if (windowManager == null) throw new ArgumentNullException(nameof(windowManager));
            if (themeSelector == null) throw new ArgumentNullException(nameof(themeSelector));
            if (header == null) throw new ArgumentNullException(nameof(header));

            this.windowManager = windowManager;
            ThemeSelector = themeSelector;
            Header = header;
            ChannelList = channelList;
            Items.AddRange(new Screen[] { Header, ChannelList, ThemeSelector });
        }

        public override string DisplayName { get; set; } = "Livestream Monitor";

        public ThemeSelectorViewModel ThemeSelector { get; set; }
        public HeaderViewModel Header { get; private set; }
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
                    Application.Current.MainWindow.Hide();
            }
        }

        public void ShowWindow()
        {
            Application.Current.MainWindow.Show();
            WindowState = WindowState.Normal;
        }
    }
}