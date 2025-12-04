using System;

namespace ElasticWrapper.ElasticSearch.Attributes
{
    public class ElasticAggregateAttribute : Attribute
    {
        public const string DefaultOrder = "_key";

        public string? GroupBy { get; set; }

        public string Order { get; set; }

        public ElasticAggregateAttribute()
        {
            Order = DefaultOrder;
        }

        public ElasticAggregateAttribute(string groupByField)
        {
            GroupBy = groupByField;
            Order = DefaultOrder;
        }

        public ElasticAggregateAttribute(string groupByField, string order)
        {
            GroupBy = groupByField;
            Order = order;
        }
    }
}