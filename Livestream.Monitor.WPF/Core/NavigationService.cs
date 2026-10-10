using System;
using Caliburn.Micro;
using Livestream.Monitor.WPF.Core.UI;

namespace Livestream.Monitor.WPF.Core
{
    public class NavigationService(SimpleContainer container, IEventAggregator eventAggregator)
        : INavigationService
    {
        private readonly SimpleContainer container = container ?? throw new ArgumentNullException(nameof(container));
        private readonly IEventAggregator eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));

        public void NavigateTo<T>(Action<T> initAction = null) where T : IScreen
        {
            var screen = container.GetInstance<T>();
            if (screen == null)
                throw new InvalidOperationException($"Could not navigate to {typeof(T).FullName}. Type not registered");

            initAction?.Invoke(screen);
            eventAggregator.PublishOnUIThreadAsync(new ActivateScreen(screen));
        }
    }
}
