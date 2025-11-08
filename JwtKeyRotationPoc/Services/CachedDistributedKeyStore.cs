using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;

namespace JwtKeyRotationPoc.Services;

/// <summary>
/// Cached wrapper around distributed key store for performance
/// Provides local caching with cache invalidation support
/// </summary>
public class CachedDistributedKeyStore : IKeyStore
{
    private readonly IDistributedKeyStore _distributedStore;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<CachedDistributedKeyStore> _logger;
    private const string CacheKeyPrefix = "jwt:key:";
    private const string AllKeysCacheKey = "jwt:keys:all";
    private const string ActiveKidCacheKey = "jwt:active:kid";
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(5);

    public CachedDistributedKeyStore(
        IDistributedKeyStore distributedStore,
        IMemoryCache memoryCache,
        ILogger<CachedDistributedKeyStore> logger)
    {
        _distributedStore = distributedStore;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    public SigningKeyInfo GetActiveKey()
    {
        var activeKid = GetActiveKid();
        if (string.IsNullOrEmpty(activeKid))
            throw new InvalidOperationException("No active key found");

        var key = GetKey(activeKid);
        if (key == null)
            throw new InvalidOperationException($"Active key {activeKid} not found");

        return key; // key is guaranteed non-null here due to check above
    }

    public IEnumerable<SigningKeyInfo> GetAllKeys()
    {
        return _memoryCache.GetOrCreate(AllKeysCacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheExpiration;
            entry.SlidingExpiration = TimeSpan.FromMinutes(1);
            
            var keys = _distributedStore.GetAllKeysAsync().GetAwaiter().GetResult().ToList();
            return keys; // Explicit return to avoid null warning
        });
    }

    public SigningKeyInfo CreateAndActivateNewKey()
    {
        const string lockKey = "key-rotation";
        const int lockTimeoutSeconds = 10;

        // Acquire distributed lock to prevent concurrent key rotation
        if (!_distributedStore.TryAcquireLockAsync(lockKey, TimeSpan.FromSeconds(lockTimeoutSeconds)).GetAwaiter().GetResult())
        {
            throw new InvalidOperationException("Another instance is currently rotating keys. Please try again.");
        }

        try
        {
            // Create new key
            var newKey = CreateRsaSigningKey();

            // Get current active key and mark as inactive
            var currentActiveKid = GetActiveKid();
            if (!string.IsNullOrEmpty(currentActiveKid))
            {
                var currentKey = GetKey(currentActiveKid);
                if (currentKey != null)
                {
                    var inactiveKey = currentKey with { IsActive = false };
                    _distributedStore.SaveKeyAsync(inactiveKey).GetAwaiter().GetResult();
                    InvalidateCache(currentActiveKid);
                }
            }

            // Save new key and set as active
            _distributedStore.SaveKeyAsync(newKey).GetAwaiter().GetResult();
            _distributedStore.SetActiveKidAsync(newKey.Kid).GetAwaiter().GetResult();

            // Invalidate caches
            InvalidateAllCaches();

            _logger.LogInformation("Key rotated successfully. New active key: {Kid}", newKey.Kid);
            return newKey;
        }
        finally
        {
            _distributedStore.ReleaseLockAsync(lockKey).GetAwaiter().GetResult();
        }
    }

    public void RetireKey(string kid)
    {
        _distributedStore.DeleteKeyAsync(kid).GetAwaiter().GetResult();
        InvalidateCache(kid);
        InvalidateAllCaches();
        _logger.LogInformation("Key retired: {Kid}", kid);
    }

    private SigningKeyInfo? GetKey(string kid)
    {
        var cacheKey = CacheKeyPrefix + kid;
        return _memoryCache.GetOrCreate(cacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheExpiration;
            entry.SlidingExpiration = TimeSpan.FromMinutes(1);
            
            return _distributedStore.GetKeyAsync(kid).GetAwaiter().GetResult();
        });
    }

    private string? GetActiveKid()
    {
        return _memoryCache.GetOrCreate(ActiveKidCacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheExpiration;
            entry.SlidingExpiration = TimeSpan.FromMinutes(1);
            
            return _distributedStore.GetActiveKidAsync().GetAwaiter().GetResult();
        });
    }

    private void InvalidateCache(string kid)
    {
        _memoryCache.Remove(CacheKeyPrefix + kid);
    }

    private void InvalidateAllCaches()
    {
        _memoryCache.Remove(AllKeysCacheKey);
        _memoryCache.Remove(ActiveKidCacheKey);
    }

    private SigningKeyInfo CreateRsaSigningKey()
    {
        var rsa = RSA.Create(2048);
        var parameters = rsa.ExportParameters(true);
        rsa.Dispose();

        var newRsa = RSA.Create();
        newRsa.ImportParameters(parameters);

        var rsaKey = new RsaSecurityKey(newRsa) { KeyId = Guid.NewGuid().ToString("N") };
        return new SigningKeyInfo(rsaKey.KeyId!, rsaKey, DateTimeOffset.UtcNow, true);
    }
}

