using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace Crux.Kernel.CurrentUser;

/// <summary>Ambient accessor for the authenticated user.</summary>
public interface ICurrentUserService
{
    string? UserId { get; }
    string? ClientIp { get; }
    bool IsAuthenticated { get; }
}

/// <summary>HttpContext-backed implementation; identity is sourced from JWT claims (anti-IDOR).</summary>
public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUserService(IHttpContextAccessor accessor) => _accessor = accessor;

    public string? UserId =>
        _accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? _accessor.HttpContext?.User.FindFirstValue("sub");

    public string? ClientIp => _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public bool IsAuthenticated => _accessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;
}
