using System;
using System.IO;
using Livestream.Monitor.Core.Utility;
using Microsoft.Win32;
using Xunit;

namespace Livestream.Monitor.Tests
{
    [Collection(nameof(WindowsCommandResolverShould))]
    public class WindowsCommandResolverShould
    {
        private static string CmdFullPath => Path.Combine(Environment.SystemDirectory, "cmd.exe");

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void Resolve_ExecutableInApplicationOrCurrentDirectory(bool inApplicationDirectory, bool includeExtension)
        {
            var commandName = "livestream-monitor-test-" + Guid.NewGuid().ToString("N");
            var originalDirectory = Environment.CurrentDirectory;
            var currentDirectory = Path.Combine(Path.GetTempPath(), commandName);
            var currentPath = Path.Combine(currentDirectory, commandName + ".exe");
            var expectedPath = inApplicationDirectory
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, commandName + ".exe")
                : currentPath;
            var input = includeExtension ? commandName + ".exe" : commandName;
            Directory.CreateDirectory(currentDirectory);

            try
            {
                Environment.CurrentDirectory = currentDirectory;
                File.Copy(CmdFullPath, currentPath);
                if (inApplicationDirectory) File.Copy(CmdFullPath, expectedPath);

                var resolved = WindowsCommandResolver.TryResolveExecutable(input, out var resolvedPath);

                Assert.True(resolved);
                Assert.Equal(expectedPath, resolvedPath, ignoreCase: true);
            }
            finally
            {
                Environment.CurrentDirectory = originalDirectory;
                if (inApplicationDirectory) File.Delete(expectedPath);
                File.Delete(currentPath);
                Directory.Delete(currentDirectory);
            }
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(false, false)]
        [InlineData(true, true)]
        [InlineData(true, false)]
        public void ResolveOnlyExistingBareAndFullyQualifiedCommands(bool fullyQualified, bool exists)
        {
            var input = exists ? "cmd" : $"livestream-monitor-missing-{Guid.NewGuid():N}";
            if (fullyQualified) input = exists ? Path.GetFullPath(CmdFullPath) : Path.Combine(Path.GetTempPath(), input + ".exe");
            if (fullyQualified && !exists) Assert.False(File.Exists(input));

            var resolved = WindowsCommandResolver.TryResolveExecutable(input, out var resolvedPath);
            Assert.Equal(exists, resolved);

            if (exists)
            {
                Assert.NotNull(resolvedPath);
                Assert.True(File.Exists(resolvedPath));
                Assert.True(PathsEqual(CmdFullPath, resolvedPath));
            }
            else
            {
                Assert.Null(resolvedPath);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Resolve_RegisteredApplication(bool includeExtension)
        {
            var commandName = $"livestream-monitor-test-{Guid.NewGuid():N}";
            var registryPath = $@"Software\Microsoft\Windows\CurrentVersion\App Paths\{commandName}.exe";
            var input = includeExtension ? commandName + ".exe" : commandName;
            var expectedPath = CmdFullPath;

            using var registration = Registry.CurrentUser.CreateSubKey(registryPath);
            try
            {
                Assert.NotNull(registration);
                registration.SetValue("", expectedPath);

                var resolved = WindowsCommandResolver.TryResolveExecutable(input, out string resolvedPath);

                Assert.True(resolved);
                Assert.Equal(expectedPath, resolvedPath, ignoreCase: true);
            }
            finally
            {
                Registry.CurrentUser.DeleteSubKey(registryPath);
            }
        }

        [Fact]
        public void NotResolve_RegisteredApplication_WithMissingExecutable()
        {
            var commandName = $"livestream-monitor-test-{Guid.NewGuid():N}";
            var registryPath = $@"Software\Microsoft\Windows\CurrentVersion\App Paths\{commandName}.exe";
            var missingPath = Path.Combine(Environment.SystemDirectory, commandName + ".exe");

            using var registration = Registry.CurrentUser.CreateSubKey(registryPath);
            try
            {
                Assert.NotNull(registration);
                registration.SetValue("", missingPath);

                var resolved = WindowsCommandResolver.TryResolveExecutable(commandName, out string resolvedPath);

                Assert.False(resolved);
                Assert.Null(resolvedPath);
            }
            finally
            {
                Registry.CurrentUser.DeleteSubKey(registryPath);
            }
        }

        private static bool PathsEqual(string expectedPath, string actualPath)
        {
            return string.Equals(
                Path.GetFullPath(expectedPath),
                Path.GetFullPath(actualPath),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    // Changing the process-wide working directory must not race other test collections.
    [CollectionDefinition(nameof(WindowsCommandResolverShould), DisableParallelization = true)]
    public class WindowsCommandResolverCollection;
}
