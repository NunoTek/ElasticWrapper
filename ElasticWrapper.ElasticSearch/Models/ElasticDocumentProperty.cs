using System;
using System.Collections.Generic;
using System.Reflection;

namespace ElasticWrapper.ElasticSearch.Models
{
    public class ElasticDocumentProperty
    {
        public string ClassName { get; set; }
        public string Name { get; set; }
        public string FullName { get; set; }

        public PropertyInfo Prop { get; internal set; }
        public Type Type { get; set; }

        public string Nested { get; set; }

        public bool Keyword => Type == typeof(string) || Type == typeof(List<string>);
        public bool Aggregate { get; set; }
        public string AggregateGroup { get; set; }
        public string AggregateOrder { get; set; }

    }
}