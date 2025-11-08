namespace JwtKeyRotationPoc.Services;

/// <summary>
/// Interface for distributed key storage that can be shared across multiple instances
/// </summary>
public interface IDistributedKeyStore
{
    Task<SigningKeyInfo?> GetKeyAsync(string kid);
    Task<IEnumerable<SigningKeyInfo>> GetAllKeysAsync();
    Task<string?> GetActiveKidAsync();
    Task SaveKeyAsync(SigningKeyInfo key);
    Task SetActiveKidAsync(string kid);
    Task DeleteKeyAsync(string kid);
    Task<bool> TryAcquireLockAsync(string lockKey, TimeSpan timeout);
    Task ReleaseLockAsync(string lockKey);
}

