using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1431: a held-aged warning and the pipeline queue reason name the stint's dominant
/// class. Same-card base inspection has its own queue reason.
/// </summary>
[Category("Integration")]
[ParallelLimiter<PipelineStatusLoadLimit>]
[Timeout(120_000)]
public sealed class DispatchHoldAttributionTests
{
    private static readonly DateTime T0 = new(2026, 10, 21, 21, 30, 0, DateTimeKind.Utc);

    /// <summary>
    /// A 325s wait whose latest sentence is remote preparation and whose prep time is 0s
    /// was lease time. The warning names lease, and the latest sentence stays parseable.
    /// </summary>
    [Test]
    public void C1431_held_aged_names_the_dominant_class()
    {
        var now = T0.AddSeconds(325);
        var prep = DispatchHoldDetails.RemoteMirrorRequested("server2", now);
        var ledger = DispatchHoldLedger.FromRows(
            [
                (T0, DispatchHoldDetails.LeaseOccupiedUnknown),
                (now, prep),
            ],
            DateTime.MinValue,
            now);
        ledger.Dominant.ShouldBe(DispatchHoldClass.Lease);
        ledger.SecondsFor(DispatchHoldClass.RemotePrep).ShouldBe(0);
        ledger.SecondsFor(DispatchHoldClass.Lease).ShouldBe(325);

        var detail = DispatchHoldDetails.Escalation(
            DispatchHoldDetails.WarningPrefix, 325, T0, T0, prep, 2, 6, ["abcd1234"], ledger);
        var classAt = detail.IndexOf("class=lease", StringComparison.Ordinal);
        var reasonAt = detail.IndexOf("reason=", StringComparison.Ordinal);
        classAt.ShouldBeGreaterThanOrEqualTo(0);
        classAt.ShouldBeLessThan(reasonAt, "latest sentence reported");
        DispatchHoldDetails.ClassOf(detail).ShouldBe(DispatchHoldClass.Lease, "latest sentence reported");
        DispatchHoldDetails.Reason(detail).ShouldBe(prep);
    }

    /// <summary>
    /// Inspection pending is <c>siblingInspection</c>, not <c>awaitingDispatch</c>.
    /// A stint spent on the lease reports the lease even when the latest sentence is remote preparation.
    /// </summary>
    [Test]
    public async Task C1431_inspection_pending_has_its_own_queue_reason()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        using var workspace = new TempWorkspace();
        var now = DateTime.UtcNow;
        var inspection = await SeedQueuedAsync(db, workspace.Path, "waiting on inspection");
        var leaseWait = await SeedQueuedAsync(db, workspace.Path, "waiting on the lease");
        var owner = await SeedQueuedAsync(db, workspace.Path, "lease owner");
        owner.Status = AgentTaskStatus.Succeeded;
        owner.CompletedAt = now.AddMinutes(-30);
        AddHeld(db, inspection.Id, DispatchHoldDetails.SiblingInspectionPending, now.AddSeconds(-300));
        AddHeld(db, leaseWait.Id, DispatchHoldDetails.LeaseHeldByOwner(owner.Id, "land", now.AddSeconds(-325)), now.AddSeconds(-325));
        AddHeld(db, leaseWait.Id, DispatchHoldDetails.RemoteMirrorRequested("server2", now), now);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var dto = await service.GetAsync(CancellationToken.None);
        var inspectionRow = dto.Stages.SelectMany(s => s.Queued).Single(t => t.TaskId == inspection.Id);
        var leaseRow = dto.Stages.SelectMany(s => s.Queued).Single(t => t.TaskId == leaseWait.Id);

        inspectionRow.QueueReason.ShouldBe("siblingInspection");
        inspectionRow.HeldBy.ShouldBeEmpty();
        leaseRow.QueueReason.ShouldBe(AgentTaskPipelineStatusService.QueueReasonRepositoryLease);
        leaseRow.HeldBy.Select(h => h.TaskId).ShouldBe([owner.Id]);
        leaseRow.HeldBy.Single().Title.ShouldBe("lease owner");
    }

    /// <summary>
    /// Start and finish timing are Information, so an investigation can read them without Debug.
    /// A null database forces the error path after the start line, which still emits the finish line.
    /// </summary>
    [Test]
    public async Task C1431_inspection_timing_is_information()
    {
        var logs = new CaptureLogger<CardSiblingInspectionService>();
        var service = new CardSiblingInspectionService(
            null!, null!, Options.Create(new GitSettings()), TimeProvider.System, new GitProcessGate(), logs);
        using var dir = new TempWorkspace();
        var task = new AgentTask
        {
            Id = Guid.NewGuid(),
            RootTaskId = Guid.NewGuid(),
            Title = "inspect",
            Goal = "inspect",
            Role = AgentTaskRole.Code,
            Status = AgentTaskStatus.Queued,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = dir.Path,
            RepoPath = dir.Path,
            CardId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
        };

        var guard = await service.InspectAsync(task, CancellationToken.None);

        guard.Reason.ShouldBe("git_inspection_error");
        logs.Entries.ShouldContain(e => e.Level == LogLevel.Information
            && e.Message.Contains("started", StringComparison.Ordinal)
            && e.Message.Contains("Sibling inspection", StringComparison.Ordinal));
        logs.Entries.ShouldContain(e => e.Level == LogLevel.Information
            && e.Message.Contains("finished:", StringComparison.Ordinal)
            && e.Message.Contains("elapsedMs=", StringComparison.Ordinal));
    }

    private static AgentTaskPipelineStatusService CreateService(AppDbContext db)
    {
        var options = Options.Create(new DelegationSettings());
        return new AgentTaskPipelineStatusService(
            db,
            options,
            new AreaMapLoader(options, NullLogger<AreaMapLoader>.Instance),
            TimeProvider.System);
    }

    private static async Task<AgentTask> SeedQueuedAsync(AppDbContext db, string directory, string title)
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = title,
            Goal = title,
            Role = AgentTaskRole.Code,
            Status = AgentTaskStatus.Queued,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = directory,
            RepoPath = directory,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.High,
            CreatedAt = now,
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static void AddHeld(AppDbContext db, Guid taskId, string detail, DateTime at) =>
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = taskId,
            Type = AgentTaskEventType.Held,
            Detail = detail,
            At = at,
        });

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-c1431").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }

    private sealed class CaptureLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
