using System;
using Newtonsoft.Json;

namespace Livestream.Monitor.Model
{
    public class LivestreamFileData
    {
        private string streamProvider;

        [JsonRequired]
        public string LivestreamId { get; set; }

        /// <summary> The username this livestream was imported from </summary>
        public string ImportedBy { get; set; }

        /// <summary> The site which this livestream belongs to (twitch/youtube etc.) </summary>
        public string StreamProvider
        {
            get { return streamProvider; }
            set
            {
                if (!StreamProviders.IsValidProvider(value))
                {
                    throw new ArgumentException(
                        $"Service provider must be one of the known valid service provider types: {String.Join(",", StreamProviders.ValidProviders)}",
                        nameof(value));
                }

                streamProvider = value;
            }
        }
    }
}
