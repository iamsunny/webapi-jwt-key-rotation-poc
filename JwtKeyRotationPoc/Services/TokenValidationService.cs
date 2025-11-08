using Microsoft.IdentityModel.Tokens;

namespace JwtKeyRotationPoc.Services;

public static class TokenValidationService
{
    public static TokenValidationParameters BuildValidationParameters(IKeyStore keyStore, IConfiguration cfg)
    {
        // Create a dictionary of keys by kid for efficient lookup
        var keysByKid = keyStore.GetAllKeys()
            .ToDictionary(k => k.Kid, k => k.Key);

        return new TokenValidationParameters
        {
            ValidIssuer = cfg["Jwt:Issuer"],
            ValidAudience = cfg["Jwt:Audience"],
            ValidateLifetime = true,
            ValidateIssuer = true,
            ValidateAudience = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            // Explicitly resolve keys by kid from the token header
            // This ensures tokens signed with old keys still validate after rotation
            IssuerSigningKeyResolver = (token, securityToken, kid, validationParameters) =>
            {
                if (kid != null && keysByKid.TryGetValue(kid, out var key))
                {
                    return new[] { key };
                }
                // If kid not found, return empty array (validation will fail)
                // This is intentional - we only accept tokens signed with known keys
                return Array.Empty<SecurityKey>();
            }
        };
    }
}

