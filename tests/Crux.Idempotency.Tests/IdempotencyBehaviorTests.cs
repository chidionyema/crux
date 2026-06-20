using System.Text.Json;
using Crux.Idempotency;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Crux.Idempotency.Tests;

/// <summary>
/// Proves the idempotency behavior: at-most-once execution, cached-response replay,
/// concurrent-duplicate race handling, and opt-out via empty key.
/// </summary>
public sealed class IdempotencyBehaviorTests
{
    private static TestJournalDb NewDb()
    {
        var opts = new DbContextOptionsBuilder<TestJournalDb>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new TestJournalDb(opts);
    }

    [Fact]
    public async Task Normal_execution_stores_response_in_journal()
    {
        var db = NewDb();
        var behavior = new IdempotencyBehavior<TestCommand, TestResponse>(
            db, NullLogger<IdempotencyBehavior<TestCommand, TestResponse>>.Instance);

        var command = new TestCommand { IdempotencyKey = "key-1" };
        var handlerInvoked = false;
        Task<TestResponse> next() { handlerInvoked = true; return Task.FromResult(new TestResponse("ok")); }

        var response = await behavior.Handle(command, next, CancellationToken.None);

        Assert.True(handlerInvoked);
        Assert.Equal("ok", response.Result);

        var entry = await db.IdempotencyJournal.FirstOrDefaultAsync(e => e.IdempotencyKey == "key-1");
        Assert.NotNull(entry);
        Assert.NotNull(entry.ResponseJson);
        var deserialized = JsonSerializer.Deserialize<TestResponse>(entry.ResponseJson);
        Assert.Equal("ok", deserialized?.Result);
    }

    [Fact]
    public async Task Duplicate_key_replays_cached_response_without_rerunning_handler()
    {
        var db = NewDb();
        var behavior = new IdempotencyBehavior<TestCommand, TestResponse>(
            db, NullLogger<IdempotencyBehavior<TestCommand, TestResponse>>.Instance);

        // First call — executes
        var handlerCalls = 0;
        Task<TestResponse> next() { handlerCalls++; return Task.FromResult(new TestResponse("first")); }

        var first = await behavior.Handle(new TestCommand { IdempotencyKey = "dup-key" }, next, CancellationToken.None);
        Assert.Equal(1, handlerCalls);
        Assert.Equal("first", first.Result);

        // Second call with same key — must NOT execute handler
        var second = await behavior.Handle(new TestCommand { IdempotencyKey = "dup-key" }, next, CancellationToken.None);
        Assert.Equal(1, handlerCalls); // handler NOT invoked again
        Assert.Equal("first", second.Result); // cached response returned
    }

    [Fact]
    public async Task Concurrent_duplicate_handles_unique_constraint_race()
    {
        // Simulate a race: two threads check the journal (both find nothing),
        // both run the handler, one wins the insert, the other hits a unique-constraint
        // violation which is caught and the behavior returns its (already-correct) response.
        var db = NewDb();
        var behavior1 = new IdempotencyBehavior<TestCommand, TestResponse>(
            db, NullLogger<IdempotencyBehavior<TestCommand, TestResponse>>.Instance);
        var behavior2 = new IdempotencyBehavior<TestCommand, TestResponse>(
            db, NullLogger<IdempotencyBehavior<TestCommand, TestResponse>>.Instance);

        var handlerCalls = 0;
        Task<TestResponse> next() { handlerCalls++; return Task.FromResult(new TestResponse("raced")); }

        // Fire both concurrently
        var t1 = behavior1.Handle(new TestCommand { IdempotencyKey = "race-key" }, next, CancellationToken.None);
        var t2 = behavior2.Handle(new TestCommand { IdempotencyKey = "race-key" }, next, CancellationToken.None);

        var results = await Task.WhenAll(t1, t2);

        // Both got responses (neither threw)
        Assert.Equal("raced", results[0].Result);
        Assert.Equal("raced", results[1].Result);

        // The key is in the journal (at least one insert succeeded)
        var entry = await db.IdempotencyJournal.FirstOrDefaultAsync(e => e.IdempotencyKey == "race-key");
        Assert.NotNull(entry);
    }

    [Fact]
    public async Task Empty_key_opts_out_of_idempotency()
    {
        var db = NewDb();
        var behavior = new IdempotencyBehavior<TestCommand, TestResponse>(
            db, NullLogger<IdempotencyBehavior<TestCommand, TestResponse>>.Instance);

        var command = new TestCommand { IdempotencyKey = "" }; // empty = opt out
        var handlerCalls = 0;
        Task<TestResponse> next() { handlerCalls++; return Task.FromResult(new TestResponse("fresh")); }

        var r1 = await behavior.Handle(command, next, CancellationToken.None);
        var r2 = await behavior.Handle(command, next, CancellationToken.None);

        // Handler runs every time — no idempotency applied
        Assert.Equal(2, handlerCalls);
        Assert.Equal("fresh", r1.Result);
        Assert.Equal("fresh", r2.Result);

        // Nothing stored in journal for empty key
        var count = await db.IdempotencyJournal.CountAsync(e => e.IdempotencyKey == "");
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Null_response_stored_and_replayed()
    {
        var db = NewDb();
        var behavior = new IdempotencyBehavior<TestCommand, TestResponse>(
            db, NullLogger<IdempotencyBehavior<TestCommand, TestResponse>>.Instance);

        Task<TestResponse> next() => Task.FromResult<TestResponse>(null!);

        // First call — stores null
        var first = await behavior.Handle(new TestCommand { IdempotencyKey = "null-resp" }, next, CancellationToken.None);
        Assert.Null(first);

        // Second call — replays default (null response -> default(TResponse))
        var handlerCalled = false;
        Task<TestResponse> next2() { handlerCalled = true; return Task.FromResult<TestResponse>(null!); }
        var second = await behavior.Handle(new TestCommand { IdempotencyKey = "null-resp" }, next2, CancellationToken.None);
        Assert.Null(second);
        Assert.False(handlerCalled); // handler NOT re-run
    }

    // ── Test types ────────────────────────────────────────────────────────

    public sealed class TestCommand : IIdempotentCommand
    {
        public string IdempotencyKey { get; init; } = string.Empty;
    }

    public sealed class TestResponse
    {
        public string Result { get; init; } = string.Empty;
        public TestResponse() { }
        public TestResponse(string result) => Result = result;
    }

    // ── In-memory journal DbContext ───────────────────────────────────────

    public sealed class TestJournalDb : DbContext, IIdempotencyJournalDbContext
    {
        public TestJournalDb(DbContextOptions<TestJournalDb> opts) : base(opts) { }

        public DbSet<IdempotencyJournalEntry> IdempotencyJournal => Set<IdempotencyJournalEntry>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<IdempotencyJournalEntry>(e =>
            {
                e.HasKey(x => x.Id);
                e.HasIndex(x => x.IdempotencyKey).IsUnique();
            });
        }

        Task<int> IIdempotencyJournalDbContext.SaveChangesAsync(CancellationToken ct) =>
            SaveChangesAsync(ct);
    }
}
