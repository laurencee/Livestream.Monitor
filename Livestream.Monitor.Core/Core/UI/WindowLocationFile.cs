using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Livestream.Monitor.Core.UI;

/// <summary>The top-left corner of a window in virtual-screen coordinates.</summary>
internal readonly record struct WindowLocation([property: JsonRequired] int X, [property: JsonRequired] int Y);

internal static class WindowLocationFile
{
    public const string FileName = "window-position.json";

    /// <summary>Returns false when the position file is inaccessible and must be preserved.</summary>
    public static bool TryLoad(string path, out WindowLocation? location)
    {
        location = null;
        try
        {
            using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            location = JsonSerializer.Deserialize<WindowLocation?>(file);
            return true;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException or JsonException)
        {
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void Save(string path, WindowLocation location)
    {
        var temporaryPath = $"{path}.{Path.GetRandomFileName()}";
        bool created = false;
        try
        {
            new FileInfo(path).Directory?.Create();

            using (var file = File.Open(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                JsonSerializer.Serialize(file, location);
            }

            // Allow concurrent readers while saving window placement.
            File.Move(temporaryPath, path, overwrite: true);
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
                // Saving the placement is optional, so ignore a temporary file that cannot be deleted.
            }
        }
    }
}
