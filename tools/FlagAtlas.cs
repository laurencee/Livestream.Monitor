#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property ImplicitUsings=disable
#:property PublishAot=false

// Regenerate the checked-in assets with .NET 10 and OxiPNG 10.0.0.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

internal static class FlagAtlas
{
    private const int TileWidth = 16;
    private const int TileHeight = 12;
    private const int StandardHeight = 11;
    private const int BytesPerPixel = 4;
    private const int MaxTextureSize = 4096;
    private const byte NoAlias = 253;
    private const byte Standalone = 254;
    private const byte Missing = 255;
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var check = false;
            var oxipng = "oxipng";
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--check") check = true;
                else if (args[i] == "--oxipng" && i + 1 < args.Length) oxipng = args[++i];
                else throw new ArgumentException("Usage: dotnet run --file tools/FlagAtlas.cs -- [--check] [--oxipng <path>]");
            }

            var tools = Path.GetDirectoryName(SourcePath())!;
            var root = Path.GetFullPath("..", tools);
            var core = Path.Combine(root, "Livestream.Monitor.Core");
            var images = Path.Combine(core, "Images");
            var slots = new Dictionary<string, byte>(StringComparer.Ordinal);
            var tiles = ReadTiles(images, slots);
            QuantizeColors(tiles);
            var (columns, atlas) = BuildAtlas(tiles, oxipng);
            var index = BuildIndex(images, slots, tiles, columns);
            var imagePath = Path.Combine(images, "CountryFlagAtlas.png");
            var indexPath = Path.Combine(core, "Core", "CountryFlagAtlas.g.cs");
            if (check)
            {
                if (!File.ReadAllBytes(imagePath).AsSpan().SequenceEqual(atlas)) throw new InvalidDataException($"Regenerate {imagePath}");
                if (File.ReadAllText(indexPath).ReplaceLineEndings("\n") != index.ReplaceLineEndings("\n"))
                    throw new InvalidDataException($"Regenerate {indexPath}");
            }
            else
            {
                File.WriteAllBytes(imagePath, atlas);
                File.WriteAllText(indexPath, index.ReplaceLineEndings(Environment.NewLine));
            }

            Console.WriteLine($"{slots.Count - 1} flags, {tiles.Count} tiles, {columns} columns, {atlas.Length} bytes");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Flag atlas: {exception.Message}");
            return 1;
        }
    }

    private static string SourcePath([CallerFilePath] string path = "") => path;

    private static List<Tile> ReadTiles(string images, Dictionary<string, byte> slots)
    {
        var tiles = new List<Tile>();
        var paths = Directory.EnumerateFiles(Path.Combine(images, "CountryFlags"), "*.png").Order(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var code = Path.GetFileNameWithoutExtension(path);
            if (code.Length < 2 || !IsLowercaseAscii(code)) throw new InvalidDataException($"Invalid flag code: {code}");
            if (code == "gg")
            {
                slots.Add(code, Standalone);
                continue;
            }

            var data = File.ReadAllBytes(path);
            ValidateMetadata(data, code);
            var tile = Decode(data);
            if (tile.Width > TileWidth || tile.Height > TileHeight) throw new InvalidDataException($"{code} exceeds the tile size");

            var index = tiles.FindIndex(other => other.Width == tile.Width && other.Height == tile.Height &&
                                                 other.Pixels.AsSpan().SequenceEqual(tile.Pixels));
            if (index < 0)
            {
                index = tiles.Count;
                if (index >= NoAlias) throw new InvalidDataException("Too many unique flag tiles");

                tiles.Add(tile);
            }

            slots.Add(code, (byte)index);
        }

        if (!slots.ContainsKey("gg") || tiles.Count == 0) throw new InvalidDataException("Unexpected flag inventory");
        return tiles;
    }

    private static void ValidateMetadata(ReadOnlySpan<byte> png, string code)
    {
        const int PngSignatureLength = 8;
        const int ChunkOverhead = 12;
        const uint SourceGamma = 45_000;
        var remaining = png[PngSignatureLength..];
        while (!remaining.IsEmpty)
        {
            if (remaining.Length < ChunkOverhead) throw new InvalidDataException($"{code} has a truncated PNG chunk");

            var length = BinaryPrimitives.ReadUInt32BigEndian(remaining);
            if (length > remaining.Length - ChunkOverhead) throw new InvalidDataException($"{code} has a truncated PNG chunk");

            var type = remaining.Slice(4, 4);
            var gammaMismatch = type.SequenceEqual("gAMA"u8) &&
                                (length != sizeof(uint) || BinaryPrimitives.ReadUInt32BigEndian(remaining[8..]) != SourceGamma);
            if (gammaMismatch || type.SequenceEqual("iCCP"u8) || type.SequenceEqual("pHYs"u8) || type.SequenceEqual("sRGB"u8) ||
                type.SequenceEqual("cHRM"u8) || type.SequenceEqual("cICP"u8))
                throw new InvalidDataException($"{code} has rendering metadata that needs separate handling");

            remaining = remaining[(ChunkOverhead + (int)length)..];
        }
    }

    private static Tile Decode(byte[] png)
    {
        using var stream = new MemoryStream(png, writable: false);
        using var bitmap = new Bitmap(stream, useIcm: false);
        var bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = bitmap.Width * BytesPerPixel;
            var pixels = new byte[bitmap.Height * stride];
            for (var row = 0; row < bitmap.Height; row++) Marshal.Copy(data.Scan0 + row * data.Stride, pixels, row * stride, stride);

            return new Tile(bitmap.Width, bitmap.Height, pixels);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static void QuantizeColors(List<Tile> tiles)
    {
        // Reserve exact RGBA entries for antialiased edges and transparent atlas padding.
        HashSet<int> reservedColors = [0];
        List<int> opaqueSamples = [];

        foreach (var tile in tiles)
        {
            foreach (var color in MemoryMarshal.Cast<byte, int>(tile.Pixels.AsSpan()))
            {
                if (Color.FromArgb(color).A == byte.MaxValue) opaqueSamples.Add(color);
                else reservedColors.Add(color);
            }
        }

        const int PaletteSize = 256;
        var capacity = PaletteSize - reservedColors.Count;
        if (capacity < 0 || capacity == 0 && opaqueSamples.Count > 0) throw new InvalidDataException("Too many translucent colors for the flag palette");
        if (opaqueSamples.Count == 0) return;

        var opaque = opaqueSamples.ToArray();
        var palette = BuildPalette(opaque, capacity);
        var nearest = opaque.Distinct().ToDictionary(color => color, color => NearestColor(Color.FromArgb(color), palette));

        foreach (var tile in tiles)
            foreach (ref var color in MemoryMarshal.Cast<byte, int>(tile.Pixels.AsSpan()))
                if (Color.FromArgb(color).A == byte.MaxValue) color = nearest[color];

        static int NearestColor(Color source, Color[] entries) => entries.MinBy(candidate =>
        {
            var red = source.R - candidate.R;
            var green = source.G - candidate.G;
            var blue = source.B - candidate.B;
            return red * red + green * green + blue * blue;
        }).ToArgb();
    }

    private static Color[] BuildPalette(int[] opaque, int capacity)
    {
        // Median cut splits the most widely dispersed, populous box along its widest channel.
        Func<Color, int>[] channels = [c => c.R, c => c.G, c => c.B];
        var boxes = new PriorityQueue<(Color[] Colors, Func<Color, int> Channel), long>();
        Enqueue(opaque.Select(color => Color.FromArgb(color)).ToArray());

        while (boxes.Count < capacity && boxes.TryPeek(out _, out var priority) && priority < 0)
        {
            var (colors, channel) = boxes.Dequeue();
            var sorted = colors.OrderBy(channel).ToArray();
            var middle = sorted.Length / 2;
            var boundary = channel(sorted[middle]);

            while (middle > 0 && channel(sorted[middle - 1]) == boundary) middle--;
            if (middle == 0)
                while (channel(sorted[middle]) == boundary) middle++;

            Enqueue(sorted[..middle]);
            Enqueue(sorted[middle..]);
        }

        var palette = new Color[boxes.Count];
        var index = 0;

        foreach (var box in boxes.UnorderedItems)
        {
            var colors = box.Element.Colors;
            var red = (int)Math.Round(colors.Average(c => c.R));
            var green = (int)Math.Round(colors.Average(c => c.G));
            var blue = (int)Math.Round(colors.Average(c => c.B));

            palette[index++] = Color.FromArgb(red, green, blue);
        }

        return palette;

        void Enqueue(Color[] colors)
        {
            var (channel, range) = channels.Select(channel => (channel, range: colors.Max(channel) - colors.Min(channel))).MaxBy(pair => pair.range);

            boxes.Enqueue((colors, channel), -(long)range * range * colors.Length);
        }
    }

    private static (int Columns, byte[] Png) BuildAtlas(List<Tile> tiles, string oxipng)
    {
        int[] candidates = [8, 16, 24, 32, 48, 64, 128, MaxTextureSize / TileWidth];
        var bestColumns = 0;
        byte[]? best = null;
        foreach (var columns in candidates.Select(count => Math.Min(count, tiles.Count)).Distinct())
        {
            var width = columns * TileWidth;
            var height = (tiles.Count + columns - 1) / columns * TileHeight;
            if (height > MaxTextureSize) continue;

            var stride = width * BytesPerPixel;
            var pixels = new byte[height * stride];
            for (var i = 0; i < tiles.Count; i++)
            {
                var tile = tiles[i];
                var tileStride = tile.Width * BytesPerPixel;
                var x = i % columns * TileWidth * BytesPerPixel;
                var y = i / columns * TileHeight;
                for (var row = 0; row < tile.Height; row++)
                    tile.Pixels.AsSpan(row * tileStride, tileStride).CopyTo(pixels.AsSpan((y + row) * stride + x));
            }

            using var encoded = new MemoryStream();
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var bounds = new Rectangle(0, 0, width, height);
            var data = bitmap.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (var row = 0; row < height; row++) Marshal.Copy(pixels, row * stride, data.Scan0 + row * data.Stride, stride);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            bitmap.Save(encoded, ImageFormat.Png);

            var png = Optimize(encoded, oxipng);
            var decoded = Decode(png);
            if (decoded.Width != width || decoded.Height != height || !decoded.Pixels.AsSpan().SequenceEqual(pixels))
                throw new InvalidDataException("Optimizer changed the atlas pixels");
            if (best != null && best.Length <= png.Length) continue;

            bestColumns = columns;
            best = png;
        }

        return (bestColumns, best ?? throw new InvalidDataException("Flags do not fit in an atlas"));
    }

    private static byte[] Optimize(MemoryStream png, string oxipng)
    {
        var start = new ProcessStartInfo(oxipng)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        // Strip display metadata to keep the atlas untagged.
        foreach (var argument in new[] { "-o", "max", "--zopfli", "--strip", "pHYs,sRGB,gAMA", "--stdout", "-" }) start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start OxiPNG");
        using var optimized = new MemoryStream();
        var read = process.StandardOutput.BaseStream.CopyToAsync(optimized);
        var errors = process.StandardError.ReadToEndAsync();
        png.Position = 0;
        png.CopyTo(process.StandardInput.BaseStream);
        process.StandardInput.Close();
        process.WaitForExit();
        read.GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new InvalidOperationException(errors.GetAwaiter().GetResult().Trim());

        return optimized.ToArray();
    }

    private static string BuildIndex(
        string images,
        Dictionary<string, byte> slots,
        List<Tile> tiles,
        int columns)
    {
        using var stream = File.OpenRead(Path.Combine(images, "countryIdIsoMappings.json"));
        var mappings = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? throw new InvalidDataException("Missing flag aliases");
        var aliases = mappings.ToDictionary(
            pair => pair.Key,
            pair => slots.GetValueOrDefault(pair.Value.ToLowerInvariant(), Missing),
            StringComparer.Ordinal);
        var code = new StringBuilder();
        code.AppendLine($$"""
            // <auto-generated by tools/FlagAtlas.cs />
            using System;

            namespace Livestream.Monitor.Core;

            internal static class CountryFlagAtlas
            {
                internal const int Columns = {{columns}};
                internal const int TileWidth = {{TileWidth}};
                internal const int TileHeight = {{TileHeight}};
                internal const byte NoAlias = {{NoAlias}};
                internal const byte Standalone = {{Standalone}};
                internal const byte Missing = {{Missing}};

            """);
        AppendTable("CountryTiles", slots, Missing);
        AppendTable("LanguageTiles", aliases, NoAlias);
        AppendNames("GetNamedLanguageTile", "language", aliases, "NoAlias");
        AppendNames("GetNamedTile", "code", slots, "Missing");

        code.AppendLine("    internal static (int Width, int Height) GetSize(byte tile) => tile switch");
        code.AppendLine("    {");
        for (var i = 0; i < tiles.Count; i++)
        {
            var tile = tiles[i];
            if (tile.Width != TileWidth || tile.Height != StandardHeight) code.AppendLine($"        {i} => ({tile.Width}, {tile.Height}),");
        }

        code.AppendLine($"        _ => (TileWidth, {StandardHeight}),");
        code.AppendLine("    };");
        code.AppendLine("}");
        return code.ToString();

        void AppendTable(string name, Dictionary<string, byte> values, byte fallback)
        {
            code.AppendLine($"    internal static ReadOnlySpan<byte> {name} =>");
            code.AppendLine("    [");
            foreach (var first in Alphabet)
            {
                var row = Alphabet.Select(second => values.GetValueOrDefault(new string([first, second]), fallback));
                code.AppendLine($"        {string.Join(", ", row)},");
            }

            code.AppendLine("    ];");
            code.AppendLine();
        }

        void AppendNames(string name, string argument, Dictionary<string, byte> values, string fallback)
        {
            code.AppendLine($"    internal static byte {name}(string {argument}) => {argument} switch");
            code.AppendLine("    {");
            foreach (var (key, value) in values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                if (key.Length != 2 || !IsLowercaseAscii(key)) code.AppendLine($"        {JsonSerializer.Serialize(key)} => {value},");

            code.AppendLine($"        _ => {fallback},");
            code.AppendLine("    };");
            code.AppendLine();
        }
    }

    private static bool IsLowercaseAscii(string value) => value.All(letter => letter is >= 'a' and <= 'z');

    private sealed record Tile(int Width, int Height, byte[] Pixels);
}
