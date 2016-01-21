using System;
using System.Collections.Generic;
using System.Linq;
using Google.API;
using TwitchTv;

namespace Livestream.Monitor.Model.StreamProviders
{
    public class StreamProviderFactory : IStreamProviderFactory
    {
        private static readonly TwitchStreamProvider TwitchStreamProvider = new TwitchStreamProvider(new TwitchTvReadonlyClient());
        private static readonly YoutubeStreamProvider YoutubeStreamProvider = new YoutubeStreamProvider(new YoutubeReadonlyClient());

        private readonly List<IStreamProvider> streamProviders = new List<IStreamProvider>()
        {
            TwitchStreamProvider,
            YoutubeStreamProvider
        }; 

        public T Get<T>() where T : IStreamProvider
        {
            var streamProvider = streamProviders.FirstOrDefault(x => x.GetType() == typeof (T));
            if (streamProvider == null)
                throw new ArgumentException($"{typeof(T).Name} is not a stream provider.");

            return (T) streamProvider;
        }

        public IEnumerable<IStreamProvider> GetAll()
        {
            return streamProviders;
        }

        public IStreamProvider GetStreamProviderByName(string streamProviderName)
        {
            if (string.IsNullOrWhiteSpace(streamProviderName)) throw new ArgumentNullException(nameof(streamProviderName));

            switch (streamProviderName)
            {
                case YoutubeStreamProvider.PROVIDER_NAME:
                    return YoutubeStreamProvider;
                case TwitchStreamProvider.PROVIDER_NAME:
                    return TwitchStreamProvider;
                default:
                    return null;
            }
        }
    }
}