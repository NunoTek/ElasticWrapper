using ElasticWrapper.ElasticSearch.Extensions;
using Newtonsoft.Json;
using System;
using System.Diagnostics.CodeAnalysis;

namespace ElasticWrapper.ElasticSearch.Converters
{
    public class ElasticJsonConverter : JsonConverter<string>
    {
        public override void WriteJson(JsonWriter writer, [AllowNull] string value, JsonSerializer serializer)
        {
            if (string.IsNullOrEmpty(value))
            {
                writer.WriteValue(null as string);
                return;
            }

            var result = value.RemoveDiacritics().ToPascalCase();

            writer.WriteValue(result);
        }

        public override string ReadJson(JsonReader reader, Type objectType, [AllowNull] string existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            return reader.Value.ToString();
        }
    }
}