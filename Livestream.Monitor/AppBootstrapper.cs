#if DEBUG
    //#define FAKE_DATA // uncomment this line to use fake data in debug mode
#endif

using System.IO;
using System.Windows.Threading;
using ExternalAPIs.TwitchTv;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.UI;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.Model.Monitoring;
using Livestream.Monitor.ViewModels;

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
            container.Singleton<IMonitoredStreamsFileHandler, MonitoredStreamsFileHandler>();
            container.Singleton<ISettingsHandler, SettingsHandler>();
            container.Singleton<IApiClientFactory, ApiClientFactory>();
            container.Singleton<FilterModel>();
            container.Singleton<INotificationHandler, NotificationHandler>(); // needs to be a single instance so we can add notifications from anywhere
            container.Singleton<PopularLivestreamWatcher>();
            container.Singleton<StreamLauncher>();

            container.PerRequest<ShellViewModel>();
            container.PerRequest<ThemeSelectorViewModel>();
            container.PerRequest<HeaderViewModel>();
            container.PerRequest<MainViewModel>();
            container.PerRequest<SettingsViewModel>();
            container.PerRequest<LivestreamListViewModel>();
            container.PerRequest<TopStreamsViewModel>();
            container.PerRequest<VodListViewModel>();
            container.PerRequest<ApiClientsQualitiesViewModel>();

            // singleton instance of the navigation service - this relies on using the container so it needs special registration
            Core.INavigationService navigationService = null;
            container.RegisterHandler(typeof (Core.INavigationService), null,
                simpleContainer => navigationService ?? (navigationService = new NavigationService(
                                                                                simpleContainer,
                                                                                simpleContainer.GetInstance<IEventAggregator>())
                                                                                ));

#if FAKE_DATA // comment out the define at the top of this file to launch debug mode with real data
            container.Singleton<IMonitorStreamsModel, FakeMonitorStreamsModel>();
#else
            container.Singleton<IMonitorStreamsModel, MonitorStreamsModel>();
#endif
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
            container.GetInstance<NotificationHandler>(); // make sure we initialize the notification handler at startup
            DisplayRootViewFor<ShellViewModel>();

            var livestreamEventWatcher = container.GetInstance<PopularLivestreamWatcher>();
            livestreamEventWatcher.StartWatching();

#if FAKE_DATA
            var windowManager = container.GetInstance<IWindowManager>();
            windowManager.ShowWindow(new EmulatorViewModel(container.GetInstance<IMonitorStreamsModel>()));
#endif
        }

        protected override void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                const string logsFolder = "logs";
                string errorLogFileName = $"{DateTime.Now.ToString("yyyy-MM-dd")}_error.log";
                string errorLogFilePath = Path.Combine(logsFolder, errorLogFileName);
                if (!Directory.Exists(logsFolder))
                    Directory.CreateDirectory(logsFolder);

                File.AppendAllText(errorLogFilePath, $"{DateTime.Now.ToString("HH:mm:ss.ffff")}| {e.Exception}"); // global exception logging
            }
            catch
            {
                // can't do anything if we fail to write the exception
            }

            base.OnUnhandledException(sender, e);
        }
    }
}