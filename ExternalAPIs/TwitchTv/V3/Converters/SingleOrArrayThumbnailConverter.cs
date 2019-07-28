using System;
using System.Collections.Generic;
using ExternalAPIs.TwitchTv.V3.Dto;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ExternalAPIs.TwitchTv.V3.Converters
{
    /// <summary>
    /// Special case converter since sometimes thumbnails are returned as just the string url of the thumbnail
    /// </summary>
    public class SingleOrArrayThumbnailConverter : SingleOrArrayConverter<Thumbnail>
    {
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            JToken token = JToken.Load(reader);
            if (token.Type == JTokenType.String)
            {
                return new List<Thumbnail>() { new Thumbnail() { Url = token.ToObject<string>() } };
            }
            if (token.Type == JTokenType.Array)
            {
                return token.ToObject<List<Thumbnail>>();
            }
            return new List<Thumbnail> { token.ToObject<Thumbnail>() };
        }
    }
}