using System;
using System.IO;

namespace Livestream.Monitor.Core;

/// <summary>Moves machine-local files that earlier versions kept in the data directory.</summary>
internal static class LegacyDataFile
{
    /// <summary>Moves <paramref name="fileName"/> from the current directory to <paramref name="destinationPath"/>, or deletes it when a file is already there.</summary>
    public static void Move(string fileName, string destinationPath)
    {
        if (!File.Exists(fileName)) return;

        // An application run from the destination folder already has the file in place.
        var destination = new FileInfo(destinationPath);
        if (string.Equals(Path.GetFullPath(fileName), destination.FullName, StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            // Once the new location holds the file, the legacy file is no longer used.
            if (destination.Exists)
            {
                File.Delete(fileName);
            }
            else
            {
                destination.Directory?.Create();
                File.Move(fileName, destination.FullName);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The next start retries. If the new location has a file by then, the legacy file is deleted instead of moved.
        }
    }
}
