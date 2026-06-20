using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Crux.Identity;

/// <summary>
/// Validates Apple Sign In identity tokens against Apple's public JWKS endpoint.
/// Requires <c>Apple:BundleId</c> configuration.
/// </summary>
public sealed class AppleTokenValidator : IAppleTokenValidator
{
    private readonly IConfiguration _config;
    private readonly ILogger<AppleTokenValidator> _logger;
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configurationManager;

    public AppleTokenValidator(IConfiguration config, ILogger<AppleTokenValidator> logger)
    {
        _config = config;
        _logger = logger;
        _configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            "https://appleid.apple.com/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever());
    }

    public async Task<ClaimsPrincipal?> ValidateAsync(string identityToken, CancellationToken ct)
    {
        try
        {
            var config = await _configurationManager.GetConfigurationAsync(ct);

            var bundleId = _config["Apple:BundleId"];
            if (string.IsNullOrEmpty(bundleId))
            {
                _logger.LogWarning("Apple:BundleId is not configured.");
                return null;
            }

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = "https://appleid.apple.com",
                ValidateAudience = true,
                ValidAudience = bundleId,
                ValidateLifetime = true,
                IssuerSigningKeys = config.SigningKeys
            };

            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(identityToken, validationParameters, out _);
            return principal;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Apple token validation failed.");
            return null;
        }
    }
}
