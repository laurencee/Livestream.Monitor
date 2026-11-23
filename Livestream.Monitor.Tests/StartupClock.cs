using System;
using System.Threading;

namespace Livestream.Monitor.Tests;

/// <summary>A fake clock for tests: time only passes when the test calls <see cref="Advance"/>.</summary>
internal sealed class StartupClock : TimeProvider
{
    private long timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Interlocked.Read(ref timestamp);
    public void Advance(TimeSpan duration) => Interlocked.Add(ref timestamp, duration.Ticks);
}
