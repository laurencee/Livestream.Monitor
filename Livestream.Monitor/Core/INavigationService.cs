using Caliburn.Micro;

namespace Livestream.Monitor.Core
{
    public interface INavigationService
    {
        void NavigateTo<T>() where T : IScreen;
    }
}