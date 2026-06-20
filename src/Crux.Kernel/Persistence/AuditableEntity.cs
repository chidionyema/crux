namespace Crux.Kernel.Persistence;

/// <summary>Marker for entities keyed by a Guid.</summary>
public interface IEntityWithGuid
{
    Guid Id { get; }
}

/// <summary>
/// Base class for entities with audit metadata.
///
/// Optimistic concurrency is provided by PostgreSQL's native <c>xmin</c> system column
/// (configured via <c>UseXminAsConcurrencyToken()</c> per entity), not a stored byte[]
/// rowversion: on Postgres a byte[] token is never auto-incremented and would silently
/// protect nothing, whereas xmin is bumped by the database on every update.
/// </summary>
public abstract class AuditableEntity : IEntityWithGuid
{
    protected AuditableEntity()
    {
        Id = Guid.NewGuid();
        CreatedAt = DateTime.UtcNow;
    }

    protected AuditableEntity(Guid id)
    {
        Id = id;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; set; }
    public string? CreatedFromIp { get; set; }
    public string? ModifiedFromIp { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? LastModifiedBy { get; set; }
    public DateTime? LastModifiedDate { get; set; }
}
