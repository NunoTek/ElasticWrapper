using System;

namespace ElasticWrapper.ElasticSearch.Attributes
{
    /// <summary>
    /// Indicates that a property maps to a nested field in Elasticsearch.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class NestedAttribute : Attribute
    {
        /// <summary>
        /// Gets or sets the name of the nested path in Elasticsearch.
        /// </summary>
        public string? Name { get; set; }

        public NestedAttribute()
        {
        }

        public NestedAttribute(string name)
        {
            Name = name;
        }
    }
}
