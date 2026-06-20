using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Crux.Observability;

/// <summary>
/// Extension methods for registering observability services.
/// </summary>
public static class ObservabilityServiceCollectionExtensions
{
    /// <summary>
    /// Registers the correlation-id middleware dependencies:
    /// <see cref="IHttpContextAccessor"/> and the outbound
    /// <see cref="CorrelationIdHttpClientHandler"/>.
    /// Must be called before <c>ConfigureHttpClientDefaults</c>
    /// so the handler type is available for <c>AddHttpMessageHandler&lt;T&gt;()</c>.
    /// </summary>
    public static IServiceCollection AddCorrelationId(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.TryAddTransient<CorrelationIdHttpClientHandler>();
        return services;
    }

    /// <summary>
    /// Opt-in: registers a readiness probe (tag <c>"ready"</c>) for the given
    /// EF Core <typeparamref name="TContext"/> using <c>DbContext.Database.CanConnectAsync</c>.
    /// Surfaces under <c>/health/ready</c>.
    /// </summary>
    public static IHealthChecksBuilder AddDbHealthCheck<TContext>(
        this IHealthChecksBuilder hcBuilder,
        string? name = null)
        where TContext : DbContext
        => hcBuilder.AddDbContextCheck<TContext>(
            name: name ?? typeof(TContext).Name,
            tags: ["ready"],
            customTestQuery: (ctx, ct) => ctx.Database.CanConnectAsync(ct));
}
