using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Caliburn.Micro;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.WPF.ViewModels
{
    public class NotificationViewModel : Screen
    {
        private const int MaximumIconBytes = 512 * 1024;
        private static readonly HttpClient IconClient = new()
        {
            MaxResponseContentBufferSize = MaximumIconBytes,
            Timeout = TimeSpan.FromSeconds(2),
        };
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

        public BitmapSource ChannelIcon
        {
            get;
            private set => Set(ref field, value);
        }

        internal async Task LoadChannelIcon()
        {
            try
            {
                var url = await LivestreamNotification.FindChannelIconUrl(LivestreamNotification.LivestreamModel.ChannelIdentifier);
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
                if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return;

                var bytes = await IconClient.GetByteArrayAsync(uri);
                using var stream = new MemoryStream(bytes);
                const int IconWidth = 128;
                var icon = new BitmapImage();
                icon.BeginInit();
                icon.StreamSource = stream;
                icon.CacheOption = BitmapCacheOption.OnLoad;
                icon.DecodePixelWidth = IconWidth;
                icon.EndInit();
                ChannelIcon = icon;
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or
                                              IOException or FileFormatException or NotSupportedException or COMException)
            {
                // Keep the live thumbnail if the optional icon cannot be loaded.
            }
        }

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
            await TryCloseAsync();
        }
    }
}
