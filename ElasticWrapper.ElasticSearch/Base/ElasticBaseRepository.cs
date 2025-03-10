using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ElasticWrapper.ElasticSearch.Models;
using ElasticWrapper.ElasticSearch.Options;
using Microsoft.Extensions.Logging;
using Nest;
using Polly;

namespace ElasticWrapper.ElasticSearch.Base;

public class ElasticBaseRepository<TElasticEntity, TElasticFilters, TKey>
where TElasticEntity : class, new()
where TElasticFilters : class
where TKey : IEquatable<TKey>
{

    public int NbRetriesCall { get; set; } = 5;

    public readonly ElasticOptions _options;
    public readonly ElasticClient _elasticClient;
    public readonly ElasticRequestBuilder<TElasticEntity, TElasticFilters> _requestBuilder;
    public readonly ILogger<ElasticRequestBuilder<TElasticEntity, TElasticFilters>> _logger;

    public ElasticBaseRepository(
        ElasticOptions options,
        ILoggerFactory loggerFactory,
        ElasticRequestBuilder<TElasticEntity, TElasticFilters>? builder = null
    )
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

    public virtual async Task<ClusterHealthResponse> HealthAsync(string indexName = null, CancellationToken cancel = default)
    {
        indexName ??= _options.Index;
        //var index = await _elasticClient.Indices.GetAsync(indexName, ct: cancel);

        var result = await _elasticClient.Cluster.HealthAsync(indexName, ct: cancel);
        return result;
    }

    #endregion

    #region Index

    public virtual async Task<bool> IndicesExistsAsync(string? indexName = null, CancellationToken cancel = default)
    {
        indexName ??= _options.Index;
        var result = await _elasticClient.Indices.ExistsAsync(indexName, ct: cancel);
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
                var policyResult = await _elasticClient.IndexLifecycleManagement.ExplainLifecycleAsync(scope);
                if (policyResult.ServerError.Status == 404)
                {
                    var policy = await _elasticClient.IndexLifecycleManagement.PutLifecycleAsync(scope, l => l
                                        .Policy(po => po
                                            .Phases(ph => ph
                                                .Hot(p => p
                                                    .Actions(a => a
                                                        .Rollover(r => r
                                                            .MaximumDocuments(_options.MaxDocuments ?? 2147483647) // Max Value
                                                            .MaximumSize($"{_options.MaxSizeGb}gb")
                                                )))
                                            //.Warm(p => p
                                            //    .MinimumAge("10d")
                                            //    .Actions(a => a
                                            //        .ForceMerge(f => f.MaximumNumberOfSegments(1))
                                            //))
                                            //.Cold(p => p
                                            //    .MinimumAge("30d")
                                            //    .Actions(a => a
                                            //        .Freeze(f => f)
                                            //        .SetPriority(f => f.Priority(50))
                                            //))
                                            //.Frozen(p => p
                                            //    .MinimumAge("60d")
                                            //    .Actions(a => a)
                                            //)
                                            //.Delete(p => p
                                            //    .MinimumAge("100d")
                                            //    .Actions(a => a
                                            //        .Delete(f => f)
                                            //))
                                            )), cancel);
                }

                var aliasResult = await _elasticClient.Indices.PutTemplateV2Async(pattern, s => s
                        .IndexPatterns($"{pattern}-*")
                        .Template(t => t
                            .Mappings(m => m.AutoMap<TElasticEntity>())
                            .Settings(s => s
                                //.NumberOfShards(3)
                                //.NumberOfReplicas(1)
                                .Setting("index.lifecycle.name", scope)
                                .Setting("index.lifecycle.rollover_alias", $"{pattern}")
                        )),
                        cancel);
            }

            var firstIndex = $"{pattern}-000001";
            var firstIndexExists = await IndicesExistsAsync(firstIndex, cancel);
            if (!firstIndexExists)
            {
                var descriptor = new Func<CreateIndexDescriptor, ICreateIndexRequest>(c => c
                    .Settings(s => s.Setting(UpdatableIndexSettings.MaxInnerResultWindow, _options.MaxInnerResultWindow))
                    .Aliases(s => s.Alias(aliasName, a => a.IsWriteIndex(true))));

                result = await _elasticClient.Indices.CreateAsync(firstIndex, descriptor, cancel);
            }
        }
        else
        {
            var descriptor = new Func<CreateIndexDescriptor, ICreateIndexRequest>(c => c
                .Index<TElasticEntity>()
                .Settings(s => s.Setting(UpdatableIndexSettings.MaxInnerResultWindow, _options.MaxInnerResultWindow))
                .Map<TElasticEntity>(m => m
                    .AutoMap<TElasticEntity>()
                ));

            result = await _elasticClient.Indices.CreateAsync(_options.Index, descriptor, cancel);
        }

        if (result == null)
        {
            throw new ArgumentNullException(nameof(CreateIndiceAsync));
        }

        if (!result.IsValid)
        {
            throw new Exception(result.OriginalException?.Message ?? result.DebugInformation);
        }

        return result.IsValid;
    }

    public virtual async Task<double?> IndicesSizeAsync(CancellationToken cancel = default)
    {
        var result = await _elasticClient.Indices.StatsAsync(_options.Index, ct: cancel);
        return result.Stats?.Primaries?.Store?.SizeInBytes;
    }

    public virtual async Task<IndexStats?> IndicesStatsAsync(CancellationToken cancel = default)
    {
        var result = await _elasticClient.Indices.StatsAsync(_options.Index, ct: cancel);
        return result.Stats?.Primaries;
    }

    public virtual async Task DeleteIndiceAsync(CancellationToken cancel = default)
    {
        if (_options.UseRollOverAlias)
        {
            var aliasName = _options.Index;

            var aliasExists = await IndicesExistsAsync(aliasName, cancel);
            if (aliasExists)
            {
                var aliasResult = await _elasticClient.Indices.GetAliasAsync(aliasName, ct: cancel);
                var indices = aliasResult.Indices.Select(x => x.Key.Name);


                var deleteDocsTasks = new List<Task>();

                foreach (var indiceName in indices)
                    deleteDocsTasks.Add(
                    _elasticClient.DeleteByQueryAsync<TElasticEntity>(del => del
                        .Index(indiceName)
                        .Query(q => q.QueryString(qs => qs.Query("*")))
                        , cancel)
                    );

                await Task.WhenAll(deleteDocsTasks);


                var deleteIndiceTasks = new List<Task>();

                foreach (var indiceName in indices)
                    deleteIndiceTasks.Add(
                        _elasticClient.Indices.DeleteAsync(indiceName, ct: cancel)
                    );

                await Task.WhenAll(deleteIndiceTasks);


                await _elasticClient.Indices.DeleteAliasAsync("*", aliasName, ct: cancel);

                return;
            }
        }

        string indexName = _options.Index;

        await _elasticClient.DeleteByQueryAsync<TElasticEntity>(del => del
            .Index(indexName)
            .Query(q => q.QueryString(qs => qs.Query("*"))
        ), cancel);

        await _elasticClient.Indices.DeleteAsync(indexName, ct: cancel);
    }

    #endregion Index

    #region Query

    public virtual async Task<bool> ExistsAsync(TKey id, CancellationToken cancel = default)
    {
        var result = await RetryOnErrorAsync(async (cancel) =>
            await _elasticClient.DocumentExistsAsync<TElasticEntity>(id.ToString(),
                i => i.Index(_options.Index).Routing(id.ToString()),
                cancel)
        , cancel);

        return result.Exists;
    }

    public virtual async Task<long> CountAsync(TElasticFilters filters, CancellationToken cancel = default)
    {
        var query = await _requestBuilder.GetQueryAsync(filters);

        var result = await RetryOnErrorAsync(async (cancel) =>
            await _elasticClient.CountAsync<TElasticEntity>(c => c
                .Index(_options.Index)
                .Query(q => query),
                cancel)
            , cancel);

        if (!result.IsValid)
        {
            throw new Exception(result.OriginalException?.Message ?? result.DebugInformation);
        }

        return result.Count;
    }

    public virtual async Task<bool> AnyAsync(TElasticFilters filters, CancellationToken cancel = default)
    {
        var query = await _requestBuilder.GetQueryAsync(filters);

        var result = await RetryOnErrorAsync(async (cancel) =>
            await _elasticClient.CountAsync<TElasticEntity>(c => c
                .Index(_options.Index)
                .Query(q => query),
                cancel)
        , cancel);

        if (!result.IsValid)
        {
            throw new Exception(result.OriginalException?.Message ?? result.DebugInformation);
        }

        return result.Count > 0;
    }

    public virtual async Task<ISearchResponse<TElasticEntity>> SearchAsync(
        TElasticFilters filters,
        ElasticPaging paging,
        CancellationToken cancel = default)
    {
        var request = await _requestBuilder.BuildSearchRequestAsync(filters, paging);

        var result = await RetryOnErrorAsync(async (cancel) =>
            await _elasticClient.SearchAsync<TElasticEntity>(request, cancel)
        , cancel);

        if (!result.IsValid)
        {
            throw new Exception(result.OriginalException?.Message ?? result.DebugInformation);
        }

        return result;
    }

    public virtual async Task<ISearchResponse<T>> ListElementsAsync<T>(
        TElasticFilters filters,
        CancellationToken cancel = default)
        where T : class
    {
        var request = await _requestBuilder.BuildSearchRequestAsync(filters, new ElasticPaging()
        {
            Size = 10000 // Max Value
        });

        var result = await RetryOnErrorAsync(async (cancel) =>
            await _elasticClient.SearchAsync<T>(request, cancel)
        , cancel);

        if (!result.IsValid)
        {
            throw new Exception(result.OriginalException?.Message ?? result.DebugInformation);
        }

        return result;
    }

    public virtual async Task<List<ElasticAggregateResult>> GetAggregationsAsync(TElasticFilters filters, CancellationToken cancel = default)
    {
        var request = await _requestBuilder.BuildAggregateRequestAsync(filters);

        var result = await RetryOnErrorAsync(async (cancel) =>
            await _elasticClient.SearchAsync<TElasticEntity>(request)
        , cancel);

        if (!result.IsValid)
        {
            throw new Exception(result.OriginalException?.Message ?? result.DebugInformation);
        }

        return ElasticAggregateResult.FromSearchResponse(result);
    }

    #endregion Query

    #region Crud

    public virtual async Task<TElasticEntity> GetAsync(TKey id, CancellationToken cancel = default)
    {
        var result = await RetryOnErrorAsync(async (cancel) =>
            await _elasticClient.GetAsync<TElasticEntity>(id.ToString(),
                descriptor => descriptor.Index(_options.Index).Routing(id.ToString()),
                cancel)
            , cancel);

        if (!result.IsValid)
        {
            throw new Exception(result.OriginalException?.Message ?? result.DebugInformation);
        }

        return result.Source;
    }

    public virtual async Task InsertAsync(TElasticEntity entity, CancellationToken cancel = default)
    {
        var result = await RetryOnErrorAsync(async (cancel) =>
            await _elasticClient.IndexAsync(entity,
                i => i.Index(_options.Index).Id(((dynamic)entity).Id),
                cancel)
            , cancel);

        if (!result.IsValid)
        {
            throw new Exception(result.OriginalException?.Message ?? result.DebugInformation);
        }
    }

    /// <summary>
    /// Update document
    /// </summary>
    /// <param name="id"></param>
    /// <param name="entity"></param>
    /// <param name="indexName">In case of rollover, the index is needed</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public virtual async Task UpdateAsync(TKey id, TElasticEntity entity, string? indexName = null, CancellationToken cancel = default)
    {
        indexName ??= _options.Index;

        var result = await RetryOnErrorAsync(async (cancel) =>
            await _elasticClient.UpdateAsync<TElasticEntity>(
                entity,
                descriptor => descriptor
                    .Index(indexName)
                    .Doc(entity)
                    .Routing(id.ToString()),
                cancel)
            , cancel);

        if (!result.IsValid)
        {
            throw new Exception(result.OriginalException?.Message ?? result.DebugInformation);
        }
    }

    public virtual async Task UpdatePartialAsync<TPartial>(TKey id, TPartial entity, CancellationToken cancel = default)
        where TPartial : class
    {
        var result = await RetryOnErrorAsync(async (cancel) =>
            await _elasticClient.UpdateAsync<TPartial>(
                entity,
                descriptor => descriptor
                    .Index(_options.Index)
                    .Doc(entity)
                    .Routing(id.ToString()),
                cancel)
            , cancel);

        if (!result.IsValid)
        {
            throw new Exception(result.OriginalException?.Message ?? result.DebugInformation);
        }
    }

    public virtual async Task DeleteAsync(TKey id, CancellationToken cancel = default)
    {
        var exist = await ExistsAsync(id);

        if (exist)
        {
            var result = await RetryOnErrorAsync(async (cancel) =>
                            await _elasticClient.DeleteAsync<TElasticEntity>(
                                id.ToString(),
                                descriptor => descriptor.Index(_options.Index),
                                cancel)
                            , cancel);

            if (!result.IsValid)
            {
                throw new Exception(result.OriginalException?.Message ?? result.DebugInformation);
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
                var result = await _elasticClient.BulkAsync(b => b.Index(_options.Index).IndexMany(processing), cancel);

                var acceptedErrorsStatus = new List<int>() { (int)HttpStatusCode.BadRequest, (int)HttpStatusCode.RequestEntityTooLarge };
                var successStatus = new List<int>() { (int)HttpStatusCode.Created, (int)HttpStatusCode.OK };

                if (result.Items.Any(x => !successStatus.Contains(x.Status)))
                {
                    // For debugging
                    var itemsOnError = result.Items.Where(x => !successStatus.Contains(x.Status));

                    var itemIdsOnError = itemsOnError.Select(x => x.Id).ToList();
                    var entitiesOnError = entities.Where(x => itemIdsOnError.Contains(((dynamic)x).Id));

                    processing = entitiesOnError;

                    throw new Exception(result.OriginalException?.Message ?? result.DebugInformation);
                }
                else if (result.ApiCall.HttpStatusCode.HasValue && acceptedErrorsStatus.Contains(result.ApiCall.HttpStatusCode.Value))
                {
                    _logger.LogWarning(result.OriginalException?.Message ?? result.DebugInformation);
                }
                else if (result.ApiCall.HttpStatusCode != (int)HttpStatusCode.OK)
                {
                    // client error
                    throw new Exception(result.OriginalException?.Message ?? result.DebugInformation);
                }

                return result;
            }, cancel);

            if (result.ApiCall.HttpStatusCode != (int)HttpStatusCode.OK)
            {
                errors.Add(result.OriginalException?.Message ?? result.DebugInformation);
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
                var result = await _elasticClient.BulkAsync(new BulkRequest
                {
                    Operations = processing
                        .Select(x => new BulkUpdateOperation<TElasticEntity, TElasticEntity>(x, x))
                        .Cast<IBulkOperation>()
                        .ToList()
                }
                , cancel);

                var acceptedErrorsStatus = new List<int>() { (int)HttpStatusCode.BadRequest, (int)HttpStatusCode.RequestEntityTooLarge };
                var successStatus = new List<int>() { (int)HttpStatusCode.Created, (int)HttpStatusCode.OK };

                if (result.Items.Any(x => !successStatus.Contains(x.Status)))
                {
                    // For debugging
                    var itemsOnError = result.Items.Where(x => !successStatus.Contains(x.Status));

                    var itemIdsOnError = itemsOnError.Select(x => x.Id).ToList();
                    var entitiesOnError = entities.Where(x => itemIdsOnError.Contains(((dynamic)x).Id));

                    processing = entitiesOnError;

                    throw new Exception(result.OriginalException?.Message ?? result.DebugInformation);
                }
                else if (result.ApiCall.HttpStatusCode.HasValue && acceptedErrorsStatus.Contains(result.ApiCall.HttpStatusCode.Value))
                {
                    _logger.LogWarning(result.OriginalException?.Message ?? result.DebugInformation);
                }
                else if (result.ApiCall.HttpStatusCode != (int)HttpStatusCode.OK)
                {
                    // client error
                    throw new Exception(result.OriginalException?.Message ?? result.DebugInformation);
                }

                return result;
            }
            , cancel);

            if (result.ApiCall.HttpStatusCode != (int)HttpStatusCode.OK)
            {
                errors.Add(result.OriginalException?.Message ?? result.DebugInformation);
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
            await _elasticClient.BulkAsync(new BulkRequest
            {
                Operations = ids.Select(x => new BulkDeleteOperation<TElasticEntity>(x))
                    .Cast<IBulkOperation>().ToList()
            }, cancel)
        , cancel);

        if (!result.IsValid)
        {
            throw new Exception(result.OriginalException?.Message ?? result.DebugInformation);
        }
    }

    #endregion Bulk

    #region Utils 

    public virtual Task DeleteDuplicatesAsync(CancellationToken cancel = default)
    {
        List<int> duplicated = new List<int>();

        // TODO faire aggregas sur Ref

        throw new NotImplementedException();

        //return BulkDeleteAsync(duplicated, cancel);
    }

    public virtual async Task EditRefreshIntervalAsync(string indexName, Time? interval = null, CancellationToken cancel = default)
    {
        if (interval == null)
            interval = new Time(1, TimeUnit.Second);

        var result = await RetryOnErrorAsync(async (cancel) =>
            await _elasticClient.Indices.UpdateSettingsAsync(indexName, settings =>
                settings.IndexSettings(x => x.RefreshInterval(interval))
            , cancel)
        , cancel);

        if (!result.IsValid)
        {
            throw new Exception(result.OriginalException?.Message ?? result.DebugInformation);
        }
    }

    public virtual Task DisableRefreshIntervalAsync(string indexName, CancellationToken cancel = default)
        => EditRefreshIntervalAsync(indexName, Time.MinusOne, cancel);

    #endregion Utils

    #region Polly

    protected virtual Task<TResponse> RetryOnErrorAsync<TResponse>(Func<CancellationToken, Task<TResponse>> action, CancellationToken cancel = default)
        => Polly.Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(NbRetriesCall, (i) => TimeSpan.FromSeconds(i * 1))
                .ExecuteAsync(action, cancel);

    protected virtual Task<TResponse> RetryOnErrorAsync<TResponse>(Func<Task<TResponse>> action)
        => Polly.Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(NbRetriesCall, (i) => TimeSpan.FromSeconds(i * 1))
                .ExecuteAsync(action);

    #endregion Polly

}