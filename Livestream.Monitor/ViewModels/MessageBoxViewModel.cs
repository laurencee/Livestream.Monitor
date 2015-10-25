using Caliburn.Micro;

namespace Livestream.Monitor.ViewModels
{
    public class MessageBoxViewModel : Screen
    {
        private string messageText;

        public MessageBoxViewModel()
        {
            DisplayName = "Livestream Monitor";
        }

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
