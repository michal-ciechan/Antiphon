using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0641: one controlled land and one real session queue on the same schema.
/// The delivery provider is the production notification graph, including the lease
/// singleton, so reconcile sees a busy repository the same way the hosted scanner does.
/// </summary>
internal sealed class LandOutcomeDeliveryHarness : IAsyncDisposable
{
    private LandDeliveryBoundary? _boundary;
    private ServiceProvider? _delivery;
    public LandingProtocolHarness Land { get; private set; } = null!;
    public BridgeQueueHarness Caller { get; private set; } = null!;
    public ProbeCounter Probes { get; private set; } = null!;
    public CompletionNoteFlushQueue Flushes { get; } = new();

    public static async Task<LandOutcomeDeliveryHarness> CreateAsync(LandDeliveryBoundary? boundary = null)
    {
        var world = new LandOutcomeDeliveryHarness();
        await world.InitializeAsync(boundary);
        return world;
    }

    private async Task InitializeAsync(LandDeliveryBoundary? boundary)
    {
        _boundary = boundary;
        Land = new LandingProtocolHarness();
        Land.ConfigureServices = services =>
        {
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IRepositoryMutationLease)).ToList())
                services.Remove(descriptor);
            services.AddSingleton<IRepositoryMutationLease>(sp =>
            {
                Probes = new ProbeCounter(new RepositoryMutationLease(sp.GetRequiredService<ILandingGit>()));
                return Probes;
            });
        };
        await Land.InitializeAsync();
        if (Probes is null)
            throw new InvalidOperationException("repository lease probe counter was not constructed");
        Caller = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = Land.Schema.ConnectionString,
        });
        Land.Messages = Caller.Queue;
        RebuildDelivery();
    }

    public void ReplaceBoundary(LandDeliveryBoundary? boundary)
    {
        _boundary = boundary;
        RebuildDelivery();
    }

    public async Task MarkBusyAsync() =>
        await Caller.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "caller is mid-turn");

    public async Task<AgentTaskLandNotification> LandAsync(bool conflict)
    {
        await using (var db = Land.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == Land.Git.TaskId);
            task.ReplyTo = AgentTaskReplyTo.Session;
            task.ParentSessionId = Caller.SessionId;
            await db.SaveChangesAsync();
        }

        await Land.AddSourceAsync();
        if (conflict)
        {
            Land.Git.BeforeCommand = (_, args) =>
            {
                if (args.Contains("rebase") && !args.Contains("--abort"))
                    return Task.FromResult<LandingGitResult?>(new LandingGitResult(1, "", "rebase_conflict"));
                if (args.Count > 0 && args[0] == "diff" && args.Contains("--diff-filter=U"))
                    return Task.FromResult<LandingGitResult?>(new LandingGitResult(0, "shared.md\0", ""));
                return Task.FromResult<LandingGitResult?>(null);
            };
        }

        (await Land.RunAsync()).ShouldBe(LandRunResult.Complete);
        await using var observer = Land.CreateContext();
        var kind = conflict ? LandNotificationKind.Conflict : LandNotificationKind.Outcome;
        var note = await observer.AgentTaskLandNotifications.AsNoTracking()
            .SingleAsync(n => n.TaskId == Land.Git.TaskId && n.Kind == kind);
        note.ParentSessionId.ShouldBe(Caller.SessionId);
        note.State.ShouldBe(LandNotificationState.Queued);
        note.QueueMessageId.ShouldBeNull();
        return note;
    }

    public async Task<RepositoryLease> HoldOtherLeaseAsync()
    {
        await using (var db = Land.CreateContext())
        {
            var owner = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == Land.Git.TaskId);
            var other = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask
            {
                Id = other,
                RootTaskId = other,
                Title = "B",
                Goal = "holds the repository lease",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Code,
                Workspace = WorkspaceMode.Worktree,
                WorkingDirectory = owner.WorkingDirectory,
                RepoPath = owner.RepoPath,
                Status = AgentTaskStatus.Succeeded,
                ReplyTo = AgentTaskReplyTo.None,
                CreatedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var held = await Probes.TryAcquireAsync(Land.Git.Repository, CancellationToken.None);
        return held.ShouldNotBeNull("B must acquire the live repository lease");
    }

    public async Task ReconcileAsync(Guid noteId)
    {
        Probes.Arm = true;
        try
        {
            await using var scope = Delivery.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AgentTaskLandNotificationService>()
                .ReconcileAsync(noteId, CancellationToken.None);
        }
        finally
        {
            Probes.Arm = false;
        }
    }

    public async Task MakeDueAsync(Guid noteId)
    {
        await using var db = Land.CreateContext();
        await db.AgentTaskLandNotifications.Where(n => n.Id == noteId)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.NextAttemptAt, DateTime.UtcNow.AddMinutes(-1)));
    }

    public async Task AssertKeyedWhileLeaseHeldAsync(AgentTaskLandNotification note)
    {
        await using var db = Land.CreateContext();
        var saved = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id);
        saved.QueueMessageId.ShouldNotBeNull("keyed enqueue/receipt missing while B owns the lease");
        saved.State.ShouldBe(LandNotificationState.AwaitingReceipt);
        var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.SourceLandNotificationId == note.Id);
        row.Id.ShouldBe(saved.QueueMessageId!.Value);
        row.AgentSessionId.ShouldBe(Caller.SessionId);
        row.Body.ShouldBe(note.Body);
        row.ContentDigest.ShouldBe(note.ContentDigest);
    }

    public async Task DeliverAsync(bool busy)
    {
        (await PumpFlushAsync(Caller.SessionId)).ShouldBeTrue("eligible caller wakeup was not enqueued");
        if (busy)
        {
            Caller.Adapter.SubmittedBodies.ShouldBeEmpty("a busy caller stays owed until the turn ends");
            await Caller.Queue.OnTurnEndAsync(Caller.SessionId, CancellationToken.None);
        }

        Caller.Adapter.SubmittedBodies.ShouldNotBeEmpty();
    }

    public async Task AssertReceiptAsync(AgentTaskLandNotification note)
    {
        await using var db = Land.CreateContext();
        var saved = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id);
        saved.State.ShouldBe(LandNotificationState.Confirmed);
        saved.LastErrorCode.ShouldBeNull();
        var submitted = Caller.Adapter.SubmittedBodies.ShouldHaveSingleItem();
        PromptSubmissionMatch.IsCompleteIn(note.Body, submitted).ShouldBeTrue();
        var prompts = await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == Caller.SessionId && t.Kind == TranscriptKinds.UserPrompt && t.Text != null)
            .ToListAsync();
        var match = prompts.Where(p => PromptSubmissionMatch.IsCompleteIn(note.Body, p.Text!)).ShouldHaveSingleItem();
        saved.ConfirmingPromptSequence.ShouldBe(match.Sequence);
    }

    public async ValueTask DisposeAsync()
    {
        if (_delivery is not null)
            await _delivery.DisposeAsync();
        if (Caller is not null)
            await Caller.DisposeAsync();
        if (Land is not null)
            await Land.DisposeAsync();
    }

    private ServiceProvider Delivery => _delivery ?? throw new InvalidOperationException("delivery provider is not built");

    private void RebuildDelivery()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Flushes);
        services.AddSingleton<SpecialistFailureQueue>();
        services.AddSingleton(Caller.Queue);
        services.AddSingleton(Caller.Runtime);
        services.AddSingleton<IRepositoryMutationLease>(Probes);
        if (_boundary is not null)
            services.AddSingleton<LandDeliveryBoundary>(_boundary);
        services.AddScoped(_ => new AppDbContext(TestDbFixture.CreateDbContextOptions(Land.Schema.ConnectionString)));
        services.AddScoped<AgentTaskLandNotificationService>();
        services.AddSingleton<IHostedService>(sp => new AgentTaskLandNotificationHostedService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ILogger<AgentTaskLandNotificationHostedService>>()));
        services.AddSingleton<IHostedService>(sp => new CompletionNoteWorkHostedService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<CompletionNoteFlushQueue>(),
            sp.GetRequiredService<SpecialistFailureQueue>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<CompletionNoteWorkHostedService>>()));
        var built = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        _ = built.GetServices<IHostedService>().ToArray();
        var previous = _delivery;
        _delivery = built;
        previous?.Dispose();
    }

    private async Task<bool> PumpFlushAsync(Guid sessionId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await foreach (var id in Flushes.ReadAllAsync(timeout.Token))
            {
                try
                {
                    if (id != sessionId)
                        continue;
                    await Caller.Queue.FlushIfIdleAsync(id, CancellationToken.None);
                    return true;
                }
                finally
                {
                    Flushes.Complete(id);
                }
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        return false;
    }

    internal sealed class ProbeCounter(IRepositoryMutationLease inner) : IRepositoryMutationLease
    {
        private int _count;
        public volatile bool Arm;
        public int Count => _count;

        public Task<RepositoryLease?> TryAcquireAsync(string repository, CancellationToken ct)
        {
            if (Arm)
                Interlocked.Increment(ref _count);
            return inner.TryAcquireAsync(repository, ct);
        }

        public bool Owns(RepositoryLease lease, string commonDirectory) => inner.Owns(lease, commonDirectory);
    }
}

internal sealed class EnqueueCut(string cut) : LandDeliveryBoundary
{
    public Guid? InsertedQueueId { get; private set; }

    public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
    {
        if (boundary == "queue-inserted")
            InsertedQueueId = identity;
        if (boundary == cut)
            throw new IOException("owned " + cut + " cut");
        return Task.CompletedTask;
    }
}
