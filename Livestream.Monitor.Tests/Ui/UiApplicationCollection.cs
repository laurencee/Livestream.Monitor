using Xunit;

namespace Livestream.Monitor.Tests.Ui;

/// <summary>Runs the UI tests one at a time, since each application's window takes focus while it runs.</summary>
[CollectionDefinition(Name)]
public class UiApplicationCollection
{
    public const string Name = "UI applications";
}
