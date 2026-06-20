using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Crux.Observability;

/// <summary>
/// <see cref="DelegatingHandler"/> that reads the correlation id from the
/// current <see cref="HttpContext"/> (set by <see cref="CorrelationIdMiddleware"/>)
/// and stamps it onto every outbound HTTP request as <c>X-Correlation-ID</c>.
///
/// Also tags the current OTel <see cref="Activity"/> with <c>correlation.id</c>
/// so traces and logs can be joined by the same key.
/// </summary>
public sealed class CorrelationIdHttpClientHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    public const string ActivityTagName = "correlation.id";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var ctx = accessor.HttpContext;
        if (ctx is not null
            && ctx.Items.TryGetValue(CorrelationIdMiddleware.ItemsKey, out var raw)
            && raw is string id
            && !string.IsNullOrWhiteSpace(id))
        {
            if (!request.Headers.Contains(CorrelationIdMiddleware.HeaderName))
            {
                request.Headers.Add(CorrelationIdMiddleware.HeaderName, id);
            }

            Activity.Current?.SetTag(ActivityTagName, id);
        }

        return base.SendAsync(request, ct);
    }
}
