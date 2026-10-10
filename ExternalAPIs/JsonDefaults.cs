using System;
using System.Buffers;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExternalAPIs;

public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = CreateOptions(false);

    public static JsonSerializerOptions IndentedOptions { get; } = CreateOptions(true);

    private static JsonSerializerOptions CreateOptions(bool indented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = indented,
            // Saved files are edited by hand and never embedded in HTML, so keep text readable;
            // characters outside the Basic Multilingual Plane (e.g. emoji) are still escaped.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new IgnoreNullPropertiesConverterFactory());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class IgnoreNullPropertiesAttribute(params string[] propertyNames) : Attribute
{
    public IReadOnlyList<string> PropertyNames { get; } = propertyNames;
}

internal sealed class IgnoreNullPropertiesConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsDefined(typeof(IgnoreNullPropertiesAttribute), inherit: false);
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        // CanConvert selects this factory only for types carrying the attribute.
        var attribute = typeToConvert.GetCustomAttribute<IgnoreNullPropertiesAttribute>()!;
        var converterType = typeof(IgnoreNullPropertiesConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType, options, attribute.PropertyNames);
    }

    private sealed class IgnoreNullPropertiesConverter<T> : JsonConverter<T> where T : class
    {
        private readonly JsonSerializerOptions options;
        private readonly HashSet<string> propertyNames;

        public IgnoreNullPropertiesConverter(JsonSerializerOptions sourceOptions, IReadOnlyList<string> propertyNames)
        {
            options = new JsonSerializerOptions(sourceOptions);
            for (var i = options.Converters.Count - 1; i >= 0; i--)
                if (options.Converters[i] is IgnoreNullPropertiesConverterFactory)
                    options.Converters.RemoveAt(i);

            var comparer = options.PropertyNameCaseInsensitive
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

            this.propertyNames = new HashSet<string>(propertyNames, comparer);
        }

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions serializerOptions)
        {
            if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Expected a JSON object.");

            using var document = JsonDocument.ParseValue(ref reader);
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    // Newtonsoft's per-property null-ignore setting preserved initialized or previously read values.
                    if (property.Value.ValueKind == JsonValueKind.Null && propertyNames.Contains(property.Name))
                        continue;

                    property.WriteTo(writer);
                }

                writer.WriteEndObject();
            }

            return JsonSerializer.Deserialize<T>(buffer.WrittenSpan, options);
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions serializerOptions)
        {
            JsonSerializer.Serialize(writer, value, options);
        }
    }
}
