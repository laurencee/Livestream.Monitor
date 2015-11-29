using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
            ChannelList = new ChannelListViewModel();
        }

        public MainViewModel(HeaderViewModel header, ChannelListViewModel channelList)
        {
            if (header == null) throw new ArgumentNullException(nameof(header));
            if (channelList == null) throw new ArgumentNullException(nameof(channelList));

            Header = header;
            ChannelList = channelList;

            Header.ActivateWith(this);
            Header.DeactivateWith(this);
            ChannelList.ActivateWith(this);
            ChannelList.DeactivateWith(this);
        }

        public HeaderViewModel Header { get; set; }

        public ChannelListViewModel ChannelList { get; set; }
    }
}
