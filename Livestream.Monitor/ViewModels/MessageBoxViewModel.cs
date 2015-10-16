using Caliburn.Micro;

namespace Livestream.Monitor.ViewModels
{
    public class MessageBoxViewModel : Screen
    {
        private string messageText;

        public string MessageText
        {
            get { return messageText; }
            set
            {
                if (value == messageText) return;
                messageText = value;
                NotifyOfPropertyChange(() => MessageText);
            }
        }
    }
}
