using System;
using System.Text;
using Elasticsearch.Net;
using ElasticWrapper.ElasticSearch.Options;
using Microsoft.Extensions.Logging;
using Nest;
using Nest.JsonNetSerializer;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace ElasticWrapper.ElasticSearch.Base;

public class ElasticClientProvider<TElasticEntity>
where TElasticEntity : class, new()
{
    private readonly ElasticOptions _options;
    private readonly ILogger<ElasticClientProvider<TElasticEntity>> _logger;

    private ElasticClient? _elasticClient;


    public ElasticClientProvider(
        ElasticOptions options,
        ILogger<ElasticClientProvider<TElasticEntity>> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ElasticClient GetClient() => Client;

    private ElasticClient Client
    {
        get
        {
            if (_elasticClient is null)
            {
                _elasticClient = new ElasticClient(ConnectionSettings);
            }

            return _elasticClient;
        }
    }

    private ConnectionSettings ConnectionSettings
    {
        get
        {
            var uri = new Uri(_options.Uri);
            var settings = new ConnectionSettings(uri);

            if (!string.IsNullOrEmpty(_options.CloudId))
            {
                var cloundConnectionPool = new CloudConnectionPool(_options.CloudId, new BasicAuthenticationCredentials(_options.UserName, _options.Password));
                settings = new ConnectionSettings(cloundConnectionPool,
                    //Permet de prendre en compte les propriétés nulles dans Elasticsearch
                    //Sinon, les propriétés passées à null sont conservées dans le document
                    sourceSerializer: (builtIn, serializationSettings) => new JsonNetSerializer(builtIn, serializationSettings,
                    () => new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Include,
                        StringEscapeHandling = StringEscapeHandling.EscapeNonAscii
                    },
                    resolver => resolver.NamingStrategy = new CamelCaseNamingStrategy()));
            }
            else
            {
                var singleNodeConnectionPool = new SingleNodeConnectionPool(uri);
                settings = new ConnectionSettings(singleNodeConnectionPool,
                    //Permet de prendre en compte les propriétés nulles dans Elasticsearch
                    //Sinon, les propriétés passées à null sont conservées dans le document
                    sourceSerializer: (builtIn, serializationSettings) => new JsonNetSerializer(builtIn, serializationSettings,
                    () => new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Include,
                        StringEscapeHandling = StringEscapeHandling.EscapeNonAscii
                    },
                    resolver => resolver.NamingStrategy = new CamelCaseNamingStrategy()));

                if (!string.IsNullOrEmpty(_options.UserName) && !string.IsNullOrEmpty(_options.Password))
                    settings.BasicAuthentication(_options.UserName, _options.Password);

                //if (!string.IsNullOrEmpty(_options.ClientId) && !string.IsNullOrEmpty(_options.ClientSecret))
                //    settings.ApiKeyAuthentication(_options.ClientId, _options.ClientSecret);
            }

            settings
                .EnableApiVersioningHeader()
                .RequestTimeout(TimeSpan.FromMinutes(2))
                .DefaultMappingFor<TElasticEntity>(m => m.IndexName(_options.Index).RelationName(nameof(TElasticEntity)))
                ;

            if (_logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
            {
                settings
                        // Déjà présent dans EnableDebugMode()
                        //.PrettyJson()
                        //.DisableDirectStreaming()

                        .EnableDebugMode()
                        .IncludeServerStackTraceOnError()
                        .OnRequestCompleted(call =>
                        {
                            if (call?.RequestBodyInBytes is null)
                                return;

                            if (!call.Success)
                                _logger.LogError(call.OriginalException?.Message ?? call.DebugInformation);

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

            settings.DefaultIndex(_options.Index);
            return settings;
        }
    }
}