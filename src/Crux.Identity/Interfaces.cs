using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;

namespace Crux.Identity;

/// <summary>JWT generation and validation.</summary>
public interface IJwtTokenService
{
    Task<JwtSecurityToken> GenerateTokenAsync(string userId, string userName, string email, IList<string> roles, IList<Claim> customClaims, DateTime expiration, CancellationToken ct = default);
    Task<ClaimsPrincipal?> ValidateTokenAsync(string tokenString, bool validateLifetime = true, CancellationToken ct = default);
    TokenValidationParameters GetTokenValidationParameters(bool validateLifetime = true);
    void SetSecureCookie(HttpContext context, JwtSecurityToken token);
    void SetSecureCookie(HttpContext context, string tokenString);
    void DeleteAuthCookie(HttpContext context);
}

/// <summary>Refresh-token lifecycle.</summary>
public interface IRefreshTokenService
{
    Task<RefreshToken> GenerateRefreshTokenAsync(string userId, string? userAgent = null, string? ipAddress = null, string? accessTokenJti = null, CancellationToken ct = default);
    Task<RefreshToken> GenerateRefreshTokenAsync(string userId, Guid? familyId, string? userAgent = null, string? ipAddress = null, string? accessTokenJti = null, CancellationToken ct = default);
    Task RevokeRefreshTokensForUserAsync(string userId, CancellationToken ct = default);
    Task<(bool Success, RefreshToken? NewToken)> RotateRefreshTokenAsync(string token, string? userAgent = null, string? ipAddress = null, string? accessTokenJti = null, CancellationToken ct = default);
}

/// <summary>JTI revocation list.</summary>
public interface ITokenRevocationService
{
    Task RevokeTokenAsync(string tokenValue, string userId, DateTime expiryDate, CancellationToken ct = default);
    Task<bool> IsTokenRevokedAsync(string tokenValue, CancellationToken ct = default);
}

/// <summary>Provides the RSA signing key for RS256 JWTs + the public JWK for JWKS.</summary>
public interface IJwtSigningKeyProvider
{
    string KeyId { get; }
    RsaSecurityKey SigningKey { get; }
    JsonWebKey PublicJwk { get; }
}

/// <summary>Validates Apple Sign In identity tokens.</summary>
public interface IAppleTokenValidator
{
    Task<ClaimsPrincipal?> ValidateAsync(string identityToken, CancellationToken ct = default);
}

/// <summary>Refresh token entity.</summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Token { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public Guid FamilyId { get; set; } = Guid.NewGuid();
    public string? UserAgent { get; set; }
    public string? IpAddress { get; set; }
    public string? AccessTokenJti { get; set; }
    public bool IsUsed { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime Expires { get; set; }
}
