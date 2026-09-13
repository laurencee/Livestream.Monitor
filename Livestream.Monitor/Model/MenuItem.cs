using Caliburn.Micro;
using Action = System.Action;

namespace Livestream.Monitor.Model
{
    public class MenuItem(Action action) : PropertyChangedBase
    {
        public string Name
        {
            get;
            set => Set(ref field, value);
        }

        public bool IsChecked
        {
            get;
            set => Set(ref field, value);
        }

        public void Command()
        {
            action();
        }
    }
}