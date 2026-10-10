using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.Model
{
    public interface INotificationHandler
    {
        void AddNotification(LivestreamNotification livestreamNotification);
    }
}
