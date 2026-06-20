using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Crux.Identity;

/// <summary>
/// Extension methods for registering Crux Identity services.
/// </summary>
public static class IdentityServiceCollectionExtensions
{
    /// <summary>
    /// Registers the core Crux Identity services: JWT token service,
    /// signing key provider (from config), and Apple token validator.
    /// Consumers must also call <c>AddAuthentication().AddJwtBearer()</c>
    /// with <c>JwtTokenService.GetTokenValidationParameters()</c>.
    /// </summary>
    public static IServiceCollection AddCruxIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        var jwtSection = configuration.GetSection(JwtOptions.SectionName);
        var signingKeyPem = jwtSection["SigningKeyPem"];
        var keyId = jwtSection["KeyId"] ?? "config-1";

        if (!string.IsNullOrEmpty(signingKeyPem))
        {
            services.TryAddSingleton<IJwtSigningKeyProvider>(
                _ => new ConfigJwtSigningKeyProvider(signingKeyPem, keyId));
        }
        else
        {
            // No persisted signing key configured. An ephemeral (per-process) RS256 key is fine for
            // Development/testing, but in Production it is a silent footgun: every instance signs with
            // a different key, so all cross-instance token validation fails and every session dies on
            // restart/scale-out — with no startup error. Fail closed instead (matches TIE's guard).
            services.TryAddSingleton<IJwtSigningKeyProvider>(sp =>
            {
                var env = sp.GetService<IHostEnvironment>();
                if (env is null || env.IsProduction())
                {
                    throw new InvalidOperationException(
                        "Jwt:SigningKeyPem is not configured. A persisted RS256 signing key is required "
                        + "in Production: an ephemeral key breaks cross-instance token validation and "
                        + "destroys all sessions on restart/scale-out. Configure Jwt:SigningKeyPem.");
                }
                return ConfigJwtSigningKeyProvider.CreateEphemeral(keyId);
            });
        }

        services.TryAddSingleton<IJwtTokenService, JwtTokenService>();
        services.TryAddSingleton<IAppleTokenValidator, AppleTokenValidator>();

        return services;
    }
}
