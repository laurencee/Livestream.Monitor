using System;
using Caliburn.Micro;
using Livestream.Monitor.Core.UI;

namespace Livestream.Monitor.Core
{
    public class NavigationService : INavigationService
    {
        private readonly SimpleContainer container;
        private readonly IEventAggregator eventAggregator;

        public NavigationService(SimpleContainer container, IEventAggregator eventAggregator)
        {
            this.container = container ?? throw new ArgumentNullException(nameof(container));
            this.eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        }

        public void NavigateTo<T>(Action<T> initAction = null) where T : IScreen
        {
            var screen = container.GetInstance<T>();
            if (screen == null)
                throw new InvalidOperationException($"Could not navigate to {typeof(T).FullName}. Type not registered");

            initAction?.Invoke(screen);
            eventAggregator.PublishOnUIThread(new ActivateScreen(screen));
        }
    }
}
