using System;
using System.Collections.Generic;
using System.Linq;
using ExternalAPIs.Hitbox;
using ExternalAPIs.TwitchTv;
using ExternalAPIs.Youtube;

namespace Livestream.Monitor.Model.ApiClients
{
    public class ApiClientFactory : IApiClientFactory
    {
        private static readonly TwitchApiClient TwitchApiClient = new TwitchApiClient(new TwitchTvReadonlyClient());
        private static readonly YoutubeApiClient YoutubeApiClient = new YoutubeApiClient(new YoutubeReadonlyClient());
        private static readonly HitboxApiClient HitboxApiClient = new HitboxApiClient(new HitboxReadonlyClient());

        private readonly List<IApiClient> apiClients = new List<IApiClient>()
        {
            TwitchApiClient,
            YoutubeApiClient,
            HitboxApiClient
        }; 

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
                    return YoutubeApiClient;
                case TwitchApiClient.API_NAME:
                    return TwitchApiClient;
                case HitboxApiClient.API_NAME:
                    return HitboxApiClient;
                default:
                    return null;
            }
        }
    }
}