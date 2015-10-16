using System;

namespace Livestream.Monitor.Model
{
    public class MenuItem
    {
        private readonly Action action;

        public MenuItem(Action action)
        {
            this.action = action;
        }

        public string Name { get; set; }

        public void Command()
        {
            action();
        }
    }
}