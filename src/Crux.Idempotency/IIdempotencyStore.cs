using Microsoft.EntityFrameworkCore;

namespace Crux.Idempotency;

/// <summary>
/// Abstraction over the idempotency journal database. Consumers provide an
/// implementation backed by their application DbContext.
/// </summary>
public interface IIdempotencyJournalDbContext
{
    DbSet<IdempotencyJournalEntry> IdempotencyJournal { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Abstraction for idempotency storage, provider-agnostic.
/// The default implementation is EFCore-based via <see cref="IIdempotencyJournalDbContext"/>.
/// </summary>
public interface IIdempotencyStore
{
    Task<IdempotencyJournalEntry?> GetByKeyAsync(string key, CancellationToken ct = default);
    Task StoreAsync(IdempotencyJournalEntry entry, CancellationToken ct = default);
}
