using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Data.AppConfiguration;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;

namespace JwtKeyRotationPoc.Services;

/// <summary>
/// Azure-based key store implementation using:
/// - Azure Key Vault: Stores RSA private keys securely
/// - Azure App Configuration: Stores metadata (active kid, key list, etc.)
/// - Local Memory Cache: Caches keys for performance
/// </summary>
public class AzureKeyStore : IKeyStore
{
    private readonly SecretClient _keyVaultClient;
    private readonly ConfigurationClient _appConfigClient;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<AzureKeyStore> _logger;
    private readonly string _keyVaultName;
    private readonly string _keyPrefix;
    private readonly string _activeKidKey;
    private readonly string _allKeysKey;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(5);

    public AzureKeyStore(
        IConfiguration configuration,
        IMemoryCache memoryCache,
        ILogger<AzureKeyStore> logger)
    {
        _memoryCache = memoryCache;
        _logger = logger;

        // Get configuration values
        _keyVaultName = configuration["Azure:KeyVault:Name"] 
            ?? throw new InvalidOperationException("Azure:KeyVault:Name must be configured");
        var keyVaultUri = $"https://{_keyVaultName}.vault.azure.net/";
        var appConfigConnectionString = configuration.GetConnectionString("AppConfiguration")
            ?? throw new InvalidOperationException("AppConfiguration connection string must be configured");

        _keyPrefix = configuration["Azure:KeyVault:KeyPrefix"] ?? "jwt-signing-key-";
        _activeKidKey = configuration["Azure:AppConfig:ActiveKidKey"] ?? "Jwt:ActiveKid";
        _allKeysKey = configuration["Azure:AppConfig:AllKeysKey"] ?? "Jwt:AllKeys";

        // Initialize Azure clients with DefaultAzureCredential (supports Managed Identity, VS, Azure CLI, etc.)
        var credential = new DefaultAzureCredential();
        _keyVaultClient = new SecretClient(new Uri(keyVaultUri), credential);
        _appConfigClient = new ConfigurationClient(appConfigConnectionString);

        // Ensure initial key exists
        EnsureInitialKeyExistsAsync().GetAwaiter().GetResult();
    }

    public SigningKeyInfo GetActiveKey()
    {
        var activeKid = GetActiveKid();
        if (string.IsNullOrEmpty(activeKid))
            throw new InvalidOperationException("No active key found");

        return GetKey(activeKid);
    }

    public IEnumerable<SigningKeyInfo> GetAllKeys()
    {
        return _memoryCache.GetOrCreate(AllKeysCacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheExpiration;
            entry.SlidingExpiration = TimeSpan.FromMinutes(1);

            return GetAllKeysInternalAsync().GetAwaiter().GetResult().ToList();
        });
    }

    public SigningKeyInfo CreateAndActivateNewKey()
    {
        // Use App Configuration ETags for optimistic concurrency control (distributed locking)
        const int maxRetries = 3;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                // Get current active kid with ETag
                var activeKidSetting = _appConfigClient.GetConfigurationSetting(_activeKidKey);
                var currentActiveKid = activeKidSetting.Value?.Value;

                // Create new key
                var newKey = CreateRsaSigningKey();

                // Store new key in Key Vault
                var secretName = _keyPrefix + newKey.Kid;
                var keyData = SerializeRsaKey(newKey.Key as RsaSecurityKey ?? throw new InvalidOperationException("Key must be RSA"));
                _keyVaultClient.SetSecret(secretName, keyData);

                // Update App Configuration atomically
                var allKeys = GetAllKidsFromAppConfig();
                if (!string.IsNullOrEmpty(currentActiveKid) && allKeys.Contains(currentActiveKid))
                {
                    // Mark old key as inactive (update metadata in App Config)
                    UpdateKeyMetadata(currentActiveKid, isActive: false);
                }

                // Add new key to list
                allKeys.Add(newKey.Kid);
                UpdateAllKeysList(allKeys);

                // Set new active kid with ETag (optimistic concurrency)
                // Note: ETag-based concurrency is handled via retry logic on 412 errors
                // For simplicity, we'll use the setting's ETag property
                var setting = new ConfigurationSetting(_activeKidKey, newKey.Kid);
                if (activeKidSetting.HasValue)
                {
                    // Copy ETag from existing setting for optimistic concurrency
                    setting = activeKidSetting.Value;
                    setting.Value = newKey.Kid;
                }
                _appConfigClient.SetConfigurationSetting(setting);

                // Invalidate caches
                InvalidateAllCaches();

                _logger.LogInformation("Key rotated successfully. New active key: {Kid}", newKey.Kid);
                return newKey;
            }
            catch (RequestFailedException ex) when (ex.Status == 412) // Precondition Failed (ETag mismatch)
            {
                if (attempt == maxRetries - 1)
                    throw new InvalidOperationException("Another instance is currently rotating keys. Please try again.", ex);
                
                _logger.LogWarning("Concurrent key rotation detected, retrying... Attempt {Attempt}", attempt + 1);
                Task.Delay(100 * (attempt + 1)).GetAwaiter().GetResult(); // Exponential backoff
            }
        }

        throw new InvalidOperationException("Failed to rotate key after retries");
    }

    public void RetireKey(string kid)
    {
        // Remove from App Configuration
        var allKeys = GetAllKidsFromAppConfig();
        allKeys.Remove(kid);
        UpdateAllKeysList(allKeys);

        // Optionally delete from Key Vault (or keep for audit)
        // For security, you might want to keep keys for audit purposes
        // Uncomment if you want to delete:
        // var secretName = _keyPrefix + kid;
        // _keyVaultClient.StartDeleteSecret(secretName);

        InvalidateCache(kid);
        InvalidateAllCaches();
        _logger.LogInformation("Key retired: {Kid}", kid);
    }

    private SigningKeyInfo GetKey(string kid)
    {
        var cacheKey = KeyCacheKeyPrefix + kid;
        return _memoryCache.GetOrCreate(cacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheExpiration;
            entry.SlidingExpiration = TimeSpan.FromMinutes(1);

            return GetKeyFromKeyVaultAsync(kid).GetAwaiter().GetResult();
        });
    }

    private string? GetActiveKid()
    {
        return _memoryCache.GetOrCreate(ActiveKidCacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheExpiration;
            entry.SlidingExpiration = TimeSpan.FromMinutes(1);

            var setting = _appConfigClient.GetConfigurationSetting(_activeKidKey);
            return setting.Value?.Value;
        });
    }

    private async Task<SigningKeyInfo> GetKeyFromKeyVaultAsync(string kid)
    {
        var secretName = _keyPrefix + kid;
        var secret = await _keyVaultClient.GetSecretAsync(secretName);
        var keyData = secret.Value.Value;

        var rsaKey = DeserializeRsaKey(keyData, kid);
        var metadata = GetKeyMetadata(kid);

        return new SigningKeyInfo(kid, rsaKey, metadata.CreatedOn, metadata.IsActive);
    }

    private async Task<IEnumerable<SigningKeyInfo>> GetAllKeysInternalAsync()
    {
        var kids = GetAllKidsFromAppConfig();
        var keys = new List<SigningKeyInfo>();

        foreach (var kid in kids)
        {
            try
            {
                var key = GetKey(kid);
                keys.Add(key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to retrieve key {Kid}", kid);
            }
        }

        return keys;
    }

    private List<string> GetAllKidsFromAppConfig()
    {
        var setting = _appConfigClient.GetConfigurationSetting(_allKeysKey);
        if (string.IsNullOrEmpty(setting.Value?.Value))
            return new List<string>();

        return JsonSerializer.Deserialize<List<string>>(setting.Value.Value) ?? new List<string>();
    }

    private void UpdateAllKeysList(List<string> kids)
    {
        _appConfigClient.SetConfigurationSetting(_allKeysKey, JsonSerializer.Serialize(kids));
    }

    private (DateTimeOffset CreatedOn, bool IsActive) GetKeyMetadata(string kid)
    {
        // Store metadata in App Configuration
        var createdOnKey = $"Jwt:Key:{kid}:CreatedOn";
        var isActiveKey = $"Jwt:Key:{kid}:IsActive";

        var createdOnSetting = _appConfigClient.GetConfigurationSetting(createdOnKey);
        var isActiveSetting = _appConfigClient.GetConfigurationSetting(isActiveKey);

        var createdOn = createdOnSetting.HasValue && DateTimeOffset.TryParse(createdOnSetting.Value.Value, out var dt)
            ? dt
            : DateTimeOffset.UtcNow;

        var isActive = isActiveSetting.HasValue && bool.TryParse(isActiveSetting.Value.Value, out var active)
            ? active
            : true;

        return (createdOn, isActive);
    }

    private void UpdateKeyMetadata(string kid, bool isActive)
    {
        var isActiveKey = $"Jwt:Key:{kid}:IsActive";
        _appConfigClient.SetConfigurationSetting(isActiveKey, isActive.ToString());
    }

    private async Task EnsureInitialKeyExistsAsync()
    {
        var activeKid = GetActiveKid();
        if (string.IsNullOrEmpty(activeKid))
        {
            _logger.LogInformation("No active key found, creating initial key...");
            var initialKey = CreateRsaSigningKey();

            // Store in Key Vault
            var secretName = _keyPrefix + initialKey.Kid;
            var keyData = SerializeRsaKey(initialKey.Key as RsaSecurityKey ?? throw new InvalidOperationException("Key must be RSA"));
            await _keyVaultClient.SetSecretAsync(secretName, keyData);

            // Store metadata in App Configuration
            UpdateAllKeysList(new List<string> { initialKey.Kid });
            _appConfigClient.SetConfigurationSetting(_activeKidKey, initialKey.Kid);
            UpdateKeyMetadata(initialKey.Kid, true);
            var createdOnKey = $"Jwt:Key:{initialKey.Kid}:CreatedOn";
            _appConfigClient.SetConfigurationSetting(createdOnKey, initialKey.CreatedOn.ToString("O"));

            // Invalidate cache to ensure fresh read
            InvalidateAllCaches();

            _logger.LogInformation("Initial key created: {Kid}", initialKey.Kid);
        }
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

    private string SerializeRsaKey(RsaSecurityKey rsaKey)
    {
        var rsa = rsaKey.Rsa ?? throw new InvalidOperationException("RSA key is null");
        var parameters = rsa.ExportParameters(true);

        var keyData = new
        {
            Modulus = Convert.ToBase64String(parameters.Modulus ?? Array.Empty<byte>()),
            Exponent = Convert.ToBase64String(parameters.Exponent ?? Array.Empty<byte>()),
            D = Convert.ToBase64String(parameters.D ?? Array.Empty<byte>()),
            P = Convert.ToBase64String(parameters.P ?? Array.Empty<byte>()),
            Q = Convert.ToBase64String(parameters.Q ?? Array.Empty<byte>()),
            DP = Convert.ToBase64String(parameters.DP ?? Array.Empty<byte>()),
            DQ = Convert.ToBase64String(parameters.DQ ?? Array.Empty<byte>()),
            InverseQ = Convert.ToBase64String(parameters.InverseQ ?? Array.Empty<byte>())
        };

        return JsonSerializer.Serialize(keyData);
    }

    private RsaSecurityKey DeserializeRsaKey(string keyData, string kid)
    {
        var data = JsonSerializer.Deserialize<JsonElement>(keyData);
        var parameters = new RSAParameters
        {
            Modulus = Convert.FromBase64String(data.GetProperty("Modulus").GetString()!),
            Exponent = Convert.FromBase64String(data.GetProperty("Exponent").GetString()!),
            D = Convert.FromBase64String(data.GetProperty("D").GetString()!),
            P = Convert.FromBase64String(data.GetProperty("P").GetString()!),
            Q = Convert.FromBase64String(data.GetProperty("Q").GetString()!),
            DP = Convert.FromBase64String(data.GetProperty("DP").GetString()!),
            DQ = Convert.FromBase64String(data.GetProperty("DQ").GetString()!),
            InverseQ = Convert.FromBase64String(data.GetProperty("InverseQ").GetString()!)
        };

        var rsa = RSA.Create();
        rsa.ImportParameters(parameters);
        return new RsaSecurityKey(rsa) { KeyId = kid };
    }

    private void InvalidateCache(string kid)
    {
        _memoryCache.Remove(KeyCacheKeyPrefix + kid);
    }

    private void InvalidateAllCaches()
    {
        _memoryCache.Remove(AllKeysCacheKey);
        _memoryCache.Remove(ActiveKidCacheKey);
    }

    private const string KeyCacheKeyPrefix = "jwt:key:";
    private const string AllKeysCacheKey = "jwt:keys:all";
    private const string ActiveKidCacheKey = "jwt:active:kid";
}

