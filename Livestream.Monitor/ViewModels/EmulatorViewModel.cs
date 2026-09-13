using System;
using System.Linq;
using Caliburn.Micro;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.ViewModels
{
    // ReSharper disable once UnusedType.Global - Used in AppBootstrapper when FAKE_DATA defined
    public class EmulatorViewModel(IMonitorStreamsModel monitorStreamsModel) : Screen
    {
        private readonly IMonitorStreamsModel monitorStreamsModel = monitorStreamsModel ?? throw new ArgumentNullException(nameof(monitorStreamsModel));

        private LivestreamModel toggleModel;

        public override string DisplayName { get; set; } = "EMULATOR";

        public void ToggleOnline()
        {
            if (toggleModel == null)
                toggleModel = monitorStreamsModel.Livestreams.First(x => x.Live);

            if (toggleModel.Live)
            {
                FakeMonitorStreamsModel.SetStreamOffline(toggleModel);
            }
            else
            {
                FakeMonitorStreamsModel.SetStreamOnline(toggleModel);
            }
        }
    }
}
