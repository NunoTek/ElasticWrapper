using System;
using System.Text;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using ElasticWrapper.ElasticSearch.Options;
using Microsoft.Extensions.Logging;

namespace ElasticWrapper.ElasticSearch.Base;

public class ElasticClientProvider<TElasticEntity>
    where TElasticEntity : class, new()
{
    private readonly ElasticOptions _options;
    private readonly ILogger<ElasticClientProvider<TElasticEntity>> _logger;

    private ElasticsearchClient? _elasticClient;

    public ElasticClientProvider(
        ElasticOptions options,
        ILogger<ElasticClientProvider<TElasticEntity>> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ElasticsearchClient GetClient() => Client;

    private ElasticsearchClient Client
    {
        get
        {
            if (_elasticClient is null)
            {
                _elasticClient = new ElasticsearchClient(ClientSettings);
            }

            return _elasticClient;
        }
    }

    private ElasticsearchClientSettings ClientSettings
    {
        get
        {
            ElasticsearchClientSettings settings;

            if (!string.IsNullOrEmpty(_options.CloudId))
            {
                settings = new ElasticsearchClientSettings(_options.CloudId, new BasicAuthentication(_options.UserName!, _options.Password!));
            }
            else
            {
                var uri = new Uri(_options.Uri);
                settings = new ElasticsearchClientSettings(uri);

                if (!string.IsNullOrEmpty(_options.UserName) && !string.IsNullOrEmpty(_options.Password))
                {
                    settings.Authentication(new BasicAuthentication(_options.UserName, _options.Password));
                }
            }

            settings
                .RequestTimeout(TimeSpan.FromMinutes(2))
                .DefaultIndex(_options.Index);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                settings
                    .EnableDebugMode()
                    .DisableDirectStreaming()
                    .OnRequestCompleted(call =>
                    {
                        if (call?.RequestBodyInBytes is null)
                            return;

                        if (!call.HasSuccessfulStatusCode)
                        {
                            _logger.LogError("Elastic request failed: {DebugInfo}", call.DebugInformation);
                        }

                        try
                        {
                            var raw = Encoding.UTF8.GetString(call.RequestBodyInBytes);
                            _logger.LogDebug("Elastic request: {raw}", raw);
                        }
                        catch
                        {
                        }
                    });
            }

            return settings;
        }
    }
}