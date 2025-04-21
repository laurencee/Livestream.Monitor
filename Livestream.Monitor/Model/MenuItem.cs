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
            set => Set(ref name, value);
        }

        public bool IsChecked
        {
            get { return isChecked; }
            set => Set(ref isChecked, value);
        }

        public void Command()
        {
            action();
        }
    }
}