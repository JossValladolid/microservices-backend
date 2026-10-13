using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using AuthService.Models;

namespace AuthService.Services;

/// <summary>
/// Centraliza la configuración JWT (leída una sola vez del entorno) y la generación de tokens.
/// </summary>
public class TokenService
{

    /// <summary>Cookie HttpOnly donde viaja el access token.</summary>
    public const string AccessTokenCookie = "authservice_access_token";

    /// <summary>Cookie HttpOnly donde viaja el refresh token.</summary>
    public const string RefreshTokenCookie = "authservice_refresh_token";

    /// <summary>Emisor de los tokens.</summary>
    public string Issuer { get; }

    /// <summary>Audiencia de los tokens.</summary>
    public string Audience { get; }

    /// <summary>Llave con la que se firman y validan los access tokens.</summary>
    public SymmetricSecurityKey SigningKey { get; }

    /// <summary>Duración del access token.</summary>
    public TimeSpan AccessTokenLifetime { get; }

    /// <summary>Duración del refresh token (sesión).</summary>
    public TimeSpan RefreshTokenLifetime { get; }

    /// <summary>
    /// Lee la configuración JWT de las variables de entorno. Falla al arrancar si falta algo.
    /// </summary>
    public TokenService()
    {
        var secret = Environment.GetEnvironmentVariable("JWT_SECRET");
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
        {
            throw new InvalidOperationException("JWT_SECRET debe estar definido y tener al menos 32 caracteres.");
        }

        Issuer = Environment.GetEnvironmentVariable("JWT_ISSUER")
            ?? throw new InvalidOperationException("JWT_ISSUER no está definido.");
        Audience = Environment.GetEnvironmentVariable("JWT_AUDIENCE")
            ?? throw new InvalidOperationException("JWT_AUDIENCE no está definido.");
        SigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

        var minutes = int.TryParse(Environment.GetEnvironmentVariable("JWT_ACCESS_EXPIRATION_MINUTES"), out var m) ? m : 15;
        var days = int.TryParse(Environment.GetEnvironmentVariable("JWT_REFRESH_EXPIRATION_DAYS"), out var d) ? d : 7;
        AccessTokenLifetime = TimeSpan.FromMinutes(minutes);
        RefreshTokenLifetime = TimeSpan.FromDays(days);
    }

    /// <summary>
    /// Parámetros con los que el middleware valida los access tokens.
    /// </summary>
    public TokenValidationParameters ValidationParameters => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = SigningKey,
        ClockSkew = TimeSpan.Zero
    };

    /// <summary>
    /// Genera un access token (JWT) con los datos y el rol del usuario.
    /// </summary>
    public string GenerateAccessToken(User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email!),
            new(ClaimTypes.Name, user.Name!)
        };

        if (user.IsAdmin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(AccessTokenLifetime),
            signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Genera un refresh token aleatorio (no es un JWT, solo un valor que se guarda en la tabla Sessions).
    /// </summary>
    public static string GenerateRefreshToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    }
}
