using System.ComponentModel.DataAnnotations;

namespace Crux.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SigningKeyPem { get; set; } = string.Empty;
    public string KeyId { get; set; } = "config-1";

    [Range(5, 1440)]
    public int TokenExpiryMinutes { get; set; } = 15;

    [Range(1, 90)]
    public int RefreshTokenExpiryDays { get; set; } = 7;
}

public static class AuthConstants
{
    public const int ClockSkewToleranceSeconds = 30;
    public const int MaxFailedLoginAttempts = 5;
    public const int LockoutDurationMinutes = 15;
    public const string RolePendingClaim = "role_pending";
}
