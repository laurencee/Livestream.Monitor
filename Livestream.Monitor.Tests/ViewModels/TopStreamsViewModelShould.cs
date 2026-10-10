using System;
using System.Threading;
using System.Threading.Tasks;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.Model.Monitoring;
using Livestream.Monitor.WPF.Model;
using Livestream.Monitor.WPF.ViewModels;
using NSubstitute;
using Xunit;
using INavigationService = Livestream.Monitor.WPF.Core.INavigationService;

namespace Livestream.Monitor.Tests.ViewModels;

public class TopStreamsViewModelShould
{
    [Fact]
    public async Task ListTheProvidersInTheCollectionTheViewBindsBeforeActivation()
    {
        // Caliburn.Micro 4 conductors bind the view of a new active item before activating, and so initializing, it.
        var provider = Substitute.For<IApiClient>();
        var viewModel = Create(provider, Substitute.For<IMonitorStreamsModel>());

        var bound = viewModel.ApiClients;
        await ((IActivate)viewModel).ActivateAsync(CancellationToken.None);

        Assert.NotNull(bound);
        Assert.Contains(provider, bound);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DropAFailedFollowChangeOnceThePageIsLeft(bool followed)
    {
        // The error message needs the page's view, which is gone once the user has left the page.
        var provider = Substitute.For<IApiClient>();
        var channel = new ChannelIdentifier(provider, "channel");
        var stream = new LivestreamModel("channel", channel);
        var streams = Substitute.For<IMonitorStreamsModel>();
        var followedStreams = new ObservableList<LivestreamModel>();
        if (followed) followedStreams.Add(stream);
        streams.Livestreams.Returns(followedStreams);
        var request = new TaskCompletionSource();
        streams.AddLivestream(channel, Arg.Any<IUserDialogs>()).Returns(request.Task);
        streams.RemoveLivestream(channel, Arg.Any<IUserDialogs>()).Returns(request.Task);
        var viewModel = Create(provider, streams);
        await ((IActivate)viewModel).ActivateAsync(CancellationToken.None);

        var click = viewModel.StreamClicked(new TopStreamResult(stream, channel) { IsMonitored = followed });
        await ((IDeactivate)viewModel).DeactivateAsync(close: true, CancellationToken.None);
        request.SetException(new InvalidOperationException("The provider is unavailable."));

        await click;
    }

    private static TopStreamsViewModel Create(IApiClient provider, IMonitorStreamsModel streams)
    {
        provider.HasTopStreamsSupport.Returns(true);
        var factory = Substitute.For<IApiClientFactory>();
        factory.GetAll().Returns([provider]);
        var settings = Substitute.For<ISettingsHandler>();
        settings.Settings.Returns(new Settings());
        var launcher = new StreamLauncher(settings, Substitute.For<IWindowManager>());

        return new TopStreamsViewModel(
            monitorStreamsModel: streams,
            settingsHandler: settings,
            streamLauncher: launcher,
            navigationService: Substitute.For<INavigationService>(),
            apiClientFactory: factory)
        {
            SelectedApiClient = provider,
        };
    }
}
