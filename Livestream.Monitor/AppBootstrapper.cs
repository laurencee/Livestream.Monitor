using System.IO;
using System.Windows;
using System.Windows.Threading;
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
            container.Singleton<NotificationHandler>(); // needs to be a single instance so we can add notifications from anywhere

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
            container.GetInstance<NotificationHandler>(); // make sure we initialize the notification handler at startup
            DisplayRootViewFor<ShellViewModel>();
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

                File.AppendAllText(errorLogFilePath, $"{DateTime.Now}| {e.Exception}"); // global exception logging
            }
            catch 
            {
                // can't do anything if we fail to write the exception
            }

            base.OnUnhandledException(sender, e);
        }
    }
}