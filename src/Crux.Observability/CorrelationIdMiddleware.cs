using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Crux.Observability;

/// <summary>
/// Reads (or mints) the <c>X-Correlation-ID</c> header on every inbound request,
/// stamps it onto the response, and stashes it in <see cref="HttpContext.Items"/>.
///
/// The optional <see cref="ILogContextEnricher"/> pushes the id into structured
/// logging (e.g. Serilog LogContext) so every log line emitted inside the request
/// scope carries the same id.
///
/// This is complementary to OTel <c>traceparent</c> propagation — traceparent
/// is the machine-readable span chain; correlation-id is the human-readable
/// handle that support/on-call greps for.
/// </summary>
public static class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemsKey = "CorrelationId";

    /// <summary>
    /// Registers the correlation-id middleware. Should run early in the pipeline
    /// — before routing, auth, anything that might log.
    /// </summary>
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
    {
        app.Use(static async (ctx, next) =>
        {
            string id;

            var alreadyStashed = ctx.Items.TryGetValue(ItemsKey, out var existing)
                && existing is string preset && !string.IsNullOrWhiteSpace(preset);

            if (alreadyStashed)
            {
                id = (string)existing!;
            }
            else if (ctx.Request.Headers.TryGetValue(HeaderName, out var headerValues)
                && !string.IsNullOrWhiteSpace(headerValues.ToString()))
            {
                id = headerValues.ToString();
                ctx.Items[ItemsKey] = id;
            }
            else
            {
                id = Guid.NewGuid().ToString("N");
                ctx.Items[ItemsKey] = id;
            }

            ctx.Response.OnStarting(static state =>
            {
                var c = (HttpContext)state;
                if (c.Items.TryGetValue(ItemsKey, out var v) && v is string s
                    && !c.Response.Headers.ContainsKey(HeaderName))
                {
                    c.Response.Headers[HeaderName] = s;
                }
                return Task.CompletedTask;
            }, ctx);

            // Push into structured logging if an enricher is registered.
            // Resolved from request services so it's per-request scoped.
            var enricher = ctx.RequestServices.GetService<ILogContextEnricher>();
            if (enricher is not null)
            {
                await enricher.EnrichAsync(id, next);
            }
            else
            {
                await next();
            }
        });

        return app;
    }
}

/// <summary>
/// Abstraction for pushing properties into the structured logging context.
/// Implement with Serilog's <c>LogContext.PushProperty</c> or equivalent.
/// Registered as a scoped or singleton service; resolved per-request by
/// the correlation-id middleware.
/// </summary>
public interface ILogContextEnricher
{
    Task EnrichAsync(string correlationId, Func<Task> next);
}
