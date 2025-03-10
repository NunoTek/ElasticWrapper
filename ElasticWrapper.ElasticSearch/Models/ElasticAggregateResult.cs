using Nest;
using System;
using System.Collections.Generic;
using System.Linq;

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

        public static List<ElasticAggregateResult> FromSearchResponse<T>(ISearchResponse<T> response) where T : class
        {
            var result = new List<ElasticAggregateResult>();

            foreach (var agg in response.Aggregations)
            {
                var items = new List<KeyedBucket<object>>();
                if (agg.Value is BucketAggregate bucket)
                {
                    items = bucket.Items.OfType<KeyedBucket<object>>().ToList();
                }
                else if (agg.Value is SingleBucketAggregate nested)
                {
                    var nestedBucket = nested.Values.OfType<BucketAggregate>().ToList();

                    if (!nestedBucket.Any())
                    {
                        nestedBucket = nested.Values.OfType<SingleBucketAggregate>()
                            .First()
                            .Values
                            .OfType<BucketAggregate>()
                            .ToList();
                    }

                    items = nestedBucket[0].Items.OfType<KeyedBucket<object>>().ToList();
                }
                else //if (agg.Value is ValueAggregate aggregateValue)
                {
                    result.Add(new ElasticAggregateResult(agg.Key) { Value = ((dynamic)agg.Value).Value });
                    continue;
                }

                var aggregate = new ElasticAggregateResult(agg.Key);
                items.ForEach(x =>
                {
                    int? groupCount = null;

                    if (x.Keys?.Contains(GroupByKey) == true && x.Values.Any())
                    {
                        var first = x.Values.First();
                        if (first is BucketAggregate)
                        {
                            // Terms Aggs
                            var groups = x.Values.OfType<BucketAggregate>().ToList();
                            var groupKeys = groups[0].Items.OfType<KeyedBucket<object>>().Select(x => x.Key.ToString()).ToHashSet();
                            groupCount = groupKeys.Distinct().Count(); // Count Distint pas encore gerer par Elastic
                        }

                        if (first is ValueAggregate)
                        {
                            // Count Aggs
                            var groups = x.Values.OfType<ValueAggregate>().ToList();
                            groupCount = groups[0].Value.HasValue ? int.Parse(groups[0].Value.ToString()) : 0;
                        }
                    }

                    aggregate.AddOption(x.Key, groupCount ?? x.DocCount ?? 0);
                });

                result.Add(aggregate);
            }

            return result;
        }
    }
}