namespace JwtKeyRotationPoc.Services;

public interface IJwtService
{
    string CreateToken(string email, string filePath, TimeSpan ttl);
}

