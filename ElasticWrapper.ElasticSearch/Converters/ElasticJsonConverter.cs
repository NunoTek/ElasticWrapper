using ElasticWrapper.ElasticSearch.Extensions;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ElasticWrapper.ElasticSearch.Converters
{
    public class ElasticJsonConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.GetString();
        }

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            if (string.IsNullOrEmpty(value))
            {
                writer.WriteNullValue();
                return;
            }

            var result = value.RemoveDiacritics().ToPascalCase();
            writer.WriteStringValue(result);
        }
    }
}