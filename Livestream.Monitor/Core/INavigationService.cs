using System;
using Caliburn.Micro;

namespace Livestream.Monitor.Core
{
    public interface INavigationService
    {
        void NavigateTo<T>(Action<T> initAction = null) where T : IScreen;
    }
}