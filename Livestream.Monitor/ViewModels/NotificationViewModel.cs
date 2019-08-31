using System;
using System.Threading.Tasks;
using System.Windows;
using Caliburn.Micro;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.ViewModels
{
    public class NotificationViewModel : Screen
    {
        private readonly IMonitorStreamsModel model;

        public NotificationViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");

            LivestreamNotification = new LivestreamNotification()
            {
                Title = "Someones stream is online",
                Message = "Livestream description for someones stream"
            };
        }

        public NotificationViewModel(
            LivestreamNotification livestreamNotification,
            IMonitorStreamsModel model)
        {
            LivestreamNotification = livestreamNotification ?? throw new ArgumentNullException(nameof(livestreamNotification));
            this.model = model ?? throw new ArgumentNullException(nameof(model));
        }

        public LivestreamNotification LivestreamNotification { get; }

        public async void Clicked()
        {
            LivestreamNotification.ClickAction?.Invoke(model, LivestreamNotification);
            await CloseAndShowApp();
        }

        private async Task CloseAndShowApp()
        {
            Application.Current.MainWindow.Show();
            Application.Current.MainWindow.WindowState = WindowState.Normal;
            Application.Current.MainWindow.Activate();

            await Task.Delay(100); // avoids some crash from 'MahApps.Metro.Controls.MetroWindow.TitleBarMouseDown', not sure what the deal is
            TryClose();
        }
    }
}
