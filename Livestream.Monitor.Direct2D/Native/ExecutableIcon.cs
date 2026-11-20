using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Livestream.Monitor.Direct2D.Native;

internal static partial class ExecutableIcon
{
    // The C# compiler assigns the application icon group ID 32512. Its directory refers to RT_ICON resources by ID.
    // https://devblogs.microsoft.com/oldnewthing/20120720-00/?p=7083
    private const int ApplicationIconId = 32512;
    private const int GroupIconResource = 14;
    private const int IconResource = 3;
    private const int HeaderSize = 6;
    private const int EntryPrefixSize = 12;
    private const int GroupEntrySize = EntryPrefixSize + sizeof(ushort);
    private const int FileEntrySize = EntryPrefixSize + sizeof(uint);

    internal static void WriteTo(Stream file, nint module)
    {
        var group = GetResource(module, ApplicationIconId, GroupIconResource);
        if (group.Length < HeaderSize) throw new InvalidDataException("Missing icon group header.");

        const int CountOffset = 4;
        var count = BinaryPrimitives.ReadUInt16LittleEndian(group[CountOffset..]);
        if (count == 0 || group.Length < HeaderSize + count * GroupEntrySize) throw new InvalidDataException("Incomplete icon group.");

        // An ICO entry holds a file offset where the resource entry holds an ID. Keep every frame so the Shell can pick one by DPI.
        var directory = new byte[HeaderSize + count * FileEntrySize];
        group[..HeaderSize].CopyTo(directory);
        file.Position = directory.Length;

        for (var i = 0; i < count; i++)
        {
            var source = group.Slice(HeaderSize + i * GroupEntrySize, GroupEntrySize);
            var entry = directory.AsSpan(HeaderSize + i * FileEntrySize, FileEntrySize);
            var id = BinaryPrimitives.ReadUInt16LittleEndian(source[EntryPrefixSize..]);
            var frame = GetResource(module, id, IconResource);
            const int SizeOffset = 8;
            if (frame.Length != BinaryPrimitives.ReadUInt32LittleEndian(source[SizeOffset..])) throw new InvalidDataException("Invalid icon frame size.");

            source[..EntryPrefixSize].CopyTo(entry);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[EntryPrefixSize..], checked((uint)file.Position));
            file.Write(frame);
        }

        file.Position = 0;
        file.Write(directory);
    }

    private static unsafe ReadOnlySpan<byte> GetResource(nint module, int id, int type)
    {
        var resource = FindResource(module, id, type);
        if (resource == 0) throw new IOException($"Missing executable icon resource {id}.");

        var size = SizeofResource(module, resource);
        var address = LockResource(LoadResource(module, resource));
        if (address == null || size == 0) throw new IOException($"Could not load executable icon resource {id}.");

        return new ReadOnlySpan<byte>(address, checked((int)size));
    }

    [LibraryImport("kernel32.dll", EntryPoint = "FindResourceW")]
    private static partial nint FindResource(nint module, nint name, nint type);

    [LibraryImport("kernel32.dll")]
    private static partial uint SizeofResource(nint module, nint resource);

    [LibraryImport("kernel32.dll")]
    private static partial nint LoadResource(nint module, nint resource);

    [LibraryImport("kernel32.dll")]
    private static unsafe partial void* LockResource(nint resource);
}
