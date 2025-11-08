using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.IdentityModel.Tokens;

namespace JwtKeyRotationPoc.Services;

/// <summary>
/// Distributed key store implementation using Redis (or any distributed cache)
/// For production, use Redis, Azure Cache, or similar distributed cache
/// This is a simplified version that demonstrates the pattern
/// </summary>
public class DistributedKeyStore : IDistributedKeyStore
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<DistributedKeyStore> _logger;
    private const string ActiveKidKey = "jwt:active:kid";
    private const string KeyPrefix = "jwt:key:";
    private const string LockPrefix = "jwt:lock:";
    private const string AllKeysSetKey = "jwt:keys:set";

    public DistributedKeyStore(IDistributedCache cache, ILogger<DistributedKeyStore> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<SigningKeyInfo?> GetKeyAsync(string kid)
    {
        var key = KeyPrefix + kid;
        var data = await _cache.GetStringAsync(key);
        if (string.IsNullOrEmpty(data))
            return null;

        return DeserializeKey(data);
    }

    public async Task<IEnumerable<SigningKeyInfo>> GetAllKeysAsync()
    {
        var allKidData = await _cache.GetStringAsync(AllKeysSetKey);
        if (string.IsNullOrEmpty(allKidData))
            return Enumerable.Empty<SigningKeyInfo>();

        var kids = JsonSerializer.Deserialize<List<string>>(allKidData) ?? new List<string>();
        var keys = new List<SigningKeyInfo>();

        foreach (var kid in kids)
        {
            var key = await GetKeyAsync(kid);
            if (key != null)
                keys.Add(key);
        }

        return keys;
    }

    public async Task<string?> GetActiveKidAsync()
    {
        return await _cache.GetStringAsync(ActiveKidKey);
    }

    public async Task SaveKeyAsync(SigningKeyInfo keyInfo)
    {
        var key = KeyPrefix + keyInfo.Kid;
        var serialized = SerializeKey(keyInfo);
        await _cache.SetStringAsync(key, serialized, new DistributedCacheEntryOptions
        {
            // Keys should persist until explicitly deleted
            AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(365)
        });

        // Update the set of all keys
        await UpdateKeysSetAsync(keyInfo.Kid, add: true);
    }

    public async Task SetActiveKidAsync(string kid)
    {
        await _cache.SetStringAsync(ActiveKidKey, kid, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(365)
        });
    }

    public async Task DeleteKeyAsync(string kid)
    {
        var key = KeyPrefix + kid;
        await _cache.RemoveAsync(key);
        await UpdateKeysSetAsync(kid, add: false);
    }

    public async Task<bool> TryAcquireLockAsync(string lockKey, TimeSpan timeout)
    {
        var fullLockKey = LockPrefix + lockKey;
        var lockValue = Guid.NewGuid().ToString();
        var expiresAt = DateTimeOffset.UtcNow.Add(timeout);

        try
        {
            // Try to set the lock (distributed cache should support atomic operations)
            // This is a simplified version - production should use Redis SETNX or similar
            var existing = await _cache.GetStringAsync(fullLockKey);
            if (!string.IsNullOrEmpty(existing))
                return false;

            await _cache.SetStringAsync(fullLockKey, lockValue, new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = expiresAt
            });

            // Verify we got the lock
            var verify = await _cache.GetStringAsync(fullLockKey);
            return verify == lockValue;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to acquire lock {LockKey}", lockKey);
            return false;
        }
    }

    public async Task ReleaseLockAsync(string lockKey)
    {
        var fullLockKey = LockPrefix + lockKey;
        await _cache.RemoveAsync(fullLockKey);
    }

    private async Task UpdateKeysSetAsync(string kid, bool add)
    {
        var allKidData = await _cache.GetStringAsync(AllKeysSetKey);
        var kids = string.IsNullOrEmpty(allKidData)
            ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(allKidData) ?? new List<string>();

        if (add && !kids.Contains(kid))
        {
            kids.Add(kid);
        }
        else if (!add)
        {
            kids.Remove(kid);
        }

        await _cache.SetStringAsync(AllKeysSetKey, JsonSerializer.Serialize(kids), new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(365)
        });
    }

    private string SerializeKey(SigningKeyInfo keyInfo)
    {
        // Serialize RSA key parameters
        if (keyInfo.Key is RsaSecurityKey rsaKey)
        {
            var rsa = rsaKey.Rsa ?? throw new InvalidOperationException("RSA key is null");
            var parameters = rsa.ExportParameters(true);
            
            var keyData = new
            {
                Kid = keyInfo.Kid,
                CreatedOn = keyInfo.CreatedOn,
                IsActive = keyInfo.IsActive,
                RSAParameters = new
                {
                    D = Convert.ToBase64String(parameters.D ?? Array.Empty<byte>()),
                    DP = Convert.ToBase64String(parameters.DP ?? Array.Empty<byte>()),
                    DQ = Convert.ToBase64String(parameters.DQ ?? Array.Empty<byte>()),
                    Exponent = Convert.ToBase64String(parameters.Exponent ?? Array.Empty<byte>()),
                    InverseQ = Convert.ToBase64String(parameters.InverseQ ?? Array.Empty<byte>()),
                    Modulus = Convert.ToBase64String(parameters.Modulus ?? Array.Empty<byte>()),
                    P = Convert.ToBase64String(parameters.P ?? Array.Empty<byte>()),
                    Q = Convert.ToBase64String(parameters.Q ?? Array.Empty<byte>())
                }
            };

            return JsonSerializer.Serialize(keyData);
        }

        throw new NotSupportedException("Only RSA keys are supported");
    }

    private SigningKeyInfo DeserializeKey(string data)
    {
        var keyData = JsonSerializer.Deserialize<JsonElement>(data);
        var kid = keyData.GetProperty("Kid").GetString()!;
        var createdOn = keyData.GetProperty("CreatedOn").GetDateTimeOffset();
        var isActive = keyData.GetProperty("IsActive").GetBoolean();
        var rsaParams = keyData.GetProperty("RSAParameters");

        var parameters = new RSAParameters
        {
            D = Convert.FromBase64String(rsaParams.GetProperty("D").GetString()!),
            DP = Convert.FromBase64String(rsaParams.GetProperty("DP").GetString()!),
            DQ = Convert.FromBase64String(rsaParams.GetProperty("DQ").GetString()!),
            Exponent = Convert.FromBase64String(rsaParams.GetProperty("Exponent").GetString()!),
            InverseQ = Convert.FromBase64String(rsaParams.GetProperty("InverseQ").GetString()!),
            Modulus = Convert.FromBase64String(rsaParams.GetProperty("Modulus").GetString()!),
            P = Convert.FromBase64String(rsaParams.GetProperty("P").GetString()!),
            Q = Convert.FromBase64String(rsaParams.GetProperty("Q").GetString()!)
        };

        var rsa = RSA.Create();
        rsa.ImportParameters(parameters);
        var rsaKey = new RsaSecurityKey(rsa) { KeyId = kid };

        return new SigningKeyInfo(kid, rsaKey, createdOn, isActive);
    }
}

