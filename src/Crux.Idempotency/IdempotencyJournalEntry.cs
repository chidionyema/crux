namespace Crux.Idempotency;

/// <summary>
/// Persisted record of an idempotently-executed command.
/// Stored in the idempotency journal table of the application database.
/// </summary>
public class IdempotencyJournalEntry
{
    public Guid Id { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string CommandType { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public string? ResponseJson { get; set; }

    private IdempotencyJournalEntry() { }

    public static IdempotencyJournalEntry Create(string key, string commandType, TimeSpan ttl)
    {
        return new IdempotencyJournalEntry
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = key,
            CommandType = commandType,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(ttl)
        };
    }
}
