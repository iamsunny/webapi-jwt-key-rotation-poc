using Microsoft.IdentityModel.Tokens;

namespace JwtKeyRotationPoc.Services;

public record SigningKeyInfo(string Kid, SecurityKey Key, DateTimeOffset CreatedOn, bool IsActive);

public interface IKeyStore
{
    SigningKeyInfo GetActiveKey();
    IEnumerable<SigningKeyInfo> GetAllKeys();
    SigningKeyInfo CreateAndActivateNewKey();
    void RetireKey(string kid);
}

