using System;
using Newtonsoft.Json;

namespace ExternalAPIs.Hitbox.Converters
{
    /// <summary> Treats poorly formatted date time values from hitbox as UTC values </summary>
    public class HoribadHitboxDateTimeOffsetConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var dateString = (string)reader.Value;
            // hitbox api doesn't specify datetimes in a valid iso format but they are at least all utc values
            if (!string.IsNullOrEmpty(dateString) && !dateString.EndsWith("Z"))
                dateString += "Z";

            DateTimeOffset dateTimeOffset;
            if (DateTimeOffset.TryParse(dateString, out dateTimeOffset))
                return dateTimeOffset;

            bool isNullable = objectType.IsGenericType && objectType.GetGenericTypeDefinition() == typeof (Nullable<>);
            return isNullable ? (DateTimeOffset?)null : DateTimeOffset.MinValue;
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(DateTimeOffset);
        }
    }
}
