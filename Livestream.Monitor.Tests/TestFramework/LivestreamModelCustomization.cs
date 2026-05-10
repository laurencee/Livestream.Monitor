using System;
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
                var identifier = fixture.Create<Guid>().ToString("N");
                string livestreamId = "Livestream " + identifier;
                string channelId = "Channel " + identifier;
                var apiClient = fixture.Create<IApiClient>();
                return new LivestreamModel(livestreamId, new ChannelIdentifier(apiClient, channelId));
            });
        }
    }
}
