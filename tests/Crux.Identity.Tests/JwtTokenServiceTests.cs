using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Crux.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Crux.Identity.Tests;

/// <summary>
/// Proves the JWT money/identity core: RS256 sign + validate round-trip, signature tamper rejection,
/// JTI revocation, and the RS256-only algorithm pin (defends against the HS256 algorithm-confusion
/// attack). These are the founder-fence behaviours that previously had ZERO coverage.
/// </summary>
public sealed class JwtTokenServiceTests
{
    private const string Issuer = "https://issuer.test";
    private const string Audience = "keystone-tests";

    private static JwtTokenService NewService(FakeRevocation revocation, string env = "Development")
    {
        var opts = Options.Create(new JwtOptions { Issuer = Issuer, Audience = Audience });
        var signingKey = ConfigJwtSigningKeyProvider.CreateEphemeral("test-key-1");
        return new JwtTokenService(
            opts, NullLogger<JwtTokenService>.Instance, revocation, signingKey,
            new FakeHostEnvironment(env));
    }

    private static async Task<string> IssueAsync(JwtTokenService svc, params string[] roles)
    {
        var token = await svc.GenerateTokenAsync(
            "user-1", "Test User", "user@test.com", roles, Array.Empty<Claim>(),
            DateTime.UtcNow.AddMinutes(15));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task Generate_then_validate_round_trips_with_RS256()
    {
        var svc = NewService(new FakeRevocation());
        var tokenString = await IssueAsync(svc, "admin");

        var parsed = new JwtSecurityTokenHandler().ReadJwtToken(tokenString);
        Assert.Equal("RS256", parsed.SignatureAlgorithm);
        Assert.Equal("test-key-1", parsed.Header["kid"]);

        var principal = await svc.ValidateTokenAsync(tokenString);

        Assert.NotNull(principal);
        // JwtSecurityTokenHandler remaps the inbound "sub" claim to ClaimTypes.NameIdentifier on
        // validation — that is the identity claim consumers (ICurrentUserService) actually read.
        Assert.Equal("user-1", principal!.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Contains(principal.Claims, c => c.Type == ClaimTypes.Role && c.Value == "admin");
    }

    [Fact]
    public async Task Validate_rejects_a_tampered_signature()
    {
        var svc = NewService(new FakeRevocation());
        var tokenString = await IssueAsync(svc, "admin");

        // Corrupt the signature segment — must fail signature validation, not throw out.
        var parts = tokenString.Split('.');
        parts[2] = parts[2][..^4] + (parts[2].EndsWith("AAAA", StringComparison.Ordinal) ? "BBBB" : "AAAA");
        var tampered = string.Join('.', parts);

        var principal = await svc.ValidateTokenAsync(tampered);

        Assert.Null(principal);
    }

    [Fact]
    public async Task Validate_returns_null_for_a_revoked_jti()
    {
        var revocation = new FakeRevocation();
        var svc = NewService(revocation);
        var tokenString = await IssueAsync(svc, "admin");

        var jti = new JwtSecurityTokenHandler().ReadJwtToken(tokenString).Id;
        Assert.False(string.IsNullOrEmpty(jti));
        revocation.Revoke(jti);

        var principal = await svc.ValidateTokenAsync(tokenString);

        Assert.Null(principal);
    }

    [Fact]
    public void Validation_parameters_pin_RS256_only()
    {
        var svc = NewService(new FakeRevocation());

        var parms = svc.GetTokenValidationParameters();

        Assert.NotNull(parms.ValidAlgorithms);
        Assert.Equal(new[] { SecurityAlgorithms.RsaSha256 }, parms.ValidAlgorithms!);
        Assert.True(parms.ValidateIssuerSigningKey);
        Assert.Equal(Issuer, parms.ValidIssuer);
        Assert.Equal(Audience, parms.ValidAudience);
    }

    [Fact]
    public async Task Validate_rejects_an_expired_token_when_lifetime_checked()
    {
        var svc = NewService(new FakeRevocation());
        var token = await svc.GenerateTokenAsync(
            "user-1", "Test User", "user@test.com", new[] { "admin" }, Array.Empty<Claim>(),
            DateTime.UtcNow.AddMinutes(-30)); // already expired, beyond the 30s clock skew
        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        Assert.Null(await svc.ValidateTokenAsync(tokenString, validateLifetime: true));
        Assert.NotNull(await svc.ValidateTokenAsync(tokenString, validateLifetime: false));
    }

    private sealed class FakeRevocation : ITokenRevocationService
    {
        private readonly HashSet<string> _revoked = new(StringComparer.Ordinal);
        public void Revoke(string jti) => _revoked.Add(jti);
        public Task RevokeTokenAsync(string tokenValue, string userId, DateTime expiryDate, CancellationToken ct = default)
        {
            _revoked.Add(tokenValue);
            return Task.CompletedTask;
        }
        public Task<bool> IsTokenRevokedAsync(string tokenValue, CancellationToken ct = default) =>
            Task.FromResult(_revoked.Contains(tokenValue));
    }

    internal sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Crux.Identity.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
