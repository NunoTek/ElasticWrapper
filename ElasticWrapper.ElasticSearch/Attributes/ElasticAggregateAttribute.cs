using Nest;
using System;

namespace ElasticWrapper.ElasticSearch.Attributes
{
    public class ElasticAggregateAttribute : Attribute
    {
        public string GroupBy { get; set; }

        public string Order { get; set; }

        public ElasticAggregateAttribute()
        {
            Order = TermsOrder.KeyAscending.Key;
        }

        public ElasticAggregateAttribute(string groupByField)
        {
            GroupBy = groupByField;
            Order = TermsOrder.KeyAscending.Key;
        }

        public ElasticAggregateAttribute(string groupByField, string order)
        {
            GroupBy = groupByField;
            Order = order;
        }
    }
}