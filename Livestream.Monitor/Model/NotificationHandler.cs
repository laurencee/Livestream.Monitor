using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Livestream.Monitor.ViewModels;

namespace Livestream.Monitor.Model
{
    public class NotificationHandler
    {
        private const double NotificationViewWindowHeight = 100;
        private const double NotificationViewWindowWidth = 300;
        private const double BottomMargin = 5;
        private const double RightMargin = 10;
        private const byte MAX_NOTIFICATIONS = 4;

        private readonly IWindowManager windowManager;
        private readonly IMonitorStreamsModel monitorStreamsModel;
        private readonly List<Notification> buffer = new List<Notification>();
        private readonly List<Notification> notifications = new List<Notification>();
        private readonly TimeSpan notificationDuration = TimeSpan.FromSeconds(5);

        private int notificationId;
        private bool hasRefreshed;

        public NotificationHandler(IWindowManager windowManager,
                                   IMonitorStreamsModel monitorStreamsModel)
        {
            if (windowManager == null) throw new ArgumentNullException(nameof(windowManager));
            if (monitorStreamsModel == null) throw new ArgumentNullException(nameof(monitorStreamsModel));

            this.windowManager = windowManager;
            this.monitorStreamsModel = monitorStreamsModel;

            foreach (var channelData in monitorStreamsModel.Channels)
            {
                HookChannelChangeEvents(channelData);
            }
            monitorStreamsModel.OnlineChannelsRefreshComplete += MonitorStreamsModelOnOnlineChannelsRefreshComplete;
            monitorStreamsModel.Channels.CollectionChanged += ChannelsOnCollectionChanged;
        }

        public void AddNotification(Notification notification)
        {
            notification.Id = notificationId++;
            if ((notifications.Count + 1) > MAX_NOTIFICATIONS)
                buffer.Add(notification);
            else
            {
                notifications.Add(notification);
                ShowNotification(notification);
            }
        }

        private void ShowNotification(Notification notification)
        {
            var vmTopLeft = GetNotificationTopLeft(notification);
            var settings = new WindowSettingsBuilder().WithWindowStyle(WindowStyle.None)
                                                      .WithResizeMode(ResizeMode.NoResize)
                                                      .WithTopLeft(vmTopLeft.Y, vmTopLeft.X)
                                                      .TransparentBackground()
                                                      .AsTopmost()
                                                      .Create();

            var notificationViewModel = new NotificationViewModel(notification);

            // put remaining notifications into their correct position after this notification closes
            notificationViewModel.Deactivated += (sender, args) =>
            {
                AdjustWindows();
                RemoveNotification(notificationViewModel.Notification);
            };
            windowManager.ShowWindow(notificationViewModel, null, settings);

            // TODO - do we really need a new dispatch timer every time we create a notification? 
            // TODO - maybe we could just have 1 long running timer that on tick checks when the notification was added and removes it past its expiry time (5 seconds)
            var timer = new DispatcherTimer() { Interval = notificationDuration };
            timer.Tick += (sender, args) =>
            {
                notificationViewModel.TryClose();
                timer.Stop();
            };
            timer.Start();
        }

        private void AdjustWindows()
        {
            var windows = Application.Current.Windows.Cast<Window>()
                                     .Where(x => x.Title == typeof(NotificationViewModel).FullName)
                                     .OrderBy(x => x.Top)
                                     .ToList();

            // drop all remaining windows down to the place of the previous window
            for (int i = 0; i < windows.Count; i++)
            {
                windows[i].Top = SystemParameters.WorkArea.Bottom - (NotificationViewWindowHeight * (i + 1)) -
                                 BottomMargin;
            }
        }

        private Point GetNotificationTopLeft(Notification notification)
        {
            if (notification == null) return new Point();

            var index = notifications.IndexOf(notification) + 1;
            // we care about the order the notifications were added for vertical positioning
            if (index == 0) return new Point();

            return new Point()
            {
                X = SystemParameters.WorkArea.Right - NotificationViewWindowWidth - RightMargin,
                Y = SystemParameters.WorkArea.Bottom - (NotificationViewWindowHeight * index) - BottomMargin
            };
        }

        private void RemoveNotification(Notification notification)
        {
            if (notifications.Contains(notification))
                notifications.Remove(notification);
            if (buffer.Count > 0)
            {
                AddNotification(buffer[0]);
                buffer.RemoveAt(0);
            }
        }

        private void UnhookChannelChangeEvents(ChannelData removedChannel)
        {
            if (removedChannel == null) throw new ArgumentNullException(nameof(removedChannel));

            removedChannel.PropertyChanged += ChannelDataOnPropertyChanged;
        }

        private void HookChannelChangeEvents(ChannelData newChannel)
        {
            if (newChannel == null) throw new ArgumentNullException(nameof(newChannel));

            newChannel.PropertyChanged += ChannelDataOnPropertyChanged;
        }

        private void MonitorStreamsModelOnOnlineChannelsRefreshComplete(object sender, EventArgs eventArgs)
        {
            if (!hasRefreshed) // only listen to stream change events after the first refresh has occurred
            {
                hasRefreshed = true;
                return;
            }

            // Only hook up channel change events 1 time
            monitorStreamsModel.OnlineChannelsRefreshComplete -= MonitorStreamsModelOnOnlineChannelsRefreshComplete;
        }

        private void ChannelsOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            foreach (ChannelData channelData in e.NewItems)
            {
                HookChannelChangeEvents(channelData);
            }

            foreach (ChannelData removedChannels in e.OldItems)
            {
                UnhookChannelChangeEvents(removedChannels);
            }
        }

        private void ChannelDataOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var channelData = (ChannelData)sender;
            if (e.PropertyName == nameof(ChannelData.Live) && hasRefreshed) // dont show notifications for the initial refresh
            {
                if (!channelData.Live) return; // only care about streams coming online

                var notification = new Notification()
                {
                    Title = $"{channelData.ChannelName} Online",
                    Message = channelData.ChannelDescription,
                    ImageUrl = channelData.Preview?.Small
                };

                AddNotification(notification);
            }
        }
    }
}