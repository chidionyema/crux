using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Crux.Resilience;

/// <summary>
/// Metrics interface for resilience policies. Implementations record
/// retry attempts, circuit breaker state changes, bulkhead rejections,
/// timeouts, and fallback executions.
/// </summary>
public interface IResilienceMetrics
{
    void RecordRetryAttempt(string serviceName, int attempt, string exceptionType);
    void RecordCircuitBreakerStateChange(string serviceName, CircuitBreakerState state);
    void RecordBulkheadRejection(string serviceName);
    void RecordTimeout(string serviceName);
    void RecordFallbackExecuted(string serviceName, string reason);
}

public enum CircuitBreakerState
{
    Closed,
    Open,
    HalfOpen
}

/// <summary>
/// No-op implementation of <see cref="IResilienceMetrics"/> for environments
/// without metrics collection.
/// </summary>
public sealed class NullResilienceMetrics : IResilienceMetrics
{
    public static readonly NullResilienceMetrics Instance = new();

    public void RecordRetryAttempt(string serviceName, int attempt, string exceptionType) { }
    public void RecordCircuitBreakerStateChange(string serviceName, CircuitBreakerState state) { }
    public void RecordBulkheadRejection(string serviceName) { }
    public void RecordTimeout(string serviceName) { }
    public void RecordFallbackExecuted(string serviceName, string reason) { }
}

/// <summary>
/// Extension methods for registering resilience services with the DI container.
/// </summary>
public static class ResilienceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the null resilience metrics singleton. Consumers add
    /// <c>AddStandardResilienceHandler()</c> on their own HttpClient defaults.
    /// </summary>
    public static IServiceCollection AddCruxResilience(this IServiceCollection services)
    {
        services.TryAddSingleton<IResilienceMetrics, NullResilienceMetrics>();
        return services;
    }
}
