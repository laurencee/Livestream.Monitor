using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.Utility;
using Livestream.Monitor.Model.Monitoring;
using Livestream.Monitor.ViewModels;

namespace Livestream.Monitor.Model
{
    public class NotificationHandler : INotificationHandler
    {
        private const double NotificationViewWindowHeight = 100;
        private const double NotificationViewWindowWidth = 400;
        private const double BottomMargin = 5;
        private const double RightMargin = 5;
        private const byte MAX_NOTIFICATIONS = 4;

        private readonly IWindowManager windowManager;
        private readonly IMonitorStreamsModel monitorStreamsModel;
        private readonly ISettingsHandler settingsHandler;
        private readonly StreamLauncher streamLauncher;
        private readonly List<LivestreamNotification> buffer = new List<LivestreamNotification>();
        private readonly List<LivestreamNotification> notifications = new List<LivestreamNotification>();
        
        private bool hasRefreshed;

        public NotificationHandler(
            IWindowManager windowManager,
            IMonitorStreamsModel monitorStreamsModel,
            ISettingsHandler settingsHandler,
            StreamLauncher streamLauncher)
        {
            if (windowManager == null) throw new ArgumentNullException(nameof(windowManager));
            if (monitorStreamsModel == null) throw new ArgumentNullException(nameof(monitorStreamsModel));
            if (settingsHandler == null) throw new ArgumentNullException(nameof(settingsHandler));
            if (streamLauncher == null) throw new ArgumentNullException(nameof(streamLauncher));

            this.windowManager = windowManager;
            this.monitorStreamsModel = monitorStreamsModel;
            this.settingsHandler = settingsHandler;
            this.streamLauncher = streamLauncher;

            monitorStreamsModel.LivestreamsRefreshComplete += MonitorStreamsModelOnLivestreamsRefreshComplete;

            if (monitorStreamsModel.Livestreams != null)
            {
                foreach (var livestream in monitorStreamsModel.Livestreams)
                {
                    HookLivestreamChangeEvents(livestream);
                }
                monitorStreamsModel.Livestreams.CollectionChanged += LivestreamsOnCollectionChanged;
            }
        }

        public void AddNotification(LivestreamNotification livestreamNotification)
        {
            if (settingsHandler.Settings.DisableNotifications) return;
            if (livestreamNotification.LivestreamModel.DontNotify) return;
            // don't notify for streams that are being watched right now
            if (streamLauncher.WatchingStreams.Contains(livestreamNotification.LivestreamModel)) return;
            
            if ((notifications.Count + 1) > MAX_NOTIFICATIONS)
            {
                buffer.Add(livestreamNotification);
            }
            else
            {
                notifications.Add(livestreamNotification);
                ShowNotification(livestreamNotification);
            }
        }

        private void ShowNotification(LivestreamNotification livestreamNotification)
        {
            var vmTopLeft = GetNotificationTopLeft(livestreamNotification);
            var settings = new WindowSettingsBuilder().NoResizeBorderless()
                                                      .WithTopLeft(vmTopLeft.Y, vmTopLeft.X)
                                                      .TransparentBackground()
                                                      .AsTopmost()
                                                      .Create();

            var notificationViewModel = new NotificationViewModel(livestreamNotification, monitorStreamsModel);

            // put remaining notifications into their correct position after this LivestreamNotification closes
            notificationViewModel.Deactivated += (sender, args) =>
            {
                AdjustWindows();
                RemoveNotification(notificationViewModel.LivestreamNotification);
            };

            Execute.OnUIThread(() =>
            {
                windowManager.ShowWindow(notificationViewModel, null, settings);

                // Close the popup after a brief duration
                var timer = new DispatcherTimer() { Interval = livestreamNotification.Duration };
                timer.Tick += (sender, args) =>
                {
                    notificationViewModel.TryClose();
                    timer.Stop();
                };
                timer.Start();
            });
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

        private Point GetNotificationTopLeft(LivestreamNotification livestreamNotification)
        {
            if (livestreamNotification == null) return new Point();

            var index = notifications.IndexOf(livestreamNotification) + 1;
            // we care about the order the notifications were added for vertical positioning
            if (index == 0) return new Point();

            return new Point()
            {
                X = SystemParameters.WorkArea.Right - NotificationViewWindowWidth - RightMargin,
                Y = SystemParameters.WorkArea.Bottom - (NotificationViewWindowHeight * index) - BottomMargin
            };
        }

        private void RemoveNotification(LivestreamNotification livestreamNotification)
        {
            if (notifications.Contains(livestreamNotification))
                notifications.Remove(livestreamNotification);
            if (buffer.Count > 0)
            {
                AddNotification(buffer[0]);
                buffer.RemoveAt(0);
            }
        }

        private void UnhookLivestreamChangeEvents(LivestreamModel removedLivestream)
        {
            if (removedLivestream == null) throw new ArgumentNullException(nameof(removedLivestream));

            removedLivestream.PropertyChanged += LivestreamOnPropertyChanged;
        }

        private void HookLivestreamChangeEvents(LivestreamModel newLivestream)
        {
            if (newLivestream == null) throw new ArgumentNullException(nameof(newLivestream));

            newLivestream.PropertyChanged += LivestreamOnPropertyChanged;
        }

        private void MonitorStreamsModelOnLivestreamsRefreshComplete(object sender, EventArgs eventArgs)
        {
            if (!hasRefreshed) // only listen to stream change events after the first refresh has occurred
            {
                hasRefreshed = true;
                return;
            }

            // Only hook up livestream change events 1 time
            monitorStreamsModel.LivestreamsRefreshComplete -= MonitorStreamsModelOnLivestreamsRefreshComplete;
        }

        private void LivestreamsOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (LivestreamModel livestream in e.NewItems)
                {
                    HookLivestreamChangeEvents(livestream);
                }
            }

            if (e.OldItems != null)
            {
                foreach (LivestreamModel removedLivestream in e.OldItems)
                {
                    UnhookLivestreamChangeEvents(removedLivestream);
                }
            }
        }

        private void LivestreamOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var livestreamModel = (LivestreamModel)sender;
            if (e.PropertyName == nameof(LivestreamModel.Live) && hasRefreshed) // dont show notifications for the initial refresh
            {
                if (!livestreamModel.Live) return; // only care about streams coming online

                // avoid a twitch api bug where sometimes online streams will not be returned when querying for online streams
                // the best way we can work around this is to pick a reasonable uptime value after which we will never show online notifications.
                // we check the LastLiveTime for null to ensure we will always notify the first time a stream come online
                if (livestreamModel.LastLiveTime != null && livestreamModel.Uptime > TimeSpan.FromMinutes(5)) return;

                var notification = new LivestreamNotification()
                {
                    Title = $"{livestreamModel.DisplayName} Online",
                    Message = livestreamModel.Description,
                    ImageUrl = livestreamModel.ThumbnailUrls?.Small,
                    LivestreamModel = livestreamModel,
                };

                AddNotification(notification);
            }
        }
    }
}