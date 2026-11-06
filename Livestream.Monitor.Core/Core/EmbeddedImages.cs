using System.IO;

namespace Livestream.Monitor.Core
{
    /// <summary>
    /// Images shared by the frontends, embedded in this assembly so frontends without WPF resources can load them.
    /// </summary>
    public static class EmbeddedImages
    {
        public const string Live = "Images.Live.png";
        public const string Offline = "Images.Offline.png";
        public const string TrayIcon = "Images.TrayIcon.ico";
        internal const string FlagAtlas = "Images.CountryFlagAtlas.png";
        internal const string GuernseyFlag = "Images.CountryFlags.gg.png";

        /// <summary>Finds the flag of a broadcaster language or country code.</summary>
        internal static FlagRegion? FindFlag(string language)
        {
            if (string.IsNullOrWhiteSpace(language)) return null;

            var tile = GetLanguageTile(language);
            if (tile == CountryFlagAtlas.NoAlias) tile = GetCountryTile(language.ToLowerInvariant());
            if (tile == CountryFlagAtlas.Missing) return null;
            if (tile == CountryFlagAtlas.Standalone) return FlagRegion.Standalone;

            var x = tile % CountryFlagAtlas.Columns * CountryFlagAtlas.TileWidth;
            var y = tile / CountryFlagAtlas.Columns * CountryFlagAtlas.TileHeight;
            var (width, height) = CountryFlagAtlas.GetSize(tile);
            return new FlagRegion(x, y, width, height);
        }

        /// <summary>Opens an embedded image, or returns null when there is no image of that name.</summary>
        public static Stream Open(string name) => typeof(EmbeddedImages).Assembly.GetManifestResourceStream(name);

        private static byte GetLanguageTile(string language)
        {
            var index = GetCodeIndex(language);
            return index >= 0 ? CountryFlagAtlas.LanguageTiles[index] : CountryFlagAtlas.GetNamedLanguageTile(language);
        }

        private static byte GetCountryTile(string code)
        {
            var index = GetCodeIndex(code);
            return index >= 0 ? CountryFlagAtlas.CountryTiles[index] : CountryFlagAtlas.GetNamedTile(code);
        }

        private static int GetCodeIndex(string code)
        {
            if (code.Length != 2) return -1;

            const int AlphabetSize = 26;
            var first = code[0] - 'a';
            var second = code[1] - 'a';
            return (uint)first < AlphabetSize && (uint)second < AlphabetSize ? first * AlphabetSize + second : -1;
        }
    }

    internal readonly record struct FlagRegion(int X, int Y, int Width, int Height)
    {
        internal static FlagRegion Standalone { get; } = new(-1, 0, 0, 0);

        internal bool IsStandalone => this == Standalone;
    }
}
