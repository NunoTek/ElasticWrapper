using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ElasticWrapper.ElasticSearch.Attributes;
using ElasticWrapper.ElasticSearch.Extensions;
using ElasticWrapper.ElasticSearch.Models;
using ElasticWrapper.ElasticSearch.Options;
using Nest;

namespace ElasticWrapper.ElasticSearch.Base;

public class ElasticRequestBuilder<TElasticEntity, TElasticFilters>
    where TElasticEntity : class, new()
    where TElasticFilters : class
{

    protected const string KeywordSuffix = "keyword";
    protected const string GroupByKey = ElasticAggregateResult.GroupByKey;

    private readonly List<ElasticDocumentProperty> _documentProperties = new List<ElasticDocumentProperty>();
    protected readonly ElasticOptions _options;

    public ElasticRequestBuilder(ElasticOptions options)
    {
        _options = options;

        AddProperties(typeof(TElasticEntity));
    }

    private string? GetFullNameCamelCased(string fullName) => !string.IsNullOrEmpty(fullName) ? string.Join(".", fullName.Split(".").Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.ToCamelCase())) : null;
    private string? GetKeywordName(string fieldName) => !string.IsNullOrEmpty(fieldName) ? $"{fieldName}.{KeywordSuffix}" : null;

    private void AddProperties(Type documentType, string? currentPath = null)
    {
        foreach (var prop in documentType.GetProperties())
        {
            var newProperty = currentPath == null ? prop.Name : $"{currentPath}.{prop.Name}";

            var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

            var aggregate = Attribute.GetCustomAttribute(prop, typeof(ElasticAggregateAttribute)) as ElasticAggregateAttribute;

            _documentProperties.Add(new ElasticDocumentProperty
            {
                ClassName = documentType.Name,
                Name = prop.Name,
                FullName = newProperty,

                Prop = prop,
                Type = propType,

                Nested = currentPath,
                Aggregate = aggregate != null,
                AggregateGroup = GetFullNameCamelCased(aggregate?.GroupBy),
                AggregateOrder = GetFullNameCamelCased(aggregate?.Order)
            });


            var listTypes = new Type[] { typeof(List<>), typeof(IEnumerable<>), typeof(ICollection<>) };
            if (propType.IsGenericType && listTypes.Contains(propType.GetGenericTypeDefinition()))
            {
                AddProperties(propType.GetGenericArguments()[0], newProperty);
            }
            else if (propType.IsClass)
            {
                AddProperties(propType, newProperty);
            }
        }
    }

    protected (BoolQuery BoolQuery, List<NestedQuery> NestedQueries) BuildModelFilters(TElasticFilters filters)
    {
        var props = filters.GetType().GetProperties()
            .Where(prop => !Attribute.IsDefined(prop, typeof(ElasticIgnoreOnBuildQueryAttribute)))
            .Where(prop => prop.GetValue(filters, null) != null)
            ;

        var mustQueries = new List<QueryContainer>();
        var shouldQueries = new List<QueryContainer>();
        var nestedQueries = new List<NestedQuery>();

        foreach (var prop in props)
        {
            var propValue = prop.GetValue(filters, null);
            if (propValue == null)
                continue;

            var nested = prop.GetCustomAttributes(true).FirstOrDefault(x => x.GetType() == typeof(NestedAttribute)) as NestedAttribute;
            var propName = nested?.Name ?? prop.Name;

            var propMap = _documentProperties.FirstOrDefault(p => p.FullName == propName) ?? _documentProperties.FirstOrDefault(p => p.Name == propName);
            if (propMap == null)
                continue;


            Type propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

            var fullName = GetFullNameCamelCased(propMap.FullName); // Correct way
            //var fullName = prop.Name.ToCamelCase(); // Incorrect way
            var fieldName = propMap.Keyword ? GetKeywordName(fullName) : fullName;



            if (propType == typeof(ElasticRangeFilter))
            {
                var range = propValue as ElasticRangeFilter;

                var rangeQuery = new NumericRangeQuery()
                {
                    Field = fieldName,
                    GreaterThan = range.Min ?? 0,
                    LessThan = range.Max
                };

                mustQueries.Add(rangeQuery);
            }
            else if (propType == typeof(string))
            {
                var stringQuery = new QueryStringQuery()
                {
                    DefaultField = fullName,
                    Query = $"*{propValue.ToString()}*"
                };

                mustQueries.Add(stringQuery);
            }
            else if (propType == typeof(List<string>))
            {
                var termsQuery = new TermsQuery()
                {
                    Field = fieldName,
                    Terms = propValue as List<string>
                };

                mustQueries.Add(termsQuery);
            }
            else if (propType.IsGenericType && propType.GetGenericTypeDefinition() == typeof(List<>))
            {
                var propValues = propValue as List<object>; // TODO: convertion
                var termsQuery = new TermsQuery()
                {
                    Field = fieldName,
                    Terms = propValues
                };

                mustQueries.Add(termsQuery);
            }
            else
            {
                var termsQuery = new TermQuery()
                {
                    Field = fieldName,
                    Value = propValue
                };

                mustQueries.Add(termsQuery);
            }

            if (!string.IsNullOrEmpty(propMap.Nested))
            {
                var lastQuery = mustQueries.Last();
                var path = propMap.Nested.ToCamelCase();

                if (!nestedQueries.Any(x => x.Path == path))
                {
                    nestedQueries.Add(new NestedQuery()
                    {
                        Path = path,
                        Query = lastQuery,
                        InnerHits = new InnerHits { Size = _options.MaxInnerResultWindow }
                    });
                }
                else
                {
                    var nestedQuery = nestedQueries.First(x => x.Path == path);
                    nestedQuery.Query &= lastQuery;
                }
            }
        }

        var boolQuery = new BoolQuery() { Must = mustQueries, Should = shouldQueries };
        return (boolQuery, nestedQueries);
    }

    public virtual Task<BoolQuery> GetQueryAsync(TElasticFilters filters)
    {
        var query = BuildModelFilters(filters);

        //    var aggregatedQuery = queries.Where(x => x != null).Aggregate((current, next) => current & next);
        //    mustContainer = new QueryContainer[] { aggregatedQuery };
        //    var query = new BoolQuery { Must = mustContainer };

        if (query.NestedQueries.Any())
        {
            var shouldQueries = query.BoolQuery.Should.ToList();
            query.NestedQueries.ForEach(x => shouldQueries.Add(x));
            query.BoolQuery.Should = shouldQueries;
        }

        return Task.FromResult(query.BoolQuery);
    }

    public virtual async Task<SearchDescriptor<TElasticEntity>> BuildSearchRequestAsync(
        SearchDescriptor<TElasticEntity> searchRequest,
        TElasticFilters filters)
    {
        var query = await GetQueryAsync(filters);

        searchRequest.Query(_ => query);

        return searchRequest;
    }

    public virtual Task<SearchDescriptor<TElasticEntity>> ApplySortAsync(SearchDescriptor<TElasticEntity> searchRequest,
        ElasticPaging paging)
    {
        if (string.IsNullOrEmpty(paging.SortBy))
            return Task.FromResult(searchRequest);

        var docProp = _documentProperties.FirstOrDefault(x => x.FullName.ToLower() == paging.SortBy.ToLower());
        if (docProp == null)
            return Task.FromResult(searchRequest);

        var key = GetFullNameCamelCased(docProp.FullName);
        if (docProp.Type == typeof(string) || docProp.Type == typeof(IEnumerable<string>))
            key += $".{KeywordSuffix}";

        if (!string.IsNullOrEmpty(docProp.Nested))
        {
            var nested = GetFullNameCamelCased(docProp.Nested);
            searchRequest.Sort(
                s => s.Field(
                    f => f
                    .Field(new Field(key))
                    .Order(paging.Descending ? SortOrder.Descending : SortOrder.Ascending)
                    .Nested(n => n.Path(nested))
                ));

            return Task.FromResult(searchRequest);
        }

        searchRequest.Sort(s => s.Field(new Field(key), paging.Descending ? SortOrder.Descending : SortOrder.Ascending));

        return Task.FromResult(searchRequest);
    }

    public virtual async Task<SearchDescriptor<TElasticEntity>> BuildCountRequestAsync(
        TElasticFilters filters)
    {
        var searchRequest = new SearchDescriptor<TElasticEntity>();
        searchRequest.Size(0);
        searchRequest.From(0);

        await BuildSearchRequestAsync(searchRequest, filters);

        return searchRequest;
    }

    public virtual async Task<SearchDescriptor<TElasticEntity>> BuildAggregateRequestAsync(
        TElasticFilters filters)
    {
        var searchRequest = await BuildCountRequestAsync(filters);
        ApplyAggregations(searchRequest, filters);

        return searchRequest;
    }

    protected SearchDescriptor<TElasticEntity> ApplyAggregations(SearchDescriptor<TElasticEntity> searchRequest,
        TElasticFilters filters)
    {
        var descriptor = new AggregationContainerDescriptor<TElasticEntity>();
        var aggregatesProps = _documentProperties.Where(p => p.Aggregate);


        var query = new BoolQuery();
        var queries = BuildModelFilters(filters);
        var hasNestedQueries = queries.NestedQueries != null && queries.NestedQueries.Any();
        if (hasNestedQueries)
        {
            var aggregatedQuery = queries.NestedQueries.Select(e => e.Query).Aggregate(new QueryContainer(), (current, next) => current & next);
            query.Must = new QueryContainer[] { aggregatedQuery };
        }

        foreach (var prop in aggregatesProps)
        {
            if (prop == null)
                continue;

            var fullName = GetFullNameCamelCased(prop.FullName);
            var fieldName = prop.Keyword ? GetKeywordName(fullName) : fullName;

            if (!string.IsNullOrEmpty(prop.Nested))
            {
                if (string.IsNullOrEmpty(prop.AggregateGroup))
                    prop.AggregateGroup = fieldName;

                var nestFieldPath = prop.Nested.ToCamelCase();
                var propName = $"{prop.Nested}{prop.Name.ToPascalCase()}";

                if (hasNestedQueries)
                {
                    descriptor
                       .Nested(prop.Name, t => t.Path(nestFieldPath)
                       .Aggregations(t => t
                           .Filter($"filtered_{prop.Name}", s => s
                               .Filter(s => s.Bool(s => query))

                               .Aggregations(t => t
                                   .Terms(fullName, t => t
                                       .Field(fieldName)
                                       .Order(o => o
                                           .Ascending(prop.AggregateOrder))
                                           .Size(int.MaxValue)))
                       )));
                }
                else
                {
                    // TODO: separate Nested Parts + Reuse selectors

                    descriptor
                       .Nested(prop.Name, t => t.Path(nestFieldPath) // Incorrect way
                       //.Nested(propName, t => t.Path(nestFieldPath) // Correct way
                       .Aggregations(t => t.Terms(fullName, t => t.Field(fieldName).Order(o => o.Ascending(prop.AggregateOrder)).Size(int.MaxValue)
                       //.Aggregations(_ => _.ValueCount(GroupByKey, t => t.Field(prop.AggregateGroup))) // TODO: a activer lorsque elastic pourra faire des count distinct // TODO: tester collapse
                       //.Aggregations(_ => _.Terms(GroupByKey, t => t.Field(prop.AggregateGroup).Size(int.MaxValue)))
                       )));
                }

                continue;
            }

            if (!string.IsNullOrEmpty(prop.AggregateGroup))
            {
                descriptor.Terms(prop.Name, t => t.Field(fieldName).Order(o => o.Ascending(prop.AggregateOrder)).Size(int.MaxValue)
                        //.Aggregations(_ => _.ValueCount(GroupByKey, t => t.Field(prop.AggregateGroup))) // TODO: a activer lorsque elastic pourra faire des count distinct // TODO: tester collapse     
                        //.Aggregations(_ => _.Terms(GroupByKey, t => t.Field(prop.AggregateGroup).Size(int.MaxValue)))
                        );
                continue;
            }

            var propType = Nullable.GetUnderlyingType(prop.Type) ?? prop.Type;

            if (propType == typeof(decimal) || propType == typeof(double) || propType == typeof(int))
            {
                descriptor
                    .Min($"{prop.Name}Min", t => t.Field(fieldName))
                    .Max($"{prop.Name}Max", t => t.Field(fieldName))
                    ;
                continue;
            }

            if (propType == typeof(bool))
            {
                descriptor.Average($"{prop.Name}Avg", t => t.Field(fieldName));
                continue;
            }

            descriptor.Terms(prop.Name, t => t.Field(fieldName).Order(o => o.Ascending(prop.AggregateOrder)).Size(int.MaxValue));
        }

        searchRequest.Aggregations(_ => descriptor);

        return searchRequest;
    }

    public virtual async Task<SearchDescriptor<TElasticEntity>> BuildSearchRequestAsync(
        TElasticFilters filters,
        ElasticPaging paging,
        bool hasSource = true,
        Func<SourceFilterDescriptor<TElasticEntity>, ISourceFilter>? source = null)
    {
        var searchRequest = new SearchDescriptor<TElasticEntity>();
        if (filters != null)
            searchRequest = await BuildSearchRequestAsync(searchRequest, filters);

        searchRequest.Size(paging.Size);
        searchRequest.From(paging.From);

        await ApplySortAsync(searchRequest, paging);

        searchRequest.Source(hasSource);
        if (source != null)
            searchRequest.Source(source);

        searchRequest.Version(true);

        return searchRequest;
    }
}