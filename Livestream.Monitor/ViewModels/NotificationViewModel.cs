using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Caliburn.Micro;
using Livestream.Monitor.Model;

namespace Livestream.Monitor.ViewModels
{
    public class NotificationViewModel : Screen
    {
        private readonly IMonitorStreamsModel model;

        public NotificationViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");

            ChannelNotification = new ChannelNotification()
            {
                Title = "Someones stream is online",
                Message = "Channel description for someones stream"
            };
        }

        public NotificationViewModel(
            ChannelNotification channelNotification,
            IMonitorStreamsModel model)
        {
            if (channelNotification == null) throw new ArgumentNullException(nameof(channelNotification));
            if (model == null) throw new ArgumentNullException(nameof(model));

            ChannelNotification = channelNotification;
            this.model = model;
        }

        public ChannelNotification ChannelNotification { get; }

        public async void Clicked()
        {
            var channel = model.Channels.FirstOrDefault(x => Equals(x, ChannelNotification.ChannelData));
            if (channel != null)
            {
                model.SelectedChannel = channel;
            }

            Application.Current.MainWindow.Show();
            Application.Current.MainWindow.WindowState = WindowState.Normal;
            Application.Current.MainWindow.Activate();
            await Task.Delay(100); // avoids some crash from 'MahApps.Metro.Controls.MetroWindow.TitleBarMouseDown', not sure what the deal is
            TryClose();
        }
    }
}
