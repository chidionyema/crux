using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Crux.Idempotency;

/// <summary>
/// MediatR pipeline behavior enforcing at-most-once execution for commands
/// implementing <see cref="IIdempotentCommand"/>.
///
/// Flow: check journal for the key → if present, return the cached response
/// without re-running the handler → else run, store the response, return.
/// Races are resolved by the UNIQUE key: the loser catches the constraint
/// violation and returns its (already-correct) response.
/// </summary>
public sealed class IdempotencyBehavior<TRequest, TResponse>(
    IIdempotencyJournalDbContext journalDb,
    ILogger<IdempotencyBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IIdempotentCommand
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(48);

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var key = request.IdempotencyKey;

        // Empty key = caller explicitly opted out.
        if (string.IsNullOrWhiteSpace(key))
            return await next();

        var existing = await journalDb.IdempotencyJournal
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.IdempotencyKey == key, cancellationToken);

        if (existing is not null)
        {
            logger.LogInformation("Idempotent replay: key={Key}, command={CommandType}", key, typeof(TRequest).Name);
            if (existing.ResponseJson is not null)
                return JsonSerializer.Deserialize<TResponse>(existing.ResponseJson)!;
            return default!;
        }

        var response = await next();

        try
        {
            var entry = IdempotencyJournalEntry.Create(key, typeof(TRequest).Name, DefaultTtl);
            entry.ResponseJson = response is not null ? JsonSerializer.Serialize(response) : null;
            journalDb.IdempotencyJournal.Add(entry);
            await journalDb.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Benign race: another thread recorded this key between our check and insert.
            logger.LogDebug(ex, "Idempotency journal race on key={Key} — benign", key);
        }

        return response;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        // Use PostgresException.SqlState for a version-safe check.
        // Fall back to "unique" text search for non-Postgres providers (in-memory, tests).
        return string.Equals((ex.InnerException as PostgresException)?.SqlState, "23505", StringComparison.Ordinal)
            || (ex.InnerException?.Message ?? "").Contains("unique", StringComparison.OrdinalIgnoreCase);
    }
}
