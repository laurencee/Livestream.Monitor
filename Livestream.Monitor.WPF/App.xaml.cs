using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Core.Utility;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.WPF
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App
    {
        internal const string Unique = "Livestream Monitor";

        // Created in Main, or by the bootstrapper at design time, which registers these instances.
        internal static SettingsHandler SettingsHandler { get; private set; }
        internal static ApiClientFactory ApiClientFactory { get; private set; }
        internal static MonitoredStreamsFileHandler FileHandler { get; private set; }
        internal static MonitorStreamsModel StreamsModel { get; private set; }

        [STAThread]
        public static void Main(string[] args)
        {
            // Settings and followed streams are files in the working directory, which a launch from another directory
            // would otherwise create there.
            const string DataDirectoryOption = "--data-directory";
            var index = Array.IndexOf(args, DataDirectoryOption);
            var dataDirectory = index >= 0 && index + 1 < args.Length ? args[index + 1] : AppContext.BaseDirectory;
            Directory.SetCurrentDirectory(dataDirectory);

            var dispatcher = Dispatcher.CurrentDispatcher;

            using var instance = SingleInstance.Claim(Unique, () => dispatcher.BeginInvoke(ShowWindows));
            if (instance == null) return;

            // The followed channels are loaded and queried while the application's resources and first view load.
            ExternalAPIs.HttpClientExtensions.PreconnectProviderApis();
            CreateServices();
            StreamsModel.BeginInitialQuery();

            var application = new App();
            application.InitializeComponent();
            application.Run();
        }

        internal static void CreateServices()
        {
            SettingsHandler = new SettingsHandler();
            ApiClientFactory = new ApiClientFactory(SettingsHandler);
            FileHandler = new MonitoredStreamsFileHandler(ApiClientFactory);
            StreamsModel = new MonitorStreamsModel(FileHandler, SettingsHandler, ApiClientFactory);
        }

        // A later launch brings the running windows to the foreground.
        private static void ShowWindows()
        {
            foreach (Window window in Current.Windows)
            {
                if (window.WindowState == WindowState.Minimized)
                {
                    // ReSharper disable once PossibleNullReferenceException -- A running app has a main window.
                    Current.MainWindow.Show();
                    window.WindowState = WindowState.Normal;
                }

                window.Activate();
            }
        }
    }
}
