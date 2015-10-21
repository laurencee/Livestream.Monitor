using Caliburn.Micro;
using Action = System.Action;

namespace Livestream.Monitor.Model
{
    public class MenuItem : PropertyChangedBase
    {
        private readonly Action action;
        private string name;
        private bool isChecked;

        public MenuItem(Action action)
        {
            this.action = action;
        }

        public string Name
        {
            get { return name; }
            set
            {
                if (value == name) return;
                name = value;
                NotifyOfPropertyChange();
            }
        }

        public bool IsChecked
        {
            get { return isChecked; }
            set
            {
                if (value == isChecked) return;
                isChecked = value;
                NotifyOfPropertyChange();
            }
        }

        public void Command()
        {
            action();
        }
    }
}