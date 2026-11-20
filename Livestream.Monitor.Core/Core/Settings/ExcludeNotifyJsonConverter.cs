using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.ApiClients;

namespace Livestream.Monitor.Core.Settings;

/// <summary>Reads current stream keys and the legacy array of Twitch stream IDs.</summary>
public sealed class ExcludeNotifyJsonConverter : JsonConverter<ObservableCollection<UniqueStreamKey>>
{
    public static bool LegacyExclusionsNeedSave;

    public override bool HandleNull => true;

    public override ObservableCollection<UniqueStreamKey> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var exclusions = new ObservableCollection<UniqueStreamKey>();
        if (reader.TokenType == JsonTokenType.Null) return exclusions;
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected a notification exclusion array.");

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) return exclusions;

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                exclusions.Add(JsonSerializer.Deserialize<UniqueStreamKey>(ref reader, options));
            }
            else
            {
                var id = reader.TokenType switch
                {
                    JsonTokenType.String => reader.GetString(),
                    JsonTokenType.Number => reader.GetInt64().ToString(CultureInfo.InvariantCulture),
                    _ => throw new JsonException("Invalid notification exclusion."),
                };
                LegacyExclusionsNeedSave = true;
                exclusions.Add(new UniqueStreamKey(TwitchApiClient.API_NAME, id));
            }
        }

        throw new JsonException("Incomplete notification exclusion array.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        ObservableCollection<UniqueStreamKey> value,
        JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var key in value) JsonSerializer.Serialize(writer, key, options);
        writer.WriteEndArray();
    }
}
