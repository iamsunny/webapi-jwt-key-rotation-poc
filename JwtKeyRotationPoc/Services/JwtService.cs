using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace JwtKeyRotationPoc.Services;

public class JwtService : IJwtService
{
    private readonly IConfiguration _cfg;
    private readonly IKeyStore _keys;

    public JwtService(IConfiguration cfg, IKeyStore keys)
    {
        _cfg = cfg;
        _keys = keys;
    }

    public string CreateToken(string email, string filePath, TimeSpan ttl)
    {
        var active = _keys.GetActiveKey();
        var creds = new SigningCredentials(active.Key, SecurityAlgorithms.RsaSha256);

        var header = new JwtHeader(creds);
        header["kid"] = active.Kid;

        var payload = new JwtPayload(
            issuer: _cfg["Jwt:Issuer"],
            audience: _cfg["Jwt:Audience"],
            claims: new[] { new Claim(ClaimTypes.Email, email), new Claim("file", filePath) },
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.Add(ttl)
        );

        var token = new JwtSecurityToken(header, payload);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

