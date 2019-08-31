using System;
using System.Linq;
using Caliburn.Micro;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.ViewModels
{
    public class EmulatorViewModel : Screen
    {
        private readonly IMonitorStreamsModel monitorStreamsModel;

        private LivestreamModel toggleModel;

        public EmulatorViewModel(
            IMonitorStreamsModel monitorStreamsModel)
        {
            this.monitorStreamsModel = monitorStreamsModel ?? throw new ArgumentNullException(nameof(monitorStreamsModel));
        }

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
