using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace JwtKeyRotationPoc.Services;

public static class KeyStoreExtensions
{
    /// <summary>
    /// Configure key store for single-instance deployment (development/testing)
    /// Uses in-memory storage
    /// </summary>
    public static IServiceCollection AddInMemoryKeyStore(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<IKeyStore, InMemoryKeyStore>();
        return services;
    }

    /// <summary>
    /// Configure key store for multi-instance deployment (production)
    /// Uses distributed cache (Redis, SQL Server, etc.) with local caching
    /// </summary>
    public static IServiceCollection AddDistributedKeyStore(
        this IServiceCollection services,
        Action<DistributedCacheEntryOptions>? configureCache = null)
    {
        // Add distributed cache (configure Redis, SQL Server, etc. separately)
        // For Redis: services.AddStackExchangeRedisCache(...)
        // For SQL Server: services.AddDistributedSqlServerCache(...)
        
        services.AddMemoryCache(); // For local caching layer
        services.AddSingleton<IDistributedKeyStore, DistributedKeyStore>();
        services.AddSingleton<IKeyStore, CachedDistributedKeyStore>();
        
        return services;
    }

    /// <summary>
    /// Configure Redis as distributed cache backend
    /// </summary>
    public static IServiceCollection AddRedisDistributedKeyStore(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = connectionString;
        });

        return services.AddDistributedKeyStore();
    }

    /// <summary>
    /// Configure Azure Key Vault + App Configuration for key storage
    /// Uses Azure Key Vault for secure key storage and App Configuration for metadata
    /// </summary>
    public static IServiceCollection AddAzureKeyStore(this IServiceCollection services)
    {
        services.AddMemoryCache(); // For local caching
        services.AddSingleton<IKeyStore, AzureKeyStore>();
        return services;
    }
}

