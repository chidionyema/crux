using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Crux.Identity;

/// <summary>
/// JWT generation + validation. RS256, key from <see cref="IJwtSigningKeyProvider"/>.
/// Includes JTI revocation check on validation and httpOnly secure-cookie helpers.
/// </summary>
public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<JwtTokenService> _logger;
    private readonly ITokenRevocationService _revocationService;
    private readonly IJwtSigningKeyProvider _signingKeyProvider;
    private readonly IHostEnvironment _environment;

    private static readonly string[] RsaSha256Algorithms = { SecurityAlgorithms.RsaSha256 };

    public JwtTokenService(
        IOptions<JwtOptions> jwtOptions,
        ILogger<JwtTokenService> logger,
        ITokenRevocationService revocationService,
        IJwtSigningKeyProvider signingKeyProvider,
        IHostEnvironment environment)
    {
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
        _revocationService = revocationService;
        _signingKeyProvider = signingKeyProvider;
        _environment = environment;
    }

    public Task<JwtSecurityToken> GenerateTokenAsync(
        string userId, string userName, string email,
        IList<string> roles, IList<Claim> customClaims,
        DateTime expiration, CancellationToken ct = default)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, userName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        if (!string.IsNullOrEmpty(email))
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, email));

        if (roles.Count == 0)
        {
            claims.Add(new Claim(AuthConstants.RolePendingClaim, "true"));
        }
        else
        {
            claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        }

        claims.AddRange(customClaims);

        var signingCredentials = new SigningCredentials(_signingKeyProvider.SigningKey, SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            expires: expiration,
            signingCredentials: signingCredentials);
        token.Header["kid"] = _signingKeyProvider.KeyId;

        _logger.LogInformation("Token generated for user {UserId} with {RoleCount} roles", userId, roles.Count);
        return Task.FromResult(token);
    }

    public async Task<ClaimsPrincipal?> ValidateTokenAsync(string tokenString, bool validateLifetime = true, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(tokenString)) return null;

        var tokenHandler = new JwtSecurityTokenHandler();
        ClaimsPrincipal principal;
        try
        {
            principal = tokenHandler.ValidateToken(tokenString, GetTokenValidationParameters(validateLifetime), out _);
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "Token validation failed");
            return null;
        }

        var jti = principal.FindFirstValue(JwtRegisteredClaimNames.Jti);
        if (!string.IsNullOrEmpty(jti) && await _revocationService.IsTokenRevokedAsync(jti, ct))
        {
            _logger.LogWarning("Token {Jti} has been revoked", jti);
            return null;
        }

        return principal;
    }

    public TokenValidationParameters GetTokenValidationParameters(bool validateLifetime = true) => new()
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = _signingKeyProvider.SigningKey,
        ValidAlgorithms = RsaSha256Algorithms,
        ValidateIssuer = true,
        ValidIssuer = _jwtOptions.Issuer,
        ValidateAudience = true,
        ValidAudience = _jwtOptions.Audience,
        ValidateLifetime = validateLifetime,
        ClockSkew = TimeSpan.FromSeconds(AuthConstants.ClockSkewToleranceSeconds)
    };

    public void SetSecureCookie(HttpContext context, JwtSecurityToken token)
        => AppendJwtCookie(context, new JwtSecurityTokenHandler().WriteToken(token), token.ValidTo);

    public void SetSecureCookie(HttpContext context, string tokenString)
        => AppendJwtCookie(context, tokenString, new JwtSecurityTokenHandler().ReadJwtToken(tokenString).ValidTo);

    private void AppendJwtCookie(HttpContext context, string tokenString, DateTime validTo) =>
        context.Response.Cookies.Append("jwt", tokenString, new CookieOptions
        {
            HttpOnly = true,
            Secure = _environment.IsProduction(),
            SameSite = SameSiteMode.Strict,
            Expires = new DateTimeOffset(validTo, TimeSpan.Zero),
            Path = "/",
            IsEssential = true
        });

    public void DeleteAuthCookie(HttpContext context) =>
        context.Response.Cookies.Delete("jwt", new CookieOptions
        {
            HttpOnly = true,
            Secure = _environment.IsProduction(),
            SameSite = SameSiteMode.Strict,
            Path = "/"
        });
}
