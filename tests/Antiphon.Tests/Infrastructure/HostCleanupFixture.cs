using Antiphon.HostCleanup;

namespace Antiphon.Tests.Infrastructure;

internal sealed class HostCleanupClock : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-10-01T12:00:00Z");
    public override DateTimeOffset GetUtcNow() => UtcNow;
}

internal sealed class HostCleanupFixture
{
    public const string CheckpointPath = "/tmp/c723-0123456789abcdef0123456789abcdef";
    public HostCleanupClock Clock { get; } = new();
    public CleanupLimits Limits { get; set; } = new();
    public List<CleanupRoot> Roots { get; } = [new("/tmp", "storage-a", CleanupFamily.Checkpoint)];
    public List<CleanupHold> Holds { get; } = [];
    public List<string> ExtraDenied { get; } = [];
    public VirtualCleanupFileSystem FileSystem { get; } = new();
    public RecordingCleanupPlanStore PlanStore { get; } = new();
    public RecordingCleanupClaimStore ClaimStore { get; } = new();

    public CleanupPlanner Planner() => new(new CleanupFamilyRegistry(Roots, ExtraDenied),
        new CleanupKeepList(Holds), Limits, Clock);

    public CleanupExecutor Executor() => new(Planner(), FileSystem, PlanStore, ClaimStore, Limits);

    public CleanupCandidateFacts Candidate(string path = CheckpointPath, DateTimeOffset? created = null,
        IReadOnlyList<CleanupEntry>? entries = null, CleanupOwner? owner = null) =>
        new(path, new CleanupIdentity("storage-a", "file-1", "generation-1"),
            created ?? Clock.UtcNow.AddHours(-25), owner ?? DeadReleasedOwner(),
            entries ?? [File(path + "/.checkpoint-test-root.json", Clock.UtcNow.AddHours(-25), 10, 10)],
            true, false, false, true, true, "boot-a", "namespace-a");

    public CleanupOwner DeadReleasedOwner() => new("boot-a", "namespace-a", 23,
        Clock.UtcNow.AddDays(-2), OwnerLiveness.Dead, true, true, false, true);

    public static CleanupEntry File(string path, DateTimeOffset lastWrite, long logical,
        long allocated, bool readable = true, bool link = false, bool mount = false) =>
        new(path, false, link, mount, readable, lastWrite, logical, allocated);

    public static CleanupEntry Directory(string path, bool readable = true,
        bool link = false, bool mount = false) =>
        new(path, true, link, mount, readable, null, null, null);

    public CleanupPlan Plan(params CleanupCandidateFacts[] candidates) =>
        Planner().Plan(Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "storage-a", candidates);
}

internal sealed class VirtualCleanupFileSystem : ICleanupFileSystem
{
    public Dictionary<string, CleanupCandidateFacts> Facts { get; } = new(StringComparer.Ordinal);
    public int ObserveCalls { get; private set; }
    public int DeleteCalls { get; private set; }
    public long ReclaimedBytes { get; set; } = 1;
    public bool Partial { get; set; }
    public Action? BeforeObserve { get; set; }
    public List<string> Events { get; } = [];

    public ValueTask<CleanupCandidateFacts?> ObserveAsync(string path, CancellationToken cancellationToken)
    {
        ObserveCalls++;
        BeforeObserve?.Invoke();
        Facts.TryGetValue(path, out var facts);
        return ValueTask.FromResult(facts);
    }

    public ValueTask<CleanupDeleteResult> DeleteNoFollowAsync(
        CleanupCandidateFacts current, CancellationToken cancellationToken)
    {
        DeleteCalls++;
        Events.Add("delete");
        if (Partial)
            return ValueTask.FromResult(new CleanupDeleteResult(CleanupOutcomeKind.Partial, 0, "partial"));
        Facts.Remove(current.Path);
        return ValueTask.FromResult(new CleanupDeleteResult(CleanupOutcomeKind.Removed, ReclaimedBytes, "removed"));
    }
}

internal sealed class RecordingCleanupPlanStore : ICleanupPlanStore
{
    public int Calls { get; private set; }
    public bool Fail { get; set; }
    public Action? OnPersist { get; set; }
    public List<string> Events { get; } = [];

    public ValueTask PersistAsync(CleanupPlan plan, CancellationToken cancellationToken)
    {
        Calls++;
        if (Fail) throw new IOException("virtual plan persistence failure");
        Events.Add("plan-commit");
        OnPersist?.Invoke();
        return ValueTask.CompletedTask;
    }
}

internal sealed class RecordingCleanupClaimStore : ICleanupClaimStore
{
    private readonly Dictionary<CleanupIdentity, CleanupOutcome> receipts = [];
    private readonly HashSet<CleanupIdentity> claims = [];
    public int Claims { get; private set; }
    public int Completions { get; private set; }
    public List<long> Reservations { get; } = [];

    public ValueTask<CleanupOutcome?> ExistingAsync(CleanupIdentity identity,
        CancellationToken cancellationToken)
    {
        receipts.TryGetValue(identity, out var receipt);
        return ValueTask.FromResult(receipt);
    }

    public ValueTask<bool> TryClaimAsync(CleanupIdentity identity,
        long reservedBytes, CancellationToken cancellationToken)
    {
        Claims++;
        Reservations.Add(reservedBytes);
        return ValueTask.FromResult(claims.Add(identity));
    }

    public ValueTask CompleteAsync(CleanupOutcome outcome,
        CancellationToken cancellationToken)
    {
        Completions++;
        if (outcome.Decision.Identity is not null)
            receipts[outcome.Decision.Identity] = outcome;
        return ValueTask.CompletedTask;
    }
}
