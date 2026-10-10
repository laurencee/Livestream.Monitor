using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Livestream.Monitor.Core.Utility;

namespace Livestream.Monitor.WPF
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App
    {
        internal const string Unique = "Livestream Monitor";

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

            var application = new App();
            application.InitializeComponent();
            application.Run();
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
