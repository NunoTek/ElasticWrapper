using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Cluster;
using Elastic.Clients.Elasticsearch.Core.Bulk;
using Elastic.Clients.Elasticsearch.IndexLifecycleManagement;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Clients.Elasticsearch.Mapping;
using ElasticWrapper.ElasticSearch.Models;
using ElasticWrapper.ElasticSearch.Options;
using Microsoft.Extensions.Logging;
using Polly;

namespace ElasticWrapper.ElasticSearch.Base;

public class ElasticBaseRepository<TElasticEntity, TElasticFilters, TKey>
    where TElasticEntity : class, new()
    where TElasticFilters : class
    where TKey : IEquatable<TKey>
{
    public int NbRetriesCall { get; set; } = 5;

    public readonly ElasticOptions _options;
    public readonly ElasticsearchClient _elasticClient;
    public readonly ElasticRequestBuilder<TElasticEntity, TElasticFilters> _requestBuilder;
    public readonly ILogger<ElasticRequestBuilder<TElasticEntity, TElasticFilters>> _logger;

    public ElasticBaseRepository(
        ElasticOptions options,
        ILoggerFactory loggerFactory,
        ElasticRequestBuilder<TElasticEntity, TElasticFilters>? builder = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _requestBuilder = builder ?? new ElasticRequestBuilder<TElasticEntity, TElasticFilters>(options);

        var logger = loggerFactory.CreateLogger<ElasticRequestBuilder<TElasticEntity, TElasticFilters>>();
        _logger = logger;

        var clientLogger = loggerFactory.CreateLogger<ElasticClientProvider<TElasticEntity>>();
        var provider = new ElasticClientProvider<TElasticEntity>(options, clientLogger);
        _elasticClient = provider.GetClient();
    }

    #region Cluster

    public virtual async Task<HealthResponse> HealthAsync(string? indexName = null, CancellationToken cancel = default)
    {
        indexName ??= _options.Index;
        var result = await _elasticClient.Cluster.HealthAsync(new HealthRequest(indexName), cancel);
        return result;
    }

    #endregion

    #region Index

    public virtual async Task<bool> IndicesExistsAsync(string? indexName = null, CancellationToken cancel = default)
    {
        indexName ??= _options.Index;
        var result = await _elasticClient.Indices.ExistsAsync(indexName, cancel);
        return result.Exists;
    }

    public virtual async Task<bool> CreateIndiceAsync(CancellationToken cancel = default)
    {
        CreateIndexResponse? result = null;

        if (_options.UseRollOverAlias)
        {
            var aliasName = _options.Index;
            var pattern = _options.Pattern ?? _options.Index;

            var aliasExists = await IndicesExistsAsync(aliasName, cancel);
            if (!aliasExists)
            {
                var scope = typeof(TElasticEntity).Name.ToLower();

                var policyResult = await _elasticClient.IndexLifecycleManagement.GetLifecycleAsync(
                    new GetLifecycleRequest(scope), cancel);

                if (!policyResult.IsValidResponse)
                {
                    var policy = new IlmPolicy
                    {
                        Phases = new Phases
                        {
                            Hot = new Phase
                            {
                                Actions = new Actions
                                {
                                    Rollover = new RolloverAction
                                    {
                                        MaxDocs = _options.MaxDocuments ?? 2147483647,
                                        MaxSize = $"{_options.MaxSizeGb}gb"
                                    }
                                }
                            }
                        }
                    };

                    await _elasticClient.IndexLifecycleManagement.PutLifecycleAsync(
                        new PutLifecycleRequest(scope) { Policy = policy }, cancel);
                }

                await _elasticClient.Indices.PutIndexTemplateAsync(pattern, d => d
                    .IndexPatterns($"{pattern}-*")
                    .Template(t => t
                        .Mappings(new TypeMapping())
                    ), cancel);
            }

            var firstIndex = $"{pattern}-000001";
            var firstIndexExists = await IndicesExistsAsync(firstIndex, cancel);
            if (!firstIndexExists)
            {
                result = await _elasticClient.Indices.CreateAsync(firstIndex, d => d
                    .Settings(s => s.MaxInnerResultWindow(_options.MaxInnerResultWindow))
                    .Aliases(a => a.Add(aliasName, al => al.IsWriteIndex(true))), cancel);
            }
        }
        else
        {
            result = await _elasticClient.Indices.CreateAsync(_options.Index, d => d
                .Settings(s => s.MaxInnerResultWindow(_options.MaxInnerResultWindow)), cancel);
        }

        if (result == null)
        {
            throw new ArgumentNullException(nameof(CreateIndiceAsync));
        }

        if (!result.IsValidResponse)
        {
            throw new Exception(result.ElasticsearchServerError?.Error?.Reason ?? result.DebugInformation);
        }

        return result.IsValidResponse;
    }

    public virtual async Task<double?> IndicesSizeAsync(CancellationToken cancel = default)
    {
        var result = await _elasticClient.Indices.StatsAsync(cancel);
        return result.All?.Primaries?.Store?.SizeInBytes;
    }

    public virtual async Task<IndexStats?> IndicesStatsAsync(CancellationToken cancel = default)
    {
        var result = await _elasticClient.Indices.StatsAsync(cancel);
        return result.All?.Primaries;
    }

    public virtual async Task DeleteIndiceAsync(CancellationToken cancel = default)
    {
        if (_options.UseRollOverAlias)
        {
            var aliasName = _options.Index;

            var aliasExists = await IndicesExistsAsync(aliasName, cancel);
            if (aliasExists)
            {
                var aliasResult = await _elasticClient.Indices.GetAliasAsync(r => r.Name(aliasName), cancel);
                var indices = aliasResult.Aliases.Keys.ToList();

                var deleteDocsTasks = new List<Task>();

                foreach (var indiceName in indices)
                {
                    deleteDocsTasks.Add(
                        _elasticClient.DeleteByQueryAsync<TElasticEntity>(indiceName.ToString(), d => d
                            .Query(q => q.QueryString(qs => qs.Query("*"))), cancel));
                }

                await Task.WhenAll(deleteDocsTasks);

                var deleteIndiceTasks = new List<Task>();

                foreach (var indiceName in indices)
                {
                    deleteIndiceTasks.Add(
                        _elasticClient.Indices.DeleteAsync(indiceName.ToString(), cancel));
                }

                await Task.WhenAll(deleteIndiceTasks);

                await _elasticClient.Indices.DeleteAliasAsync(Indices.All, aliasName, cancel);

                return;
            }
        }

        string indexName = _options.Index;

        await _elasticClient.DeleteByQueryAsync<TElasticEntity>(indexName, d => d
            .Query(q => q.QueryString(qs => qs.Query("*"))), cancel);

        await _elasticClient.Indices.DeleteAsync(indexName, cancel);
    }

    #endregion Index

    #region Query

    public virtual async Task<bool> ExistsAsync(TKey id, CancellationToken cancel = default)
    {
        var result = await RetryOnErrorAsync(async (cancel) =>
            await _elasticClient.ExistsAsync<TElasticEntity>(_options.Index, id!.ToString()!, r => r
                .Routing(id.ToString()), cancel), cancel);

        return result.Exists;
    }

    public virtual async Task<long> CountAsync(TElasticFilters filters, CancellationToken cancel = default)
    {
        var query = await _requestBuilder.GetQueryAsync(filters);

        var result = await RetryOnErrorAsync(async (ct) =>
            await _elasticClient.CountAsync<TElasticEntity>(c => c
                .Indices(_options.Index)
                .Query(query), ct), cancel);

        if (!result.IsValidResponse)
        {
            throw new Exception(result.ElasticsearchServerError?.Error?.Reason ?? result.DebugInformation);
        }

        return result.Count;
    }

    public virtual async Task<bool> AnyAsync(TElasticFilters filters, CancellationToken cancel = default)
    {
        var count = await CountAsync(filters, cancel);
        return count > 0;
    }

    public virtual async Task<SearchResponse<TElasticEntity>> SearchAsync(
        TElasticFilters filters,
        ElasticPaging paging,
        CancellationToken cancel = default)
    {
        var request = await _requestBuilder.BuildSearchRequestAsync(filters, paging);

        var result = await RetryOnErrorAsync(async (cancel) =>
            await _elasticClient.SearchAsync<TElasticEntity>(request, cancel), cancel);

        if (!result.IsValidResponse)
        {
            throw new Exception(result.ElasticsearchServerError?.Error?.Reason ?? result.DebugInformation);
        }

        return result;
    }

    public virtual async Task<SearchResponse<T>> ListElementsAsync<T>(
        TElasticFilters filters,
        CancellationToken cancel = default)
        where T : class
    {
        var requestDescriptor = await _requestBuilder.BuildSearchRequestAsync(filters, new ElasticPaging
        {
            Size = 10000
        });

        var baseResult = await RetryOnErrorAsync(async (cancel) =>
            await _elasticClient.SearchAsync<TElasticEntity>(requestDescriptor, cancel), cancel);

        if (!baseResult.IsValidResponse)
        {
            throw new Exception(baseResult.ElasticsearchServerError?.Error?.Reason ?? baseResult.DebugInformation);
        }

        var typedResult = await _elasticClient.SearchAsync<T>(s => s
            .Indices(_options.Index)
            .Size(10000), cancel);

        return typedResult;
    }

    public virtual async Task<List<ElasticAggregateResult>> GetAggregationsAsync(TElasticFilters filters, CancellationToken cancel = default)
    {
        var request = await _requestBuilder.BuildAggregateRequestAsync(filters);

        var result = await RetryOnErrorAsync(async (cancel) =>
            await _elasticClient.SearchAsync<TElasticEntity>(request, cancel), cancel);

        if (!result.IsValidResponse)
        {
            throw new Exception(result.ElasticsearchServerError?.Error?.Reason ?? result.DebugInformation);
        }

        return ElasticAggregateResult.FromAggregateDictionary(result.Aggregations!);
    }

    #endregion Query

    #region Crud

    public virtual async Task<TElasticEntity?> GetAsync(TKey id, CancellationToken cancel = default)
    {
        var result = await RetryOnErrorAsync(async (cancel) =>
            await _elasticClient.GetAsync<TElasticEntity>(_options.Index, id!.ToString()!, r => r
                .Routing(id.ToString()), cancel), cancel);

        if (!result.IsValidResponse)
        {
            throw new Exception(result.ElasticsearchServerError?.Error?.Reason ?? result.DebugInformation);
        }

        return result.Source;
    }

    public virtual async Task InsertAsync(TElasticEntity entity, CancellationToken cancel = default)
    {
        var id = ((dynamic)entity).Id?.ToString();

        var result = await RetryOnErrorAsync(async (cancel) =>
            await _elasticClient.IndexAsync(entity, r => r
                .Index(_options.Index)
                .Id(id), cancel), cancel);

        if (!result.IsValidResponse)
        {
            throw new Exception(result.ElasticsearchServerError?.Error?.Reason ?? result.DebugInformation);
        }
    }

    /// <summary>
    /// Update document
    /// </summary>
    /// <param name="id"></param>
    /// <param name="entity"></param>
    /// <param name="indexName">In case of rollover, the index is needed</param>
    /// <param name="cancel"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public virtual async Task UpdateAsync(TKey id, TElasticEntity entity, string? indexName = null, CancellationToken cancel = default)
    {
        indexName ??= _options.Index;

        var result = await RetryOnErrorAsync(async (cancel) =>
            await _elasticClient.UpdateAsync<TElasticEntity, TElasticEntity>(indexName, id!.ToString()!, r => r
                .Doc(entity)
                .Routing(id.ToString()), cancel), cancel);

        if (!result.IsValidResponse)
        {
            throw new Exception(result.ElasticsearchServerError?.Error?.Reason ?? result.DebugInformation);
        }
    }

    public virtual async Task UpdatePartialAsync<TPartial>(TKey id, TPartial entity, CancellationToken cancel = default)
        where TPartial : class
    {
        var result = await RetryOnErrorAsync(async (cancel) =>
            await _elasticClient.UpdateAsync<TElasticEntity, TPartial>(_options.Index, id!.ToString()!, r => r
                .Doc(entity)
                .Routing(id.ToString()), cancel), cancel);

        if (!result.IsValidResponse)
        {
            throw new Exception(result.ElasticsearchServerError?.Error?.Reason ?? result.DebugInformation);
        }
    }

    public virtual async Task DeleteAsync(TKey id, CancellationToken cancel = default)
    {
        var exist = await ExistsAsync(id, cancel);

        if (exist)
        {
            var result = await RetryOnErrorAsync(async (cancel) =>
                await _elasticClient.DeleteAsync(_options.Index, id!.ToString()!, cancel), cancel);

            if (!result.IsValidResponse)
            {
                throw new Exception(result.ElasticsearchServerError?.Error?.Reason ?? result.DebugInformation);
            }
        }
    }

    #endregion Crud

    #region Bulk

    public virtual async Task BulkInsertAsync(IEnumerable<TElasticEntity> entities, CancellationToken cancel = default)
    {
        int chunkSize = 50000;
        int count = entities.Count();
        var errors = new List<string>();

        await DisableRefreshIntervalAsync(_options.Index, cancel);

        for (int processed = 0; processed < count; processed += chunkSize)
        {
            var processing = entities.Skip(processed).Take(chunkSize);

            var result = await RetryOnErrorAsync(async (cancel) =>
            {
                var result = await _elasticClient.BulkAsync(b => b
                    .Index(_options.Index)
                    .IndexMany(processing), cancel);

                var acceptedErrorsStatus = new List<int> { (int)HttpStatusCode.BadRequest, (int)HttpStatusCode.RequestEntityTooLarge };
                var successStatus = new List<int> { (int)HttpStatusCode.Created, (int)HttpStatusCode.OK };

                if (result.Items.Any(x => !successStatus.Contains(x.Status)))
                {
                    var itemsOnError = result.Items.Where(x => !successStatus.Contains(x.Status));
                    var itemIdsOnError = itemsOnError.Select(x => x.Id).ToList();
                    var entitiesOnError = entities.Where(x => itemIdsOnError.Contains(((dynamic)x).Id?.ToString() ?? ""));

                    processing = entitiesOnError;

                    throw new Exception(result.ElasticsearchServerError?.Error?.Reason ?? result.DebugInformation);
                }
                else if (result.ApiCallDetails.HttpStatusCode.HasValue && acceptedErrorsStatus.Contains(result.ApiCallDetails.HttpStatusCode.Value))
                {
                    _logger.LogWarning(result.ElasticsearchServerError?.Error?.Reason ?? result.DebugInformation);
                }
                else if (result.ApiCallDetails.HttpStatusCode != (int)HttpStatusCode.OK)
                {
                    throw new Exception(result.ElasticsearchServerError?.Error?.Reason ?? result.DebugInformation);
                }

                return result;
            }, cancel);

            if (result.ApiCallDetails.HttpStatusCode != (int)HttpStatusCode.OK)
            {
                errors.Add(result.ElasticsearchServerError?.Error?.Reason ?? result.DebugInformation);
            }
        }

        await EditRefreshIntervalAsync(_options.Index, cancel: cancel);

        if (errors.Any())
        {
            var exceptions = $"\r\n{string.Join("; \r\n", errors)}";
            throw new Exception(exceptions);
        }
    }

    public virtual async Task BulkUpdateAsync(IEnumerable<TElasticEntity> entities, CancellationToken cancel = default)
    {
        int chunkSize = 50000;
        int count = entities.Count();
        var errors = new List<string>();

        await DisableRefreshIntervalAsync(_options.Index, cancel);

        for (int processed = 0; processed < count; processed += chunkSize)
        {
            var processing = entities.Skip(processed).Take(chunkSize);

            var result = await RetryOnErrorAsync(async (cancel) =>
            {
                var result = await _elasticClient.BulkAsync(b => b
                    .Index(_options.Index)
                    .UpdateMany(processing, (d, doc) => d.Doc(doc)), cancel);

                var acceptedErrorsStatus = new List<int> { (int)HttpStatusCode.BadRequest, (int)HttpStatusCode.RequestEntityTooLarge };
                var successStatus = new List<int> { (int)HttpStatusCode.Created, (int)HttpStatusCode.OK };

                if (result.Items.Any(x => !successStatus.Contains(x.Status)))
                {
                    var itemsOnError = result.Items.Where(x => !successStatus.Contains(x.Status));
                    var itemIdsOnError = itemsOnError.Select(x => x.Id).ToList();
                    var entitiesOnError = entities.Where(x => itemIdsOnError.Contains(((dynamic)x).Id?.ToString() ?? ""));

                    processing = entitiesOnError;

                    throw new Exception(result.ElasticsearchServerError?.Error?.Reason ?? result.DebugInformation);
                }
                else if (result.ApiCallDetails.HttpStatusCode.HasValue && acceptedErrorsStatus.Contains(result.ApiCallDetails.HttpStatusCode.Value))
                {
                    _logger.LogWarning(result.ElasticsearchServerError?.Error?.Reason ?? result.DebugInformation);
                }
                else if (result.ApiCallDetails.HttpStatusCode != (int)HttpStatusCode.OK)
                {
                    throw new Exception(result.ElasticsearchServerError?.Error?.Reason ?? result.DebugInformation);
                }

                return result;
            }, cancel);

            if (result.ApiCallDetails.HttpStatusCode != (int)HttpStatusCode.OK)
            {
                errors.Add(result.ElasticsearchServerError?.Error?.Reason ?? result.DebugInformation);
            }
        }

        await EditRefreshIntervalAsync(_options.Index, cancel: cancel);

        if (errors.Any())
        {
            var exceptions = $"\r\n{string.Join("; \r\n", errors)}";
            throw new Exception(exceptions);
        }
    }

    public virtual async Task BulkDeleteAsync(IEnumerable<int> ids, CancellationToken cancel = default)
    {
        if (!ids.Any())
            return;

        var result = await RetryOnErrorAsync(async (cancel) =>
            await _elasticClient.BulkAsync(b => b
                .Index(_options.Index)
                .DeleteMany(ids.Select(id => new Id(id.ToString()))), cancel), cancel);

        if (!result.IsValidResponse)
        {
            throw new Exception(result.ElasticsearchServerError?.Error?.Reason ?? result.DebugInformation);
        }
    }

    #endregion Bulk

    #region Utils 

    public virtual Task DeleteDuplicatesAsync(CancellationToken cancel = default)
    {
        List<int> duplicated = new List<int>();
        throw new NotImplementedException();
    }

    public virtual async Task EditRefreshIntervalAsync(string indexName, TimeSpan? interval = null, CancellationToken cancel = default)
    {
        interval ??= TimeSpan.FromSeconds(1);

        var result = await RetryOnErrorAsync(async (cancel) =>
            await _elasticClient.Indices.PutSettingsAsync(r => r
                .Indices(indexName)
                .Settings(s => s.RefreshInterval(interval)), cancel), cancel);

        if (!result.IsValidResponse)
        {
            throw new Exception(result.ElasticsearchServerError?.Error?.Reason ?? result.DebugInformation);
        }
    }

    public virtual Task DisableRefreshIntervalAsync(string indexName, CancellationToken cancel = default)
        => EditRefreshIntervalAsync(indexName, TimeSpan.FromMilliseconds(-1), cancel);

    #endregion Utils

    #region Polly

    protected virtual Task<TResponse> RetryOnErrorAsync<TResponse>(Func<CancellationToken, Task<TResponse>> action, CancellationToken cancel = default)
        => Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(NbRetriesCall, (i) => TimeSpan.FromSeconds(i * 1))
            .ExecuteAsync(action, cancel);

    protected virtual Task<TResponse> RetryOnErrorAsync<TResponse>(Func<Task<TResponse>> action)
        => Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(NbRetriesCall, (i) => TimeSpan.FromSeconds(i * 1))
            .ExecuteAsync(action);

    #endregion Polly
}