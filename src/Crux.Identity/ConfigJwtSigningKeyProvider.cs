using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Crux.Identity;

/// <summary>
/// <see cref="IJwtSigningKeyProvider"/> that loads the RSA private key from configuration
/// instead of Vault. Reads <c>Jwt:SigningKeyPem</c> (raw PEM or base64-encoded PEM).
/// </summary>
public sealed class ConfigJwtSigningKeyProvider : IJwtSigningKeyProvider
{
    public string KeyId { get; }
    public RsaSecurityKey SigningKey { get; }
    public JsonWebKey PublicJwk { get; }

    public ConfigJwtSigningKeyProvider(string privateKeyPem, string keyId)
    {
        var pem = privateKeyPem.Contains("-----BEGIN", StringComparison.Ordinal)
            ? privateKeyPem
            : Encoding.UTF8.GetString(Convert.FromBase64String(privateKeyPem));

        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);

        SigningKey = new RsaSecurityKey(rsa) { KeyId = keyId };
        KeyId = keyId;

        var pub = rsa.ExportParameters(includePrivateParameters: false);
        PublicJwk = new JsonWebKey
        {
            Kty = "RSA",
            Use = "sig",
            Alg = SecurityAlgorithms.RsaSha256,
            Kid = keyId,
            N = Base64UrlEncoder.Encode(pub.Modulus!),
            E = Base64UrlEncoder.Encode(pub.Exponent!),
        };
    }

    public static ConfigJwtSigningKeyProvider CreateEphemeral(string keyId = "ephemeral-1")
    {
        using var rsa = RSA.Create(2048);
        return new ConfigJwtSigningKeyProvider(rsa.ExportRSAPrivateKeyPem(), keyId);
    }
}
