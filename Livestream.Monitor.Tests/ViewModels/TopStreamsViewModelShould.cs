using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Caliburn.Micro;
using ExternalAPIs;
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
    private static readonly TimeSpan SuggestionTimeout = TimeSpan.FromSeconds(5);

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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SendTheGameFilterOnlyWhenTheProviderSupportsIt(bool supportsGameFilter)
    {
        const string Game = "Example game";
        var provider = Substitute.For<IApiClient>();
        provider.IsAuthorized.Returns(true);
        provider.HasTopStreamGameFilterSupport.Returns(supportsGameFilter);
        provider.GetTopStreams(Arg.Any<TopStreamQuery>()).Returns(new TopStreamsResponse());
        var streams = Substitute.For<IMonitorStreamsModel>();
        streams.Livestreams.Returns(new ObservableList<LivestreamModel>());
        var viewModel = Create(provider, streams);
        viewModel.GameName = Game;
        await ((IActivate)viewModel).ActivateAsync(CancellationToken.None);

        await viewModel.RefreshItems();

        var expectedGame = supportsGameFilter ? Game : null;
        await provider.Received(1).GetTopStreams(Arg.Is<TopStreamQuery>(query => query.GameName == expectedGame));
        Assert.Equal(Game, viewModel.GameName);
    }

    [Fact]
    public void DiscardSuggestionsFromBeforeAProviderRoundTrip()
    {
        var provider = Substitute.For<IApiClient>();
        provider.HasTopStreamGameFilterSupport.Returns(true);
        var oldRequest = new TaskCompletionSource<List<KnownGame>>();
        var currentGames = new List<KnownGame> { new() { GameName = "Current game" } };
        provider.GetKnownGameNames(null).Returns(oldRequest.Task, Task.FromResult(currentGames));
        var otherProvider = Substitute.For<IApiClient>();
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);

        try
        {
            var viewModel = Create(provider, Substitute.For<IMonitorStreamsModel>());
            viewModel.SelectedApiClient = otherProvider;
            viewModel.SelectedApiClient = provider;
            oldRequest.SetResult([new KnownGame { GameName = "Stale game" }]);

            Assert.Equal(["Current game"], viewModel.PossibleGameNames);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    [Fact]
    public async Task LoadDefaultSuggestionsWhenClearingAnUnresolvedGameFilter()
    {
        const string DefaultGame = "Default game";
        var provider = Substitute.For<IApiClient>();
        provider.IsAuthorized.Returns(true);
        provider.HasTopStreamGameFilterSupport.Returns(true);
        provider.GetTopStreams(Arg.Any<TopStreamQuery>()).Returns(new TopStreamsResponse());
        var oldRequest = new TaskCompletionSource<List<KnownGame>>();
        var currentGames = new List<KnownGame> { new() { GameName = DefaultGame } };
        provider.GetKnownGameNames(null).Returns(oldRequest.Task, Task.FromResult(currentGames));
        var streams = Substitute.For<IMonitorStreamsModel>();
        streams.Livestreams.Returns(new ObservableList<LivestreamModel>());
        var viewModel = Create(provider, streams);
        var suggestionsLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PossibleGameNames.CollectionChanged += (_, _) =>
        {
            if (viewModel.PossibleGameNames.Contains(DefaultGame)) suggestionsLoaded.TrySetResult();
        };

        await ((IActivate)viewModel).ActivateAsync(CancellationToken.None);
        viewModel.GameName = "Transient filter";
        viewModel.GameName = "";
        oldRequest.SetResult([new KnownGame { GameName = "Stale game" }]);

        await suggestionsLoaded.Task.WaitAsync(SuggestionTimeout);
        Assert.Equal([DefaultGame], viewModel.PossibleGameNames);
    }

    [Fact]
    public async Task KeepTypedSuggestionsWhenTheViewFinishesLoading()
    {
        const string Search = "Example";
        const string Match = "Example game";
        var provider = Substitute.For<IApiClient>();
        provider.IsAuthorized.Returns(true);
        provider.HasTopStreamGameFilterSupport.Returns(true);
        provider.GetTopStreams(Arg.Any<TopStreamQuery>()).Returns(new TopStreamsResponse());
        var lateDefaults = new TaskCompletionSource<List<KnownGame>>();
        var emptyDefaults = Task.FromResult(new List<KnownGame>());
        provider.GetKnownGameNames(null).Returns(emptyDefaults, lateDefaults.Task);
        var searchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var searchResults = new TaskCompletionSource<List<KnownGame>>();
        provider.GetKnownGameNames(Search).Returns(_ =>
        {
            searchStarted.SetResult();
            return searchResults.Task;
        });

        var streams = Substitute.For<IMonitorStreamsModel>();
        streams.Livestreams.Returns(new ObservableList<LivestreamModel>());
        var viewModel = Create(provider, streams);
        var suggestionsLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PossibleGameNames.CollectionChanged += (_, _) =>
        {
            if (viewModel.PossibleGameNames.Contains(Match)) suggestionsLoaded.TrySetResult();
        };

        await ((IActivate)viewModel).ActivateAsync(CancellationToken.None);
        viewModel.GameName = Search;
        await searchStarted.Task.WaitAsync(SuggestionTimeout);
        await viewModel.LoadTopStreams();

        searchResults.SetResult([new KnownGame { GameName = Match }]);
        await suggestionsLoaded.Task.WaitAsync(SuggestionTimeout);
        lateDefaults.SetResult([new KnownGame { GameName = "Late default" }]);

        await provider.Received(1).GetKnownGameNames(null);
        Assert.Equal([Match], viewModel.PossibleGameNames);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BrowseVodsOnlyForASupportedProvider(bool supportsVods)
    {
        var provider = Substitute.For<IApiClient>();
        provider.HasVodViewerSupport.Returns(supportsVods);
        var navigation = Substitute.For<INavigationService>();
        var viewModel = Create(provider, Substitute.For<IMonitorStreamsModel>(), navigation);
        var channel = new ChannelIdentifier(provider, "channel");
        var stream = new LivestreamModel("channel", channel);

        viewModel.GotoVodViewer(new TopStreamResult(stream, channel));

        navigation.Received(supportsVods ? 1 : 0).NavigateTo(Arg.Any<Action<VodListViewModel>>());
    }

    private static TopStreamsViewModel Create(IApiClient provider, IMonitorStreamsModel streams, INavigationService navigation = null)
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
            navigationService: navigation ?? Substitute.For<INavigationService>(),
            apiClientFactory: factory)
        {
            SelectedApiClient = provider,
        };
    }
}
