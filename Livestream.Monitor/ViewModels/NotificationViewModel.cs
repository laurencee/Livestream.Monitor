using System;
using System.Linq;
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
            if (livestreamNotification == null) throw new ArgumentNullException(nameof(livestreamNotification));
            if (model == null) throw new ArgumentNullException(nameof(model));

            LivestreamNotification = livestreamNotification;
            this.model = model;
        }

        public LivestreamNotification LivestreamNotification { get; }

        public async void Clicked()
        {
            if (LivestreamNotification.ClickAction != null)
            {
                LivestreamNotification.ClickAction();
                await SafeClose();
                return;
            }

            var livestream = model.Livestreams.FirstOrDefault(x => Equals(x, LivestreamNotification.LivestreamModel));
            if (livestream != null)
            {
                model.SelectedLivestream = livestream;
            }

            Application.Current.MainWindow.Show();
            Application.Current.MainWindow.WindowState = WindowState.Normal;
            Application.Current.MainWindow.Activate();
            await SafeClose();
        }

        private async Task SafeClose()
        {
            await Task.Delay(100); // avoids some crash from 'MahApps.Metro.Controls.MetroWindow.TitleBarMouseDown', not sure what the deal is
            TryClose();
        }
    }
}
