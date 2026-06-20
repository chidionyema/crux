using System.Text.Json.Serialization;

namespace Crux.Identity;

public sealed class AuthResponseDto
{
    [JsonPropertyName("token")] public string Token { get; set; } = string.Empty;
    [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
    [JsonPropertyName("user_id")] public string UserId { get; set; } = string.Empty;
    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("expires")] public DateTime Expires { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("role_pending")] public bool RolePending { get; set; }
}

public sealed class UserProfileDto
{
    [JsonPropertyName("first_name")] public string FirstName { get; set; } = string.Empty;
    [JsonPropertyName("last_name")] public string LastName { get; set; } = string.Empty;
    [JsonPropertyName("display_name")] public string DisplayName => $"{FirstName} {LastName}".Trim();
    [JsonPropertyName("phone")] public string Phone { get; set; } = string.Empty;
    [JsonPropertyName("bio")] public string Bio { get; set; } = string.Empty;
    [JsonPropertyName("website")] public string Website { get; set; } = string.Empty;
    [JsonPropertyName("avatar_url")] public string AvatarUrl { get; set; } = string.Empty;
    [JsonPropertyName("country")] public string Country { get; set; } = "GB";
}

public sealed class SessionDto
{
    [JsonPropertyName("family_id")] public Guid FamilyId { get; set; }
    [JsonPropertyName("user_agent")] public string? UserAgent { get; set; }
    [JsonPropertyName("ip_address")] public string? IpAddress { get; set; }
    [JsonPropertyName("created_at")] public DateTime CreatedAt { get; set; }
    [JsonPropertyName("expires")] public DateTime Expires { get; set; }
    [JsonPropertyName("is_current")] public bool IsCurrent { get; set; }
}
