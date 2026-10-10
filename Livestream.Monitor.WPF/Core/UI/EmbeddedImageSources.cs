using System.Windows.Media;
using System.Windows.Media.Imaging;
using Livestream.Monitor.Core;

namespace Livestream.Monitor.WPF.Core.UI;

/// <summary>WPF image sources of the <see cref="EmbeddedImages"/>, for use from XAML through x:Static.</summary>
public static class EmbeddedImageSources
{
    public static ImageSource Live { get; } = Load(EmbeddedImages.Live);

    public static ImageSource Offline { get; } = Load(EmbeddedImages.Offline);

    /// <summary>A frozen image of an <see cref="EmbeddedImages"/> name, or null when there is no such image.</summary>
    public static BitmapImage Load(string name)
    {
        using var stream = EmbeddedImages.Open(name);
        if (stream == null) return null;

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        // Decode now so the resource stream can be closed.
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
