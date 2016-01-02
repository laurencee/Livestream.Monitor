using Caliburn.Micro;

namespace Livestream.Monitor.Core.UI
{
    /// <summary> Event to be used for calling the shell to activate a screen </summary>
    public class ActivateScreen
    {
        public ActivateScreen(IScreen screen)
        {
            Screen = screen;
        }

        public IScreen Screen { get; private set; }
    }
}