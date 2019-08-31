using System;
using Caliburn.Micro;

namespace Livestream.Monitor.ViewModels
{
    public class MainViewModel : Screen
    {
        public MainViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");

            Header = new HeaderViewModel();
            LivestreamList = new LivestreamListViewModel();
        }

        public MainViewModel(HeaderViewModel header, LivestreamListViewModel livestreamList)
        {
            Header = header ?? throw new ArgumentNullException(nameof(header));
            LivestreamList = livestreamList ?? throw new ArgumentNullException(nameof(livestreamList));

            Header.ActivateWith(this);
            Header.DeactivateWith(this);
            LivestreamList.ActivateWith(this);
            LivestreamList.DeactivateWith(this);
        }

        public HeaderViewModel Header { get; set; }

        public LivestreamListViewModel LivestreamList { get; set; }
    }
}
