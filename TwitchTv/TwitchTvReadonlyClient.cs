using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TwitchTv.Dto;
using static System.String;

namespace TwitchTv
{
    public class TwitchTvReadonlyClient : ITwitchTvReadonlyClient
    {
        public async Task<UserFollows> GetUserFollows(string username)
        {
            if (IsNullOrWhiteSpace(username))
                throw new ArgumentException("Argument is null or whitespace", nameof(username));

            const int itemsPerPage = 100; // 25 is default, 100 is maximum

            HttpClient client = GetHttpClient();
            var request = $"{RequestConstants.UserFollows.Replace("{0}", username)}?limit={itemsPerPage}";
            var responseString = await client.GetStringAsync(request);
            var userFollows = JsonConvert.DeserializeObject<UserFollows>(responseString);
            // if necessary, page until we get all followed streams
            while (userFollows.Total > 0 && userFollows.Follows.Count < userFollows.Total)
            {
                var pagedRequest = $"{request}&offset={userFollows.Follows.Count}";
                responseString = await client.GetStringAsync(pagedRequest);
                var pagedFollows = JsonConvert.DeserializeObject<UserFollows>(responseString);
                userFollows.Follows.AddRange(pagedFollows.Follows);
            }
            return userFollows;
        }

        public async Task<Stream> GetStreamDetails(string streamName)
        {
            if (IsNullOrWhiteSpace(streamName))
                throw new ArgumentException("Argument is null or whitespace", nameof(streamName));

            HttpClient client = GetHttpClient();
            var request = RequestConstants.StreamDetails.Replace("{0}", streamName);
            var responseString = await client.GetStringAsync(request);
            var streamRoot = JsonConvert.DeserializeObject<StreamRoot>(responseString);

            return streamRoot.Stream;
        }

        private HttpClient GetHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(RequestConstants.AcceptHeader));

            return client;
        }
    }
}