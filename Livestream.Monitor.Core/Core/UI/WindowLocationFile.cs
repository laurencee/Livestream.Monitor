using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Livestream.Monitor.Core.UI;

/// <summary>The top-left corner of a window in virtual-screen coordinates.</summary>
internal readonly record struct WindowLocation([property: JsonRequired] int X, [property: JsonRequired] int Y);

internal static class WindowLocationFile
{
    internal const string FileName = "window-position.json";

    /// <summary>Returns false when the position file is inaccessible and must be preserved.</summary>
    public static bool TryLoad(out WindowLocation? location)
    {
        location = null;
        try
        {
            using var file = File.Open(FileName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            location = JsonSerializer.Deserialize<WindowLocation?>(file);
            return true;
        }
        catch (Exception error) when (error is FileNotFoundException or JsonException)
        {
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void Save(WindowLocation location)
    {
        var temporaryPath = $"{FileName}.{Path.GetRandomFileName()}";
        bool created = false;
        try
        {
            using (var file = File.Open(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                JsonSerializer.Serialize(file, location);
            }

            // Either frontend can read or save placement while the other is running.
            File.Move(temporaryPath, FileName, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (!created) return;

            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException)
            {
                // Placement is optional even if the data directory cannot be written.
            }
        }
    }
}
