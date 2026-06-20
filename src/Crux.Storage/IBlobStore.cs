namespace Crux.Storage;

/// <summary>
/// Cloud object store abstraction. Provides presigned upload/download URLs
/// and object deletion. Compatible with S3, R2, MinIO, GCS, and Azure Blob
/// (any provider supporting S3-compatible presigned URLs).
/// </summary>
public interface IBlobStore
{
    /// <summary>False when storage is not configured (no credentials). Callers should surface a 503.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Generates a presigned PUT URL for direct client-side upload.
    /// The URL is valid for the configured expiry period (default 5 minutes).
    /// </summary>
    /// <param name="key">Object key (path) in the bucket.</param>
    /// <param name="contentType">MIME type of the content being uploaded.</param>
    /// <param name="expiryMinutes">Optional override for URL expiry in minutes.</param>
    /// <returns>A presigned URL the client can PUT to.</returns>
    string GetUploadPresignUrl(string key, string contentType, int? expiryMinutes = null);

    /// <summary>
    /// Generates a presigned GET URL for time-limited object download.
    /// </summary>
    /// <param name="key">Object key (path) in the bucket.</param>
    /// <param name="expiryMinutes">Optional override for URL expiry in minutes. Default 5.</param>
    /// <returns>A presigned URL the client can GET.</returns>
    string GetDownloadPresignUrl(string key, int? expiryMinutes = null);

    /// <summary>
    /// Deletes an object from the store. No-op if the object doesn't exist.
    /// </summary>
    /// <param name="key">Object key (path) in the bucket.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteAsync(string key, CancellationToken ct = default);
}

/// <summary>
/// Configuration for the blob store. Bound from "Storage" configuration section.
/// When <see cref="Enabled"/> is false, presigned URLs return a no-op placeholder.
/// </summary>
public sealed class BlobStoreOptions
{
    public const string SectionName = "Storage";

    /// <summary>Set to false in environments without object storage (presigned URLs return placeholders).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>S3-compatible service endpoint URL (e.g. https://{account}.r2.cloudflarestorage.com).</summary>
    public string ServiceUrl { get; set; } = string.Empty;

    /// <summary>Access key ID.</summary>
    public string AccessKey { get; set; } = string.Empty;

    /// <summary>Secret access key.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Bucket name.</summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>AWS region or "auto" for Tigris/R2. Default "us-east-1".</summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>Presigned URL TTL in minutes. Default 5.</summary>
    public int PresignedUrlExpiryMinutes { get; set; } = 5;
}
