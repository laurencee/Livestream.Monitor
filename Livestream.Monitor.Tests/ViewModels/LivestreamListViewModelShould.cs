using System;
using System.Threading;
using System.Threading.Tasks;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.Monitoring;
using Livestream.Monitor.Tests.TestFramework;
using Livestream.Monitor.WPF.Model;
using Livestream.Monitor.WPF.ViewModels;
using NSubstitute;
using Xunit;
using INavigationService = Livestream.Monitor.WPF.Core.INavigationService;

namespace Livestream.Monitor.Tests.ViewModels;

public class LivestreamListViewModelShould
{
    [Fact]
    public async Task KeepAutoRefreshingAfterActivationRefreshesStaleStreams()
    {
        await StaThread.Run(async () =>
        {
            // A stale last refresh makes activation refresh first, as when returning from Top Streams or VODs.
            var streams = Substitute.For<IMonitorStreamsModel>();
            streams.Livestreams.Returns(new ObservableList<LivestreamModel>());
            streams.LastRefreshTime.Returns(DateTimeOffset.MinValue);
            streams.RefreshLivestreams().Returns(Task.CompletedTask);
            var settings = Substitute.For<ISettingsHandler>();
            settings.Settings.Returns(new Settings());
            var launcher = new StreamLauncher(settings, Substitute.For<IWindowManager>());
            var navigation = Substitute.For<INavigationService>();
            var viewModel = new LivestreamListViewModel(
                monitorStreamsModel: streams,
                filterModel: new FilterModel(),
                streamLauncher: launcher,
                navigationService: navigation,
                settingsHandler: settings);

            await ((IActivate)viewModel).ActivateAsync(CancellationToken.None);

            await streams.Received(1).RefreshLivestreams();
            Assert.True(viewModel.IsActive);
            Assert.True(viewModel.IsAutoRefreshing);
        });
    }
}
