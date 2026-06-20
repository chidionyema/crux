using System.Security.Cryptography;
using Crux.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Crux.Identity.Tests;

/// <summary>
/// Proves the signing-key-provider registration in <c>AddCruxIdentity</c>, including the P1 fix
/// restored this session: in Production an absent <c>Jwt:SigningKeyPem</c> must FAIL CLOSED rather
/// than silently mint a per-process ephemeral RS256 key (which breaks cross-instance token validation
/// and kills every session on restart/scale-out, with no startup error).
/// </summary>
public sealed class AddCruxIdentityTests
{
    private static IJwtSigningKeyProvider? ResolveSigningKeyProvider(
        IDictionary<string, string?> config, string env)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(
            new JwtTokenServiceTests.FakeHostEnvironment(env));
        services.AddCruxIdentity(configuration);

        return services.BuildServiceProvider().GetRequiredService<IJwtSigningKeyProvider>();
    }

    [Fact]
    public void Production_without_signing_key_fails_closed()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ResolveSigningKeyProvider(new Dictionary<string, string?>(StringComparer.Ordinal), "Production"));

        Assert.Contains("SigningKeyPem", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Development_without_signing_key_allows_ephemeral()
    {
        var provider = ResolveSigningKeyProvider(
            new Dictionary<string, string?>(StringComparer.Ordinal), "Development");

        Assert.NotNull(provider);
        Assert.NotNull(provider!.SigningKey);
    }

    [Fact]
    public void Configured_signing_key_is_used_even_in_production()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportRSAPrivateKeyPem();
        var config = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Jwt:SigningKeyPem"] = pem,
            ["Jwt:KeyId"] = "prod-key-42",
        };

        var provider = ResolveSigningKeyProvider(config, "Production");

        Assert.NotNull(provider);
        Assert.Equal("prod-key-42", provider!.KeyId);
        // A persisted key is stable: a second registration yields the same public modulus.
        var second = ResolveSigningKeyProvider(config, "Production");
        Assert.Equal(provider.PublicJwk.N, second!.PublicJwk.N);
    }
}
