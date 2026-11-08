using System;
using System.Threading.Tasks;
using Livestream.Monitor.Direct2D.Windows.Output;
using Xunit;

namespace Livestream.Monitor.Tests.Direct2D;

public class BoundedOutputBufferShould
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RequirePositiveCapacity(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedOutputBuffer(capacity));
    }

    [Fact]
    public void RetainTheNewestTextWithinTheDefaultCapacity()
    {
        var buffer = new BoundedOutputBuffer();
        var text = new string('x', BoundedOutputBuffer.DefaultCapacity) + "y";
        buffer.Append(text);

        Assert.True(buffer.TryRead(out var snapshot));
        Assert.Equal(text[1..], snapshot.Text);
        Assert.Equal(1, snapshot.DiscardedCharacters);
    }

    [Fact]
    public void ReportOnlyChangesAndRetainHistoryAcrossRingWraparound()
    {
        const int Capacity = 7;
        var buffer = new BoundedOutputBuffer(Capacity);
        Assert.False(buffer.TryRead(out _));
        buffer.Append(ReadOnlySpan<char>.Empty);
        Assert.False(buffer.TryRead(out _));

        buffer.Append("ab\r\n");
        Assert.True(buffer.TryRead(out var first));
        Assert.Equal("ab\r\n", first.Text);
        Assert.Equal(0, first.DiscardedCharacters);
        Assert.False(buffer.TryRead(out _));
        buffer.Append(ReadOnlySpan<char>.Empty);
        Assert.False(buffer.TryRead(out _));

        buffer.Append("c\n");
        Assert.True(buffer.TryRead(out var combined));
        Assert.Equal("ab\r\nc\n", combined.Text);
        Assert.Equal(0, combined.DiscardedCharacters);
        Assert.False(buffer.TryRead(out _));

        buffer.Append("de");
        Assert.True(buffer.TryRead(out var wrapped));
        Assert.Equal("b\r\nc\nde", wrapped.Text);
        Assert.Equal(1, wrapped.DiscardedCharacters);

        buffer.Append("fgh");
        Assert.True(buffer.TryRead(out var latest));
        Assert.Equal("c\ndefgh", latest.Text);
        Assert.Equal(4, latest.DiscardedCharacters);
        Assert.Equal("ab\r\n", first.Text);
        Assert.Equal("b\r\nc\nde", wrapped.Text);
        Assert.False(buffer.TryRead(out _));
    }

    [Fact]
    public void DiscardOldHistoryAndThePrefixOfAnOversizedChunk()
    {
        const int Capacity = 5;
        var buffer = new BoundedOutputBuffer(Capacity);
        buffer.Append("old");
        buffer.Append("0123456789");

        Assert.True(buffer.TryRead(out var snapshot));
        Assert.Equal("56789", snapshot.Text);
        Assert.Equal(8, snapshot.DiscardedCharacters);

        buffer.Append("z");
        Assert.True(buffer.TryRead(out var next));
        Assert.Equal("6789z", next.Text);
        Assert.Equal(9, next.DiscardedCharacters);
    }

    [Fact]
    public void AvoidAllocatingWhenNothingHasChanged()
    {
        var buffer = new BoundedOutputBuffer();
        buffer.Append("output");
        Assert.True(buffer.TryRead(out _));
        Assert.False(buffer.TryRead(out _));

        const int ReadCount = 1000;
        var anyChanges = false;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < ReadCount; i++) anyChanges |= buffer.TryRead(out _);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.False(anyChanges);
        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData("A\uD83D\uDE00", "B", "\uD83D\uDE00B", 1)]
    [InlineData("\uD83D\uDE00B", "C", "BC", 2)]
    [InlineData("old", "A\uD83D\uDE00BC", "BC", 6)]
    public void AvoidLeavingALowSurrogateAtTheDiscardBoundary(
        string first,
        string second,
        string expected,
        long discarded)
    {
        const int Capacity = 3;
        var buffer = new BoundedOutputBuffer(Capacity);
        buffer.Append(first);
        buffer.Append(second);

        Assert.True(buffer.TryRead(out var snapshot));
        Assert.Equal(expected, snapshot.Text);
        Assert.Equal(discarded, snapshot.DiscardedCharacters);
    }

    [Fact]
    public void AccountForASurrogatePairThatCannotFitInOneCharacter()
    {
        var buffer = new BoundedOutputBuffer(capacity: 1);
        buffer.Append("\uD83D");
        buffer.Append("\uDE00");

        Assert.True(buffer.TryRead(out var empty));
        Assert.Empty(empty.Text);
        Assert.Equal(2, empty.DiscardedCharacters);

        buffer.Append("x");
        Assert.True(buffer.TryRead(out var latest));
        Assert.Equal("x", latest.Text);
        Assert.Equal(2, latest.DiscardedCharacters);
    }

    [Fact]
    public async Task KeepConcurrentChunksWholeAndDiscardCountsMonotonic()
    {
        const int WriterCount = 4;
        const int AppendCount = 256;
        const int ChunkLength = 4;
        const int Capacity = 64;
        var buffer = new BoundedOutputBuffer(Capacity);
        var writers = new Task[WriterCount];

        for (var i = 0; i < writers.Length; i++)
        {
            var chunk = new string((char)('A' + i), ChunkLength);
            writers[i] = Task.Run(() =>
            {
                for (var iteration = 0; iteration < AppendCount; iteration++) buffer.Append(chunk);
            });
        }

        var completion = Task.WhenAll(writers);
        var latest = new OutputSnapshot("", 0);

        while (!completion.IsCompleted)
        {
            ReadAvailable();
            await Task.Yield();
        }

        await completion;
        ReadAvailable();
        Assert.Equal(Capacity, latest.Text.Length);
        Assert.Equal(WriterCount * AppendCount * ChunkLength, latest.DiscardedCharacters + latest.Text.Length);

        void ReadAvailable()
        {
            if (!buffer.TryRead(out var snapshot)) return;

            Assert.InRange(snapshot.Text.Length, ChunkLength, Capacity);
            Assert.Equal(0, snapshot.Text.Length % ChunkLength);
            Assert.True(snapshot.DiscardedCharacters >= latest.DiscardedCharacters);
            Assert.Equal(0, snapshot.DiscardedCharacters % ChunkLength);

            for (var i = 0; i < snapshot.Text.Length; i += ChunkLength)
            {
                var character = snapshot.Text[i];
                Assert.InRange(character, 'A', 'D');
                for (var offset = 1; offset < ChunkLength; offset++) Assert.Equal(character, snapshot.Text[i + offset]);
            }

            latest = snapshot;
        }
    }
}
