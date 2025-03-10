using System.Globalization;
using System.Text;

namespace ElasticWrapper.ElasticSearch.Extensions
{
    public static class StringExtensions
    {
        public static string ToPascalTitleCase(this string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            value = value.Replace("_", " ");
            var info = new CultureInfo("fr-FR").TextInfo;
            value = info.ToTitleCase(value);

            return value;
        }

        public static string ToPascalCase(this string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            if (!string.IsNullOrEmpty(value) && value.Length > 1)
                return char.ToUpperInvariant(value[0]) + value.Substring(1);

            return value.ToUpperInvariant();
        }

        public static string ToCamelCase(this string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            if (!string.IsNullOrEmpty(value) && value.Length > 1)
                return char.ToLowerInvariant(value[0]) + value.Substring(1);

            return value.ToLowerInvariant();
        }

        public static string RemoveDiacritics(this string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            return Encoding.UTF8.GetString(Encoding.GetEncoding("ISO-8859-8").GetBytes(value.Trim()));
        }
    }
}