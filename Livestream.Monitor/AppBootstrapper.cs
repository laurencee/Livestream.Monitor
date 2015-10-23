using Livestream.Monitor.Core;
using Livestream.Monitor.Model;
using Livestream.Monitor.ViewModels;
using TwitchTv;

namespace Livestream.Monitor
{
    using System;
    using System.Collections.Generic;
    using Caliburn.Micro;

    public class AppBootstrapper : BootstrapperBase
    {
        private SimpleContainer container;

        public AppBootstrapper()
        {
            Initialize();
        }

        protected override void Configure()
        {
            container = new SimpleContainer();

            container.Singleton<IEventAggregator, EventAggregator>();
            container.Singleton<IWindowManager, MetroWindowManager>();
            container.Singleton<ITwitchTvReadonlyClient, TwitchTvReadonlyClient>();
            container.Singleton<IMonitorStreamsModel, MonitorStreamsModel>();
            container.Singleton<IMonitoredStreamsFileHandler, MonitoredStreamsFileHandler>();
            container.Singleton<ISettingsHandler, SettingsHandler>();
            container.Singleton<FilterModel>();

            container.PerRequest<ShellViewModel>();
            container.PerRequest<ThemeSelectorViewModel>();
            container.PerRequest<HeaderViewModel>();
            container.PerRequest<ChannelListViewModel>();

            container.PerRequest<StreamLauncher>();
        }

        protected override object GetInstance(Type service, string key)
        {
            var instance = container.GetInstance(service, key);
            if (instance != null)
                return instance;

            throw new InvalidOperationException("Could not locate any instances.");
        }

        protected override IEnumerable<object> GetAllInstances(Type service)
        {
            return container.GetAllInstances(service);
        }

        protected override void BuildUp(object instance)
        {
            container.BuildUp(instance);
        }

        protected override void OnStartup(object sender, System.Windows.StartupEventArgs e)
        {
            DisplayRootViewFor<ShellViewModel>();
        }
    }
}