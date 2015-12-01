using System;
using System.Collections.Generic;
using System.Windows;
using Livestream.Monitor.Core;

namespace Livestream.Monitor
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application, ISingleInstanceApp
    {
        private const string Unique = "Livestream Monitor";

        [STAThread]
        public static void Main()
        {
            // Check that only one instance of the application is running
            if (SingleInstance<App>.InitializeAsFirstInstance(Unique))
            {
                var application = new App();
                application.InitializeComponent();
                application.Run();

                // Allow single instance code to perform cleanup operations
                SingleInstance<App>.Cleanup();
            }
        }

        /// <summary> Command that will be called if a second attempt to launch the application occurs. </summary>
        public bool SignalExternalCommandLineArgs(IList<string> args)
        {
            foreach (Window window in Windows)
            {
                // Bring window to foreground
                if (window.WindowState == WindowState.Minimized)
                {
                    Current.MainWindow.Show();
                    window.WindowState = WindowState.Normal;
                }

                window.Activate();
            }

            return true;
        }
    }
}
