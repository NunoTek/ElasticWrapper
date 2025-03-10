using ElasticWrapper.ElasticSearch.Base;
using ElasticWrapper.ElasticSearch.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;

namespace ElasticWrapper.ElasticSearch.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddElasticWrapper<TModel, TModelFilter, TKey>(this IServiceCollection services, Action<ElasticOptions> configure)
        where TModel : class, new()
        where TModelFilter : class
        where TKey : IEquatable<TKey>
    {
        services.AddOptions<ElasticOptions>().Configure(configure);
        services.AddSingleton(x => x.GetRequiredService<IOptions<ElasticOptions>>().Value);

        services.AddScoped<ElasticBaseRepository<TModel, TModelFilter, TKey>>();
        services.AddScoped<ElasticRequestBuilder<TModel, TModelFilter>>();

        return services;
    }
}
