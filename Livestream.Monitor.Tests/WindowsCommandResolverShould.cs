using System;
using System.IO;
using Livestream.Monitor.Core.Utility;
using Microsoft.Win32;
using Xunit;

namespace Livestream.Monitor.Tests
{
    public class WindowsCommandResolverShould
    {
        private static string CmdFullPath => Path.Combine(Environment.SystemDirectory, "cmd.exe");

        [Fact]
        public void Resolve_CommonWindowsCommand_FromSearchPath()
        {
            var resolved = WindowsCommandResolver.TryResolveExecutable("cmd", out string resolvedPath);

            Assert.True(resolved);
            Assert.NotNull(resolvedPath);
            Assert.True(File.Exists(resolvedPath));
            Assert.True(PathsEqual(CmdFullPath, resolvedPath));
        }

        [Fact]
        public void NotResolve_MissingCommand_FromSearchPath()
        {
            var missingCommand = $"livestream-monitor-missing-{Guid.NewGuid():N}";

            var resolved = WindowsCommandResolver.TryResolveExecutable(missingCommand, out string resolvedPath);

            Assert.False(resolved);
            Assert.Null(resolvedPath);
        }

        [Fact]
        public void Resolve_ExistingFullyQualifiedPath()
        {
            var expectedPath = Path.GetFullPath(CmdFullPath);

            var resolved = WindowsCommandResolver.TryResolveExecutable(expectedPath, out string resolvedPath);

            Assert.True(resolved);
            Assert.NotNull(resolvedPath);
            Assert.True(File.Exists(resolvedPath));
            Assert.True(PathsEqual(expectedPath, resolvedPath));
        }

        [Fact]
        public void NotResolve_MissingFullyQualifiedPath()
        {
            var missingPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.exe");
            Assert.False(File.Exists(missingPath));

            var resolved = WindowsCommandResolver.TryResolveExecutable(missingPath, out string resolvedPath);

            Assert.False(resolved);
            Assert.Null(resolvedPath);
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
}
