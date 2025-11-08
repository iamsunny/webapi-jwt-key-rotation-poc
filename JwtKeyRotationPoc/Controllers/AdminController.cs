using Microsoft.AspNetCore.Mvc;
using JwtKeyRotationPoc.Services;

namespace JwtKeyRotationPoc.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly IKeyStore _keys;
    
    public AdminController(IKeyStore keys) => _keys = keys;

    [HttpPost("rotate-key")]
    public IActionResult Rotate()
    {
        var newKey = _keys.CreateAndActivateNewKey();
        return Ok(new { newKey.Kid, newKey.CreatedOn, newKey.IsActive });
    }

    [HttpPost("retire/{kid}")]
    public IActionResult Retire(string kid)
    {
        _keys.RetireKey(kid);
        return Ok(new { message = $"Key {kid} retired successfully" });
    }

    [HttpGet("keys")]
    public IActionResult Keys() => Ok(_keys.GetAllKeys().Select(k => new { k.Kid, k.CreatedOn, k.IsActive }));
}

