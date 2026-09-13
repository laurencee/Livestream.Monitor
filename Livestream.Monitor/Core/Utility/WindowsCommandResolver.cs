using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Livestream.Monitor.Core.Utility;

public static class WindowsCommandResolver
{
    public static bool TryResolveExecutable(string inputText, out string resolvedPath)
    {
        resolvedPath = null;
        if (string.IsNullOrWhiteSpace(inputText)) return false;

        var candidateText = Environment.ExpandEnvironmentVariables(inputText.Trim().Trim('"'));
        if (LooksLikePath(candidateText)) return TryResolvePathCandidate(candidateText, out resolvedPath);

        return TryResolveFromRegistry(ref resolvedPath, candidateText) || 
               TryResolveFromSearchPath(candidateText, out resolvedPath);
    }

    private static bool TryResolvePathCandidate(string candidateText, out string resolvedPath)
    {
        resolvedPath = null;

        if (File.Exists(candidateText))
        {
            resolvedPath = Path.GetFullPath(candidateText);
            return true;
        }

        if (Path.HasExtension(candidateText)) return false;

        foreach (var extension in GetPathExtensions())
        {
            var candidateWithExtension = candidateText + extension;
            if (!File.Exists(candidateWithExtension)) continue;

            resolvedPath = Path.GetFullPath(candidateWithExtension);
            return true;
        }

        return false;
    }

    private static bool TryResolveFromRegistry(ref string resolvedPath, string candidateText)
    {
        var executableName = Path.HasExtension(candidateText) ? candidateText : candidateText + ".exe";
        const string appPathsKey = @"Software\Microsoft\Windows\CurrentVersion\App Paths";
        var registrationPath = $@"{appPathsKey}\{executableName}";
        RegistryKey[] registryRoots = [Registry.CurrentUser, Registry.LocalMachine];
        foreach (var registryRoot in registryRoots)
        {
            using var registration = registryRoot.OpenSubKey(registrationPath);
            var registeredPath = registration?.GetValue(null) as string;
            if (string.IsNullOrWhiteSpace(registeredPath)) continue;

            var expandedPath = Environment.ExpandEnvironmentVariables(registeredPath.Trim().Trim('"'));
            if (File.Exists(expandedPath))
            {
                resolvedPath = Path.GetFullPath(expandedPath);
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveFromSearchPath(string fileName, out string resolvedPath)
    {
        resolvedPath = null;
        var searchPath = BuildSearchPath();

        if (Path.HasExtension(fileName)) return TrySearchPath(searchPath, fileName, null, out resolvedPath);

        foreach (var extension in GetPathExtensions())
        {
            if (TrySearchPath(searchPath, fileName, extension, out resolvedPath)) return true;
        }

        return false;
    }

    private static bool TrySearchPath(string searchPath, string fileName, string extension, out string resolvedPath)
    {
        resolvedPath = null;

        var requiredLength = SearchPathW(searchPath, fileName, extension, 0, null, IntPtr.Zero);
        if (requiredLength == 0) return false;

        var buffer = new StringBuilder((int)requiredLength + 1);
        var writtenLength = SearchPathW(searchPath, fileName, extension, buffer.Capacity, buffer, IntPtr.Zero);
        if (writtenLength == 0) return false;

        resolvedPath = buffer.ToString();
        return true;
    }

    private static string BuildSearchPath()
    {
        var parts = new List<string>();

        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windowsDirectory)) parts.Add(windowsDirectory);

        var systemDirectory = Environment.SystemDirectory;
        if (!string.IsNullOrWhiteSpace(systemDirectory)) parts.Add(systemDirectory);

        var envPath = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(envPath)) parts.Add(envPath);

        return string.Join(";", parts);
    }

    private static string[] GetPathExtensions()
    {
        var rawPathExtensions = Environment.GetEnvironmentVariable("PATHEXT");
        if (string.IsNullOrWhiteSpace(rawPathExtensions)) return [".COM", ".EXE", ".BAT", ".CMD"];

        var extensions = rawPathExtensions.Split([';'], StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < extensions.Length; i++)
            extensions[i] = extensions[i].Trim();

        return extensions;
    }

    private static bool LooksLikePath(string inputText)
    {
        return Path.IsPathRooted(inputText) ||
               inputText.StartsWith(".", StringComparison.Ordinal) ||
               inputText.Contains("\\") ||
               inputText.Contains("/");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "SearchPathW", SetLastError = true)]
    private static extern uint SearchPathW(
        string lpPath,
        string lpFileName,
        string lpExtension,
        int nBufferLength,
        StringBuilder lpBuffer,
        IntPtr lpFilePart);
}
