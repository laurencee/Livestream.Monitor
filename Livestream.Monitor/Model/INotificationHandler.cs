using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.Model
{
    public interface INotificationHandler // only exists for testability purposes
    {
        void AddNotification(LivestreamNotification livestreamNotification);
    }
}