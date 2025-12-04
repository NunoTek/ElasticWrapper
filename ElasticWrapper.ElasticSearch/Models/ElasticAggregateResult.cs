using Elastic.Clients.Elasticsearch.Aggregations;
using System;
using System.Collections.Generic;

namespace ElasticWrapper.ElasticSearch.Models
{
    public class ElasticAggregateResult
    {
        public const string GroupByKey = "group_by";

        public string Key { get; private set; }

        public object? Value { get; private set; }

        private readonly List<ElasticAggregateOption> _options = new List<ElasticAggregateOption>();
        public IReadOnlyList<ElasticAggregateOption> Options => _options.AsReadOnly();

        public ElasticAggregateResult(string key)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
        }

        public void AddOption(object key, long count)
        {
            if (key is null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            _options.Add(new ElasticAggregateOption(key, count));
        }

        public class ElasticAggregateOption
        {
            protected internal ElasticAggregateOption(object key, long count)
            {
                Key = key;
                Count = count;
            }
            public object Key { get; private set; }

            public long Count { get; private set; }
        }

        public static List<ElasticAggregateResult> FromAggregateDictionary(AggregateDictionary aggregations)
        {
            var result = new List<ElasticAggregateResult>();

            foreach (var agg in aggregations)
            {
                var aggregate = new ElasticAggregateResult(agg.Key);

                if (agg.Value is StringTermsAggregate stringTerms)
                {
                    foreach (var bucket in stringTerms.Buckets)
                    {
                        aggregate.AddOption(bucket.Key.Value!, bucket.DocCount);
                    }
                    result.Add(aggregate);
                }
                else if (agg.Value is LongTermsAggregate longTerms)
                {
                    foreach (var bucket in longTerms.Buckets)
                    {
                        aggregate.AddOption(bucket.Key, bucket.DocCount);
                    }
                    result.Add(aggregate);
                }
                else if (agg.Value is DoubleTermsAggregate doubleTerms)
                {
                    foreach (var bucket in doubleTerms.Buckets)
                    {
                        aggregate.AddOption(bucket.Key, bucket.DocCount);
                    }
                    result.Add(aggregate);
                }
                else if (agg.Value is NestedAggregate nested)
                {
                    var nestedResults = FromAggregateDictionary(nested.Aggregations);
                    result.AddRange(nestedResults);
                }
                else if (agg.Value is FilterAggregate filter)
                {
                    var filterResults = FromAggregateDictionary(filter.Aggregations);
                    result.AddRange(filterResults);
                }
                else if (agg.Value is MinAggregate minAgg)
                {
                    result.Add(new ElasticAggregateResult(agg.Key) { Value = minAgg.Value });
                }
                else if (agg.Value is MaxAggregate maxAgg)
                {
                    result.Add(new ElasticAggregateResult(agg.Key) { Value = maxAgg.Value });
                }
                else if (agg.Value is AverageAggregate avgAgg)
                {
                    result.Add(new ElasticAggregateResult(agg.Key) { Value = avgAgg.Value });
                }
                else if (agg.Value is ValueCountAggregate countAgg)
                {
                    result.Add(new ElasticAggregateResult(agg.Key) { Value = countAgg.Value });
                }
            }

            return result;
        }
    }
}