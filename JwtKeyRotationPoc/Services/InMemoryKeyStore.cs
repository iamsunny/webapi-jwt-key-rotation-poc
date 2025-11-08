using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace JwtKeyRotationPoc.Services;

public class InMemoryKeyStore : IKeyStore
{
    private readonly ConcurrentDictionary<string, SigningKeyInfo> _keys = new();
    private string _activeKid;

    public InMemoryKeyStore()
    {
        var initial = CreateRsaSigningKey();
        _activeKid = initial.Kid;
        _keys[initial.Kid] = initial;
    }

    private SigningKeyInfo CreateRsaSigningKey()
    {
        var rsa = RSA.Create(2048);
        // Export parameters to create a new RSA instance that won't be disposed
        var parameters = rsa.ExportParameters(true);
        rsa.Dispose();
        
        // Create a new RSA instance from the exported parameters
        var newRsa = RSA.Create();
        newRsa.ImportParameters(parameters);
        
        var rsaKey = new RsaSecurityKey(newRsa) { KeyId = Guid.NewGuid().ToString("N") };
        return new SigningKeyInfo(rsaKey.KeyId!, rsaKey, DateTimeOffset.UtcNow, true);
    }

    public SigningKeyInfo GetActiveKey() => _keys[_activeKid];

    public IEnumerable<SigningKeyInfo> GetAllKeys() => _keys.Values;

    public SigningKeyInfo CreateAndActivateNewKey()
    {
        var newKey = CreateRsaSigningKey();
        if (_activeKid != null && _keys.TryGetValue(_activeKid, out var old))
            _keys[_activeKid] = old with { IsActive = false };
        _keys[newKey.Kid] = newKey;
        _activeKid = newKey.Kid;
        return newKey;
    }

    public void RetireKey(string kid) => _keys.TryRemove(kid, out _);
}

