using System;
using Caliburn.Micro;
using Livestream.Monitor.Model;

namespace Livestream.Monitor.ViewModels
{
    public class NotificationViewModel : Screen
    {
        public NotificationViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");

            Notification = new Notification()
            {
                Title = "Someones stream is online",
                Message = "Channel description for someones stream"
            };
        }

        public NotificationViewModel(Notification notification)
        {
            Notification = notification;
        }

        public Notification Notification { get; }
    }
}
