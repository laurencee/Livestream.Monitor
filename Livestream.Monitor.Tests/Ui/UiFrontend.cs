using System;
using System.Windows.Automation;
using Livestream.Monitor.Direct2D;
using Livestream.Monitor.WPF;

namespace Livestream.Monitor.Tests.Ui;

public enum FrontendKind
{
    Wpf,
    Direct2D,
}

/// <summary>
/// How a frontend is started, and the automation ids of the controls UI tests use, so one scenario checks both
/// frontends.
/// </summary>
internal sealed class UiFrontend
{
    private static readonly UiFrontend Wpf = new()
    {
        Executable = "Livestream.Monitor.WPF.exe",
        InstanceName = App.Unique,
        Arguments = profile => ["--data-directory", profile.DataDirectory],
        AcceptMessage = "PART_AffirmativeButton",
        DeclinePrompt = "PART_NegativeButton",
        NameFilter = "FilterModel_LivestreamNameFilter",
        ChannelToAdd = "StreamName",
        AddChannel = "AddStream",
        NameHeader = new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.HeaderItem),
            new PropertyCondition(AutomationElement.NameProperty, "Name")),
        TopStreams = "TopStreams",
        Providers = "ApiClients",
        TopStreamTile = "StreamTile",
        NextPage = "Next",
        VodViewer = "VodViewer",
        KnownChannelsOpener = "KnownStreams",
        KnownChannels = "KnownStreams",
        VodTile = "VodTile",
        OpenSettings = "ShowSettings",
        CheckForNewVersions = "CheckForNewVersions",
        SaveSettings = "Save",
    };

    private static readonly UiFrontend Direct2D = new()
    {
        Executable = "Livestream.Monitor.Direct2D.exe",
        InstanceName = Program.InstanceName,
        Arguments = profile => ["--data-directory", profile.DataDirectory, "--quiet"],
        AcceptMessage = "dialog.accept",
        DeclinePrompt = "dialog.cancel",
        NameFilter = "filter",
        ChannelToAdd = "channel",
        AddChannel = "add",
        NameHeader = new PropertyCondition(AutomationElement.AutomationIdProperty, "column.1"),
        TopStreams = "top",
        Providers = "provider",
        TopStreamTile = "tile.0",
        NextPage = "page.next",
        VodViewer = "vod",
        KnownChannelsOpener = "vod.channels",
        KnownChannels = "vod.known",
        VodTile = "tile.0",
        OpenSettings = "settings",
        CheckForNewVersions = "settings.versions",
        SaveSettings = "settings.save",
    };

    public required string Executable { get; init; }
    public required string InstanceName { get; init; }
    public required Func<UiTestProfile, string[]> Arguments { get; init; }

    /// <summary>The affirmative button of an in-window message or prompt.</summary>
    public required string AcceptMessage { get; init; }

    public required string DeclinePrompt { get; init; }

    public required string NameFilter { get; init; }
    public required string ChannelToAdd { get; init; }
    public required string AddChannel { get; init; }
    public required Condition NameHeader { get; init; }
    public required string TopStreams { get; init; }
    public required string Providers { get; init; }

    /// <summary>The first tile on its page.</summary>
    public required string TopStreamTile { get; init; }

    public required string NextPage { get; init; }
    public required string VodViewer { get; init; }

    /// <summary>Opens the followed channels list on the VOD page, by expanding or invoking it.</summary>
    public required string KnownChannelsOpener { get; init; }

    public required string KnownChannels { get; init; }

    /// <summary>The first tile on its page.</summary>
    public required string VodTile { get; init; }

    public required string OpenSettings { get; init; }
    public required string CheckForNewVersions { get; init; }
    public required string SaveSettings { get; init; }

    public static UiFrontend Of(FrontendKind kind) => kind == FrontendKind.Wpf ? Wpf : Direct2D;
}
