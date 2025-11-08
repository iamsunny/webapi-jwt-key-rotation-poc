using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using JwtKeyRotationPoc.Services;

namespace JwtKeyRotationPoc.Controllers;

[ApiController]
[Route(".well-known")]
public class JwksController : ControllerBase
{
    private readonly IKeyStore _keys;
    
    public JwksController(IKeyStore keys) => _keys = keys;

    [HttpGet("jwks.json")]
    public IActionResult GetJwks()
    {
        var jwks = new JsonWebKeySet();
        foreach (var k in _keys.GetAllKeys())
        {
            if (k.Key is RsaSecurityKey rsaKey)
            {
                var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(rsaKey);
                jwk.Kid = k.Kid;
                jwk.Use = "sig";
                jwks.Keys.Add(jwk);
            }
        }
        return Ok(jwks);
    }
}

