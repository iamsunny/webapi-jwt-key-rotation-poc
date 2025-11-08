using Microsoft.AspNetCore.Mvc;
using JwtKeyRotationPoc.Services;

namespace JwtKeyRotationPoc.Controllers;

[ApiController]
[Route("api/link")]
public class LinkController : ControllerBase
{
    private readonly IJwtService _jwtService;

    public LinkController(IJwtService jwtService)
    {
        _jwtService = jwtService;
    }

    [HttpPost("secure")]
    public IActionResult CreateSecureLink([FromBody] CreateLinkRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.FilePath))
        {
            return BadRequest(new { error = "Email and FilePath are required" });
        }

        var ttl = request.TtlMinutes > 0 
            ? TimeSpan.FromMinutes(request.TtlMinutes) 
            : TimeSpan.FromHours(1);

        var token = _jwtService.CreateToken(request.Email, request.FilePath, ttl);
        var downloadUrl = $"{Request.Scheme}://{Request.Host}/api/download?token={Uri.EscapeDataString(token)}";

        return Ok(new { token, downloadUrl, expiresIn = ttl.TotalMinutes });
    }

    public record CreateLinkRequest(string Email, string FilePath, int TtlMinutes = 60);
}

