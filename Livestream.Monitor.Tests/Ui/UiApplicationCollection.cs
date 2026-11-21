using Xunit;

namespace Livestream.Monitor.Tests.Ui;

/// <summary>Runs the UI tests that open desktop windows one at a time; the Settings journeys run alongside them.</summary>
[CollectionDefinition(Name)]
public class UiApplicationCollection
{
    public const string Name = "UI applications";
}
