using System;
using System.Threading;
using System.Threading.Tasks;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.Model.Monitoring;
using Livestream.Monitor.Tests.TestFramework;
using Livestream.Monitor.WPF.Core.UI;
using Livestream.Monitor.WPF.Model;
using Livestream.Monitor.WPF.ViewModels;
using NSubstitute;
using Xunit;
using INavigationService = Livestream.Monitor.WPF.Core.INavigationService;

namespace Livestream.Monitor.Tests.ViewModels;

public class ShellViewModelShould
{
    [Fact]
    public Task ShowTheNextPageWithoutPassingThroughTheMainPage() => StaThread.Run(async () =>
    {
        // Showing the main page in passing reloads its livestream list view after its sort has been cleared again.
        var (shell, main) = await CreateActivatedShell();
        var first = new FirstPage();
        var second = new SecondPage();
        await shell.HandleAsync(new ActivateScreen(first), CancellationToken.None);
        var mainActivations = 0;
        main.Activated += (_, _) => mainActivations++;

        await shell.HandleAsync(new ActivateScreen(second), CancellationToken.None);

        Assert.Same(second, shell.ActiveItem);
        Assert.Equal(0, mainActivations);
        Assert.False(first.IsActive);
        Assert.DoesNotContain(first, shell.Items);
    });

    private static async Task<(ShellViewModel Shell, MainViewModel Main)> CreateActivatedShell()
    {
        var settings = Substitute.For<ISettingsHandler>();
        settings.Settings.Returns(new Settings());
        var streams = Substitute.For<IMonitorStreamsModel>();
        streams.Livestreams.Returns(new ObservableList<LivestreamModel>());
        streams.LastRefreshTime.Returns(DateTimeOffset.Now);
        var apiClients = Substitute.For<IApiClientFactory>();
        var windows = Substitute.For<IWindowManager>();
        var launcher = new StreamLauncher(settings, windows);
        var filter = new FilterModel();
        var navigation = Substitute.For<INavigationService>();
        var header = new HeaderViewModel(
            monitorStreamsModel: streams,
            streamLauncher: launcher,
            filterModelModel: filter,
            apiClientFactory: apiClients);

        var list = new LivestreamListViewModel(
            monitorStreamsModel: streams,
            filterModel: filter,
            streamLauncher: launcher,
            navigationService: navigation,
            settingsHandler: settings);

        var main = new MainViewModel(header, list);
        var settingsPage = new SettingsViewModel(settings, new ThemeSelectorViewModel(settings));
        var popular = new PopularLivestreamWatcher(
            settingsHandler: settings,
            notificationHandler: Substitute.For<INotificationHandler>(),
            openStream: _ => { },
            apiClientFactory: apiClients);

        var shell = new ShellViewModel(
            settingsViewModel: settingsPage,
            mainViewModel: main,
            eventAggregator: Substitute.For<IEventAggregator>(),
            navigationService: navigation,
            monitorStreamsModel: streams,
            settingsHandler: settings,
            popularLivestreamWatcher: popular);

        await ((IActivate)shell).ActivateAsync(CancellationToken.None);
        return (shell, main);
    }

    private sealed class FirstPage : Screen;

    private sealed class SecondPage : Screen;
}
