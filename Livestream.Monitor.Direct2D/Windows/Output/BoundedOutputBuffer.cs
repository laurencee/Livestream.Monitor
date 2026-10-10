namespace Livestream.Monitor.Direct2D.Windows.Output;

internal sealed class BoundedOutputBuffer
{
    public const int DefaultCapacity = 32 * 1024;
    private readonly Lock sync = new();
    private readonly char[] buffer;
    private int start;
    private int length;
    private long discardedCharacters;
    private bool dirty;

    public BoundedOutputBuffer(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        buffer = new char[capacity];
    }

    public void Append(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty) return;

        // Copying and snapshotting share the lock so concurrent readers cannot interleave partial chunks.
        lock (sync)
        {
            var discard = Math.Max(0L, (long)length + text.Length - buffer.Length);
            discardedCharacters += discard;

            if (discard >= length)
            {
                text = text[(int)(discard - length)..];
                start = 0;
                length = 0;
            }
            else
            {
                start = (int)((start + discard) % buffer.Length);
                length -= (int)discard;
            }

            var tail = (int)(((long)start + length) % buffer.Length);
            var firstPart = Math.Min(text.Length, buffer.Length - tail);
            text[..firstPart].CopyTo(buffer.AsSpan(tail));
            text[firstPart..].CopyTo(buffer);
            length += text.Length;

            if (discard != 0 && char.IsLowSurrogate(buffer[start]))
            {
                start = (start + 1) % buffer.Length;
                length--;
                discardedCharacters++;
            }

            dirty = true;
        }
    }

    public bool TryRead(out OutputSnapshot snapshot)
    {
        lock (sync)
        {
            if (!dirty)
            {
                snapshot = default;
                return false;
            }

            var text = string.Create(length, (buffer, start), static (destination, state) =>
            {
                var firstPart = Math.Min(destination.Length, state.buffer.Length - state.start);
                state.buffer.AsSpan(state.start, firstPart).CopyTo(destination);
                state.buffer.AsSpan(0, destination.Length - firstPart).CopyTo(destination[firstPart..]);
            });

            snapshot = new OutputSnapshot(text, discardedCharacters);
            dirty = false;
            return true;
        }
    }
}

/// <summary>Retained UTF-16 text and its absolute character offset in the appended output.</summary>
internal readonly record struct OutputSnapshot(string Text, long DiscardedCharacters);
