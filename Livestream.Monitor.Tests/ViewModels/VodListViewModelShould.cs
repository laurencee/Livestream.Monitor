using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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

namespace Livestream.Monitor.Tests.ViewModels;

public class VodListViewModelShould
{
    [Fact]
    public async Task LaunchEachClickedVodUsingItsOwnProviderUrlAndTitle()
    {
        // Use an invalid executable name so the test can't start a player.
        const string Player = "invalid|vod-player";
        var configuration = new Settings
        {
            DebugMode = true,
            Twitch = { VodCommand = new ExecCommand { FilePath = Player, Args = "twitch {url}" } },
            YouTube = { VodCommand = new ExecCommand { FilePath = Player, Args = "youtube {url}" } },
        };

        var settings = Substitute.For<ISettingsHandler>();
        settings.Settings.Returns(configuration);
        var windows = Substitute.For<IWindowManager>();
        var outputs = new List<MessageBoxViewModel>();
        windows.When(x => x.ShowWindowAsync(Arg.Any<object>(), Arg.Any<object>(), Arg.Any<IDictionary<string, object>>()))
            .Do(call => outputs.Add((MessageBoxViewModel)call[0]));

        var launcher = new StreamLauncher(settings, windows);
        var streams = Substitute.For<IMonitorStreamsModel>();
        streams.Livestreams.Returns(new ObservableList<LivestreamModel>());
        var viewModel = new VodListViewModel(launcher, streams, Substitute.For<IApiClientFactory>());
        var twitch = Substitute.For<IApiClient>();
        twitch.ApiName.Returns(TwitchApiClient.API_NAME);
        var youtube = Substitute.For<IApiClient>();
        youtube.ApiName.Returns(YoutubeApiClient.API_NAME);
        viewModel.SelectedApiClient = twitch;
        var first = new VodDetails { ApiClient = twitch, Url = "https://www.twitch.tv/videos/1", Title = "First VOD" };
        var second = new VodDetails { ApiClient = youtube, Url = "https://www.youtube.com/watch?v=second", Title = "Second VOD" };

        await viewModel.OpenVod(first);
        await viewModel.OpenVod(second);

        var launches = outputs.Distinct().ToArray();
        Assert.Equal(2, launches.Length);
        Assert.Equal(first.Title, launches[0].DisplayName);
        Assert.Contains($"[DEBUG] {Player} twitch {first.Url}", launches[0].MessageText);
        Assert.Equal(second.Title, launches[1].DisplayName);
        Assert.Contains($"[DEBUG] {Player} youtube {second.Url}", launches[1].MessageText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DropAFailedVodQueryOnceThePageIsLeft(bool unknownStream)
    {
        var provider = Substitute.For<IApiClient>();
        provider.HasVodViewerSupport.Returns(true);
        var query = new TaskCompletionSource<IReadOnlyCollection<VodDetails>>();
        provider.GetVods(Arg.Any<VodQuery>()).Returns(query.Task);
        var factory = Substitute.For<IApiClientFactory>();
        factory.GetAll().Returns([provider]);
        var streams = Substitute.For<IMonitorStreamsModel>();
        streams.Livestreams.Returns(new ObservableList<LivestreamModel>());
        var settings = Substitute.For<ISettingsHandler>();
        settings.Settings.Returns(new Settings());
        var launcher = new StreamLauncher(settings, Substitute.For<IWindowManager>());
        var viewModel = new VodListViewModel(launcher, streams, factory);

        // The query runs from an async void page change, so what it throws reaches the synchronization context, where
        // WPF's dispatcher would crash the application.
        var context = new RecordingContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);

        try
        {
            await ((IActivate)viewModel).ActivateAsync(CancellationToken.None);
            viewModel.SelectedApiClient = provider;
            viewModel.StreamDisplayName = "channel";
            await ((IDeactivate)viewModel).DeactivateAsync(close: true, CancellationToken.None);
            Exception failure = unknownStream
                ? new HttpRequestWithStatusException(HttpStatusCode.NotFound, "Unknown stream.")
                : new InvalidOperationException("The provider is unavailable.");

            query.SetException(failure);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        Assert.Empty(context.Errors);
    }

    [Fact]
    public async Task DiscardVodResultsAfterChangingOrClearingTheChannel()
    {
        var provider = Substitute.For<IApiClient>();
        var oldRequest = new TaskCompletionSource<IReadOnlyCollection<VodDetails>>();
        var clearedRequest = new TaskCompletionSource<IReadOnlyCollection<VodDetails>>();
        var currentVod = new VodDetails { Title = "Current VOD" };
        var currentRequest = Task.FromResult<IReadOnlyCollection<VodDetails>>([currentVod]);
        provider.GetVods(Arg.Any<VodQuery>()).Returns(oldRequest.Task, currentRequest, clearedRequest.Task);
        var streams = Substitute.For<IMonitorStreamsModel>();
        streams.Livestreams.Returns(new ObservableList<LivestreamModel>());
        var settings = Substitute.For<ISettingsHandler>();
        settings.Settings.Returns(new Settings());
        var launcher = new StreamLauncher(settings, Substitute.For<IWindowManager>());
        var viewModel = new VodListViewModel(launcher, streams, Substitute.For<IApiClientFactory>());
        var context = new RecordingContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);

        try
        {
            await ((IActivate)viewModel).ActivateAsync(CancellationToken.None);
            viewModel.SelectedApiClient = provider;
            viewModel.StreamDisplayName = "old";
            viewModel.StreamDisplayName = "current";
            oldRequest.SetResult([new VodDetails { Title = "Old VOD" }]);

            Assert.Same(currentVod, Assert.Single(viewModel.Items));

            viewModel.StreamDisplayName = "about to clear";
            viewModel.StreamDisplayName = "";
            clearedRequest.SetResult([new VodDetails { Title = "Cleared VOD" }]);

            Assert.Empty(viewModel.Items);
            Assert.False(viewModel.LoadingItems);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        Assert.Empty(context.Errors);
    }

    [Fact]
    public void OfferTheSelectedProvidersVodTypes()
    {
        // The view lists the types through a binding, which only re-reads them when told they changed.
        var provider = Substitute.For<IApiClient>();
        provider.VodTypes.Returns(["archive", "highlight"]);
        var streams = Substitute.For<IMonitorStreamsModel>();
        streams.Livestreams.Returns(new ObservableList<LivestreamModel>());
        var settings = Substitute.For<ISettingsHandler>();
        settings.Settings.Returns(new Settings());
        var launcher = new StreamLauncher(settings, Substitute.For<IWindowManager>());
        var viewModel = new VodListViewModel(launcher, streams, Substitute.For<IApiClientFactory>());
        var changed = new List<string>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        viewModel.SelectedApiClient = provider;

        Assert.Contains(nameof(VodListViewModel.VodTypes), changed);
        Assert.Equal(["archive", "highlight"], viewModel.VodTypes);
        Assert.Equal("archive", viewModel.SelectedVodType);
    }

    [Fact]
    public void ListTheProvidersKnownStreamsAlphabetically()
    {
        var provider = Substitute.For<IApiClient>();
        // The ids sort in another order, so only ordering by display name passes.
        var livestreams = new ObservableList<LivestreamModel> { Stream("1", "charlie"), Stream("3", "Alpha"), Stream("2", "bravo") };
        var streams = Substitute.For<IMonitorStreamsModel>();
        streams.Livestreams.Returns(livestreams);
        var settings = Substitute.For<ISettingsHandler>();
        settings.Settings.Returns(new Settings());
        var launcher = new StreamLauncher(settings, Substitute.For<IWindowManager>());
        var viewModel = new VodListViewModel(launcher, streams, Substitute.For<IApiClientFactory>()) { SelectedApiClient = provider };

        Assert.Equal(["Alpha", "bravo", "charlie"], viewModel.KnownStreams.Select(stream => stream.DisplayName));

        LivestreamModel Stream(string id, string displayName) =>
            new(id, new ChannelIdentifier(provider, id)) { DisplayName = displayName };
    }

    // Runs posted work at once and keeps what it throws.
    private sealed class RecordingContext : SynchronizationContext
    {
        public List<Exception> Errors { get; } = [];

        public override void Post(SendOrPostCallback callback, object state)
        {
            try
            {
                callback(state);
            }
            catch (Exception exception)
            {
                Errors.Add(exception);
            }
        }
    }
}
