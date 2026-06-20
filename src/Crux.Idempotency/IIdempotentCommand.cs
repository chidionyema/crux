using MediatR;

namespace Crux.Idempotency;

/// <summary>
/// Marker interface for MediatR commands that require idempotent execution.
/// The IdempotencyBehavior pipeline behavior intercepts commands implementing
/// this interface and ensures at-most-once semantics.
/// </summary>
public interface IIdempotentCommand
{
    /// <summary>
    /// Unique key identifying this command instance. Two commands with the same
    /// key are considered duplicates; only the first will execute.
    /// Empty/null key = opt out of idempotency for this invocation.
    /// </summary>
    string IdempotencyKey { get; }
}
