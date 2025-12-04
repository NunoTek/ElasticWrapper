using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Search;
using Elastic.Clients.Elasticsearch.QueryDsl;
using ElasticWrapper.ElasticSearch.Attributes;
using ElasticWrapper.ElasticSearch.Extensions;
using ElasticWrapper.ElasticSearch.Models;
using ElasticWrapper.ElasticSearch.Options;

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

    private string? GetFullNameCamelCased(string? fullName) => !string.IsNullOrEmpty(fullName) ? string.Join(".", fullName.Split(".").Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.ToCamelCase())) : null;
    private string? GetKeywordName(string? fieldName) => !string.IsNullOrEmpty(fieldName) ? $"{fieldName}.{KeywordSuffix}" : null;

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
            else if (propType.IsClass && propType != typeof(string))
            {
                AddProperties(propType, newProperty);
            }
        }
    }

    protected (BoolQuery BoolQuery, List<NestedQuery> NestedQueries) BuildModelFilters(TElasticFilters filters)
    {
        var props = filters.GetType().GetProperties()
            .Where(prop => !Attribute.IsDefined(prop, typeof(ElasticIgnoreOnBuildQueryAttribute)))
            .Where(prop => prop.GetValue(filters, null) != null);

        var mustQueries = new List<Query>();
        var shouldQueries = new List<Query>();
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

            var fullName = GetFullNameCamelCased(propMap.FullName)!;
            var fieldName = propMap.Keyword ? GetKeywordName(fullName)! : fullName;

            Query? query = null;

            if (propType == typeof(ElasticRangeFilter))
            {
                var range = propValue as ElasticRangeFilter;
                query = new NumberRangeQuery(new Field(fieldName))
                {
                    Gt = range?.Min ?? 0,
                    Lt = range?.Max
                };
            }
            else if (propType == typeof(string))
            {
                query = new QueryStringQuery
                {
                    DefaultField = fullName,
                    Query = $"*{propValue}*"
                };
            }
            else if (propType == typeof(List<string>))
            {
                var values = (propValue as List<string>)?.Select(v => FieldValue.String(v)).ToList();
                query = new TermsQuery
                {
                    Field = fieldName,
                    Terms = values != null ? new TermsQueryField(values) : null
                };
            }
            else if (propType.IsGenericType && propType.GetGenericTypeDefinition() == typeof(List<>))
            {
                var propValues = (propValue as IEnumerable<object>)?.Select(v => FieldValue.String(v?.ToString()!)).ToList();
                query = new TermsQuery
                {
                    Field = fieldName,
                    Terms = propValues != null ? new TermsQueryField(propValues) : null
                };
            }
            else
            {
                query = new TermQuery(fieldName)
                {
                    Field = fieldName,
                    Value = ConvertToFieldValue(propValue)
                };
            }

            if (query != null)
            {
                mustQueries.Add(query);

                if (!string.IsNullOrEmpty(propMap.Nested))
                {
                    var path = propMap.Nested.ToCamelCase();

                    var existingNested = nestedQueries.FirstOrDefault(x => x.Path == path);
                    if (existingNested == null)
                    {
                        nestedQueries.Add(new NestedQuery
                        {
                            Path = path,
                            Query = query,
                            InnerHits = new InnerHits { Size = _options.MaxInnerResultWindow }
                        });
                    }
                    else
                    {
                        existingNested.Query = new BoolQuery
                        {
                            Must = new List<Query> { existingNested.Query!, query }
                        };
                    }
                }
            }
        }

        var boolQuery = new BoolQuery { Must = mustQueries, Should = shouldQueries };
        return (boolQuery, nestedQueries);
    }

    private static FieldValue ConvertToFieldValue(object value)
    {
        return value switch
        {
            string s => FieldValue.String(s),
            int i => FieldValue.Long(i),
            long l => FieldValue.Long(l),
            double d => FieldValue.Double(d),
            float f => FieldValue.Double(f),
            decimal dec => FieldValue.Double((double)dec),
            bool b => FieldValue.Boolean(b),
            _ => FieldValue.String(value?.ToString() ?? string.Empty)
        };
    }

    public virtual Task<BoolQuery> GetQueryAsync(TElasticFilters filters)
    {
        var query = BuildModelFilters(filters);

        if (query.NestedQueries.Any())
        {
            var shouldQueries = query.BoolQuery.Should?.ToList() ?? new List<Query>();
            query.NestedQueries.ForEach(x => shouldQueries.Add(x));
            query.BoolQuery.Should = shouldQueries;
        }

        return Task.FromResult(query.BoolQuery);
    }

    public virtual async Task<SearchRequestDescriptor<TElasticEntity>> BuildSearchRequestAsync(
        SearchRequestDescriptor<TElasticEntity> searchRequest,
        TElasticFilters filters)
    {
        var query = await GetQueryAsync(filters);
        searchRequest.Query(query);
        return searchRequest;
    }

    public virtual Task<SearchRequestDescriptor<TElasticEntity>> ApplySortAsync(
        SearchRequestDescriptor<TElasticEntity> searchRequest,
        ElasticPaging paging)
    {
        if (string.IsNullOrEmpty(paging.SortBy))
            return Task.FromResult(searchRequest);

        var docProp = _documentProperties.FirstOrDefault(x => x.FullName.Equals(paging.SortBy, StringComparison.OrdinalIgnoreCase));
        if (docProp == null)
            return Task.FromResult(searchRequest);

        var key = GetFullNameCamelCased(docProp.FullName)!;
        if (docProp.Type == typeof(string) || docProp.Type == typeof(IEnumerable<string>))
            key += $".{KeywordSuffix}";

        if (!string.IsNullOrEmpty(docProp.Nested))
        {
            var nested = GetFullNameCamelCased(docProp.Nested)!;
            searchRequest.Sort(s => s
                .Field(new Field(key), f => f
                    .Order(paging.Descending ? SortOrder.Desc : SortOrder.Asc)
                    .Nested(n => n.Path(nested))));
            return Task.FromResult(searchRequest);
        }

        searchRequest.Sort(s => s
            .Field(new Field(key), f => f.Order(paging.Descending ? SortOrder.Desc : SortOrder.Asc)));

        return Task.FromResult(searchRequest);
    }

    public virtual async Task<SearchRequestDescriptor<TElasticEntity>> BuildCountRequestAsync(TElasticFilters filters)
    {
        var searchRequest = new SearchRequestDescriptor<TElasticEntity>();
        searchRequest.Size(0);
        searchRequest.From(0);

        await BuildSearchRequestAsync(searchRequest, filters);

        return searchRequest;
    }

    public virtual async Task<SearchRequestDescriptor<TElasticEntity>> BuildAggregateRequestAsync(TElasticFilters filters)
    {
        var searchRequest = await BuildCountRequestAsync(filters);
        ApplyAggregations(searchRequest, filters);

        return searchRequest;
    }

    protected SearchRequestDescriptor<TElasticEntity> ApplyAggregations(
        SearchRequestDescriptor<TElasticEntity> searchRequest,
        TElasticFilters filters)
    {
        var aggregatesProps = _documentProperties.Where(p => p.Aggregate).ToList();

        searchRequest.Aggregations(aggs =>
        {
            foreach (var prop in aggregatesProps)
            {
                if (prop == null)
                    continue;

                var fullName = GetFullNameCamelCased(prop.FullName)!;
                var fieldName = prop.Keyword ? GetKeywordName(fullName)! : fullName;

                if (!string.IsNullOrEmpty(prop.Nested))
                {
                    if (string.IsNullOrEmpty(prop.AggregateGroup))
                        prop.AggregateGroup = fieldName;

                    var nestFieldPath = prop.Nested.ToCamelCase();

                    aggs.Add(prop.Name, a => a
                        .Nested(n => n
                            .Path(nestFieldPath))
                        .Aggregations(na => na
                            .Add(fullName, ta => ta
                                .Terms(t => t
                                    .Field(fieldName)
                                    .Size(int.MaxValue)))));
                    continue;
                }

                var propType = Nullable.GetUnderlyingType(prop.Type) ?? prop.Type;

                if (propType == typeof(decimal) || propType == typeof(double) || propType == typeof(int))
                {
                    aggs.Add($"{prop.Name}Min", a => a.Min(m => m.Field(fieldName)));
                    aggs.Add($"{prop.Name}Max", a => a.Max(m => m.Field(fieldName)));
                    continue;
                }

                if (propType == typeof(bool))
                {
                    aggs.Add($"{prop.Name}Avg", a => a.Avg(av => av.Field(fieldName)));
                    continue;
                }

                aggs.Add(prop.Name, a => a
                    .Terms(t => t
                        .Field(fieldName)
                        .Size(int.MaxValue)));
            }
        });

        return searchRequest;
    }

    public virtual async Task<SearchRequestDescriptor<TElasticEntity>> BuildSearchRequestAsync(
        TElasticFilters filters,
        ElasticPaging paging,
        bool hasSource = true)
    {
        var searchRequest = new SearchRequestDescriptor<TElasticEntity>();

        if (filters != null)
            searchRequest = await BuildSearchRequestAsync(searchRequest, filters);

        searchRequest.Size(paging.Size);
        searchRequest.From(paging.From);

        await ApplySortAsync(searchRequest, paging);

        searchRequest.Source(hasSource);
        searchRequest.Version(true);

        return searchRequest;
    }
}