using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using JwtKeyRotationPoc.Services;

namespace JwtKeyRotationPoc.Controllers;

[ApiController]
[Route("api/download")]
public class DownloadController : ControllerBase
{
    private readonly IKeyStore _keyStore;
    private readonly IConfiguration _configuration;

    public DownloadController(IKeyStore keyStore, IConfiguration configuration)
    {
        _keyStore = keyStore;
        _configuration = configuration;
    }

    [HttpGet]
    public IActionResult Download([FromQuery] string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return BadRequest(new { error = "Token is required" });
        }

        try
        {
            var validationParameters = TokenValidationService.BuildValidationParameters(_keyStore, _configuration);
            var handler = new JwtSecurityTokenHandler();
            
            var principal = handler.ValidateToken(token, validationParameters, out var validatedToken);
            var jwtToken = validatedToken as JwtSecurityToken;

            if (jwtToken == null)
            {
                return Unauthorized(new { error = "Invalid token" });
            }

            var email = principal.FindFirst(ClaimTypes.Email)?.Value;
            var filePath = principal.FindFirst("file")?.Value;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                return BadRequest(new { error = "File path not found in token" });
            }

            // In a real application, you would validate the file exists and user has access
            // For this POC, we'll just return the file information
            return Ok(new
            {
                message = "Token validated successfully",
                email,
                filePath,
                expiresAt = jwtToken.ValidTo,
                kid = jwtToken.Header.Kid
            });
        }
        catch (SecurityTokenExpiredException)
        {
            return Unauthorized(new { error = "Token has expired" });
        }
        catch (SecurityTokenInvalidSignatureException)
        {
            return Unauthorized(new { error = "Invalid token signature" });
        }
        catch (Exception ex)
        {
            return Unauthorized(new { error = "Token validation failed", details = ex.Message });
        }
    }
}

