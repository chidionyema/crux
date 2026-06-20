using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Crux.Kernel.Extensions;

/// <summary>
/// Extension methods for HttpContext to simplify claim extraction.
/// </summary>
public static class HttpContextExtensions
{
    public static Guid UserId(this ClaimsPrincipal user)
    {
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? user.FindFirstValue("sub");
        return Guid.Parse(id!);
    }

    public static string? UserIdOrNull(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");

    public static string? ClientIp(this HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString();
}
