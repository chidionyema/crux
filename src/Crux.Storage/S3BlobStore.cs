using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Crux.Storage;

/// <summary>
/// S3-compatible implementation of <see cref="IBlobStore"/>.
/// Works with AWS S3, Cloudflare R2, Tigris, MinIO, and any provider
/// supporting the S3 presigned-URL API.
/// </summary>
public sealed class S3BlobStore : IBlobStore
{
    private readonly IAmazonS3 _s3;
    private readonly BlobStoreOptions _opts;
    private readonly Protocol _presignProtocol;

    public S3BlobStore(IAmazonS3 s3, IOptions<BlobStoreOptions> opts)
    {
        _s3 = s3;
        _opts = opts.Value;
        IsConfigured = _opts.Enabled && !string.IsNullOrEmpty(_opts.ServiceUrl)
            && !string.IsNullOrEmpty(_opts.AccessKey) && !string.IsNullOrEmpty(_opts.SecretKey)
            && !string.IsNullOrEmpty(_opts.BucketName);
        _presignProtocol = string.IsNullOrEmpty(_opts.ServiceUrl)
            || _opts.ServiceUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? Protocol.HTTPS
            : Protocol.HTTP;
    }

    public bool IsConfigured { get; }

    public string GetUploadPresignUrl(string key, string contentType, int? expiryMinutes = null)
    {
        if (!_opts.Enabled)
            return $"https://storage-disabled.local/{_opts.BucketName}/{key}";

        var expiry = expiryMinutes ?? _opts.PresignedUrlExpiryMinutes;

        var req = new GetPreSignedUrlRequest
        {
            BucketName = _opts.BucketName,
            Key = key,
            Verb = HttpVerb.PUT,
            ContentType = contentType,
            Expires = DateTime.UtcNow.AddMinutes(expiry),
            Protocol = _presignProtocol,
        };

        return _s3.GetPreSignedURL(req);
    }

    public string GetDownloadPresignUrl(string key, int? expiryMinutes = null)
    {
        if (!_opts.Enabled)
            return $"https://storage-disabled.local/{_opts.BucketName}/{key}";

        var expiry = expiryMinutes ?? _opts.PresignedUrlExpiryMinutes;

        return _s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _opts.BucketName,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(expiry),
            Protocol = _presignProtocol,
        });
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        if (!_opts.Enabled) return;
        await _s3.DeleteObjectAsync(_opts.BucketName, key, ct);
    }
}

/// <summary>
/// Extension methods for registering storage services with the DI container.
/// </summary>
public static class StorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IBlobStore"/> with the S3 implementation.
    /// Binds the <c>Storage</c> configuration section to <see cref="BlobStoreOptions"/>.
    /// </summary>
    public static IServiceCollection AddCruxStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(BlobStoreOptions.SectionName);
        var opts = new BlobStoreOptions();
        section.Bind(opts);

        services.Configure<BlobStoreOptions>(section);
        services.TryAddSingleton<IBlobStore, S3BlobStore>();

        // Always register IAmazonS3 so S3BlobStore can be constructed.
        // When disabled or unconfigured, S3BlobStore.IsConfigured returns false.
        services.TryAddSingleton<IAmazonS3>(sp =>
        {
            var s3Config = new AmazonS3Config
            {
                ServiceURL = opts.ServiceUrl,
                ForcePathStyle = true,
            };
            if (!string.IsNullOrEmpty(opts.Region) && !string.Equals(opts.Region, "auto", StringComparison.OrdinalIgnoreCase))
            {
                s3Config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(opts.Region);
            }
            else
            {
                // Cloudflare R2 / Tigris use the pseudo-region "auto". The AWS SDK still needs an
                // explicit signing region for SigV4 when only a ServiceURL is set (no RegionEndpoint);
                // without this, presigned URLs can fail signature validation against R2.
                s3Config.AuthenticationRegion = "auto";
            }
            return new AmazonS3Client(opts.AccessKey, opts.SecretKey, s3Config);
        });

        return services;
    }
}
