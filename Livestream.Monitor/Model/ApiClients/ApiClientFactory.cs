using System;
using System.Collections.Generic;
using System.Linq;
using ExternalAPIs.Smashcast;
using ExternalAPIs.TwitchTv.Helix;
using ExternalAPIs.TwitchTv.V5;
using ExternalAPIs.Youtube;
using Livestream.Monitor.Core;

namespace Livestream.Monitor.Model.ApiClients
{
    public class ApiClientFactory : IApiClientFactory
    {
        private readonly TwitchApiClient twitchApiClient;
        private readonly YoutubeApiClient youtubeApiClient;
        private readonly SmashcastApiClient smashcastApiClient;

        private readonly List<IApiClient> apiClients = new List<IApiClient>();

        public ApiClientFactory(ISettingsHandler settingsHandler)
        {
            if (settingsHandler == null) throw new ArgumentNullException(nameof(settingsHandler));

            twitchApiClient = new TwitchApiClient(new TwitchTvV5ReadonlyClient(), new TwitchTvHelixHelixReadonlyClient(), settingsHandler);
            youtubeApiClient = new YoutubeApiClient(new YoutubeReadonlyClient());
            smashcastApiClient = new SmashcastApiClient(new SmashcastReadonlyClient());

            apiClients.Add(twitchApiClient);
            apiClients.Add(youtubeApiClient);
            apiClients.Add(smashcastApiClient);
        }

        public T Get<T>() where T : IApiClient
        {
            var apiClient = apiClients.FirstOrDefault(x => x.GetType() == typeof (T));
            if (apiClient == null)
                throw new ArgumentException($"{typeof(T).Name} is not a stream provider.");

            return (T) apiClient;
        }

        public IEnumerable<IApiClient> GetAll()
        {
            return apiClients;
        }

        public IApiClient GetByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));

            switch (name)
            {
                case YoutubeApiClient.API_NAME:
                    return youtubeApiClient;
                case TwitchApiClient.API_NAME:
                    return twitchApiClient;
                case SmashcastApiClient.API_NAME:
                    return smashcastApiClient;
                default:
                    return null;
            }
        }
    }
}