using System.Linq;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.Model.Monitoring;
using AutoFixture;

namespace Livestream.Monitor.Tests.TestFramework
{
    public class LivestreamModelCustomization : ICustomization
    {
        public void Customize(IFixture fixture)
        {
            fixture.Register(() =>
            {
                var generator = fixture.Create<Generator<int>>();
                var generatorValue = generator.Take(1);
                string livestreamId = "Livestream " + generatorValue;
                string channelId = "Channel " + generatorValue;
                var apiClient = fixture.Create<IApiClient>();
                return new LivestreamModel(livestreamId, new ChannelIdentifier(apiClient, channelId));
            });
        }
    }
}
