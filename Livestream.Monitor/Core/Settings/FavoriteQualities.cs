using System.Collections.Generic;
using System.Linq;

namespace Livestream.Monitor.Core;

public class FavoriteQualities
{
    public static class FallbackQualityOption
    {
        public const string Worst = "Worst";
        public const string Best = "Best";
    }

    public List<string> Qualities { get; set; } = [];

    public string FallbackQuality { get; set; } = FallbackQualityOption.Best;

    public override bool Equals(object obj)
    {
        var qualities = obj as FavoriteQualities;
        return qualities != null &&
               Qualities.SequenceEqual(qualities.Qualities) &&
               FallbackQuality == qualities.FallbackQuality;
    }

    public override int GetHashCode()
    {
        var hashCode = 643755790;
        hashCode = hashCode * -1521134295 + EqualityComparer<List<string>>.Default.GetHashCode(Qualities);
        hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(FallbackQuality);
        return hashCode;
    }
}