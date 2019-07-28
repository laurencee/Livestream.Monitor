using Livestream.Monitor.Core;
using NSubstitute;
using AutoFixture;

namespace Livestream.Monitor.Tests.TestFramework
{
    public class SettingsCustomization : ICustomization
    {
        public void Customize(IFixture fixture)
        {
            fixture.Register(() =>
            {
                var settingsHandler = Substitute.For<ISettingsHandler>();
                settingsHandler.Settings.Returns(fixture.Create<Settings>());
                return settingsHandler;
            });
        }
    }
}
