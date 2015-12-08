using Caliburn.Micro;

namespace Livestream.Monitor.Core.UI
{
    /// <summary> Event to be used for calling the shell to activate a screen </summary>
    public class ActivateScreen
    {
        public ActivateScreen(Screen screen)
        {
            Screen = screen;
        }

        public Screen Screen { get; private set; }
    }
}