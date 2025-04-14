using System;
using System.Collections.ObjectModel;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.ApiClients;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Livestream.Monitor.Core;

/// <summary>
/// Migrates from the old array of streamids format to the new format using <see cref="UniqueStreamKey"/> type
/// </summary>
public class ExcludeNotifyJsonConverter : JsonConverter
{
    public static bool SaveRequired;

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        var jsonObj = JArray.FromObject(value);
        jsonObj.WriteTo(writer);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null) return null;

        var exclusions = (ObservableCollection<UniqueStreamKey>)existingValue;

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonToken.EndArray:
                    return exclusions;
                case JsonToken.StartObject:
                    var excludeNotify = serializer.Deserialize<UniqueStreamKey>(reader); // could have null property values
                    exclusions.Add(excludeNotify);
                    break;
                default: // convert old array of stream ids
                    var streamId = reader.Value.ToString();
                    SaveRequired = true; // if we ran conversions then we should save the new output file
                    exclusions.Add(new UniqueStreamKey(TwitchApiClient.API_NAME, streamId));
                    break;
            }
        }

        return exclusions;
    }

    public override bool CanConvert(Type objectType)
    {
        return true;
    }
}