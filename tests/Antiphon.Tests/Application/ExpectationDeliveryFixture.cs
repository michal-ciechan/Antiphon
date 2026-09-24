using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0650 S4. The real <see cref="SessionMessageQueueService"/> around the standing agent's
/// owned session, on an isolated schema, with a fake terminal that records every write and turns
/// a submitted body into a UserPrompt row. Nothing here stubs the queue's delivery path.
/// </summary>
internal sealed class ExpectationDeliveryFixture : IAsyncDisposable
{
    private int _ordinal;

    private ExpectationDeliveryFixture(IsolatedTestSchema schema, ExpectationTestWorld world, BridgeQueueHarness harness)
    {
        Schema = schema;
        World = world;
        Harness = harness;
    }

    public IsolatedTestSchema Schema { get; }
    public ExpectationTestWorld World { get; }
    public BridgeQueueHarness Harness { get; }
    public Guid SessionId => World.OwnedSessionId;
    public RecordingCatchUp CatchUp { get; } = new();

    /// <param name="observable">One completed turn first, so the send has a transcript floor.</param>
    public static async Task<ExpectationDeliveryFixture> CreateAsync(bool observable = true)
    {
        var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = await ExpectationTestWorld.CreateAsync(schema.ConnectionString);
        await using (var db = world.Db())
        {
            await db.Agents.Where(a => a.Id == world.AgentId).ExecuteUpdateAsync(u => u
                .SetProperty(a => a.AlwaysOn, true)
                .SetProperty(a => a.PersistentSessionId, world.OwnedSessionId.ToString("D")));
        }

        var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ConnectionString = schema.ConnectionString,
            AttachSessionId = world.OwnedSessionId,
            AttachAgentId = world.AgentId,
            ConfigureServices = services =>
                services.AddSingleton<IExpectationPromptSender, SessionQueueExpectationPromptSender>(),
        });
        var fixture = new ExpectationDeliveryFixture(schema, world, harness);
        if (observable)
            await harness.InsertTurnAsync("an earlier prompt", "an earlier answer");
        return fixture;
    }

    public AppDbContext Db() => World.Db();

    public IExpectationPromptSender Sender => Harness.Provider.GetRequiredService<IExpectationPromptSender>();

    public async Task<DateTime> GenerationAsync(Guid? sessionId = null)
    {
        await using var db = Db();
        var id = sessionId ?? SessionId;
        return SessionGeneration.Normalize(await db.AgentSessions.Where(s => s.Id == id).Select(s => s.StartedAt).SingleAsync());
    }

    /// <summary>A fresh service and context per call, the way a job pass would build one.</summary>
    public async Task<ExpectationDeliveryResult> DeliverAsync(Guid nudgeId)
    {
        await using var db = Db();
        var service = new ExpectationNudgeDeliveryService(db, Sender, TimeProvider.System, CatchUp);
        return await service.DeliverAsync(World.Directive, nudgeId, CancellationToken.None);
    }

    /// <summary>
    /// A committed nudge with its audit comment, as S3 leaves it. The body carries the nudge id so
    /// every receipt search is about exactly this nudge.
    /// </summary>
    public async Task<ExpectationNudge> NudgeAsync(
        string? body = null,
        ExpectationAttemptState state = ExpectationAttemptState.None,
        Guid? destination = null,
        DateTime? generation = null,
        long? baseline = null,
        Guid? checkTaskId = null)
    {
        var id = Guid.NewGuid();
        var text = body ?? $"Expectation nudge {id:D}: 3 queued tasks held. Reply {ExpectationPromptFormatter.AckMarker(id)}";
        var now = DateTime.UtcNow;
        var comment = new CardComment
        {
            Id = Guid.NewGuid(),
            CardId = World.CardId,
            Body = "audit " + id.ToString("D"),
            Author = ExpectationLedger.AuditAuthor,
            CreatedAt = now,
        };
        var nudge = new ExpectationNudge
        {
            Id = id,
            DirectiveId = World.Directive.Id,
            Ordinal = Interlocked.Increment(ref _ordinal),
            EvidenceSnapshot = "StalledPipeline queue_age",
            Body = text,
            BodyDigest = new string('a', 64),
            DestinationSessionId = destination,
            DestinationGeneration = generation,
            BaselineSequence = baseline,
            AttemptState = state,
            AuditCommentId = comment.Id,
            CreatedAt = now,
        };
        await using var db = Db();
        if (checkTaskId is { } taskId)
        {
            // The nudge's Check note on its subject task, as the ledger commits it.
            var check = ExpectationTestWorld.Event(taskId, AgentTaskEventType.Check, now, $"[expectation-nudge:{id:D}] StalledPipeline");
            db.AgentTaskEvents.Add(check);
            nudge.CheckEventIdsJson = System.Text.Json.JsonSerializer.Serialize(new[] { check.Id });
        }
        db.CardComments.Add(comment);
        db.ExpectationNudges.Add(nudge);
        await db.SaveChangesAsync();
        return nudge;
    }

    /// <summary>A dispatched task on the audit card, to carry a nudge's Check note.</summary>
    public async Task<Guid> SubjectTaskAsync()
    {
        var id = Guid.NewGuid();
        await using var db = Db();
        db.AgentTasks.Add(World.Task(id, AgentTaskStatus.Dispatched, DateTime.UtcNow.AddMinutes(-30)));
        await db.SaveChangesAsync();
        return id;
    }

    public async Task<ExpectationNudge> ReloadAsync(Guid nudgeId)
    {
        await using var db = Db();
        return await db.ExpectationNudges.AsNoTracking().SingleAsync(n => n.Id == nudgeId);
    }

    public async Task<long> MaxSequenceAsync(Guid? sessionId = null)
    {
        await using var db = Db();
        var id = sessionId ?? SessionId;
        return await db.TranscriptEntries.Where(t => t.AgentSessionId == id).MaxAsync(t => (long?)t.Sequence) ?? 0;
    }

    public async Task<long> AppendTranscriptAsync(Guid sessionId, string kind, string? text)
    {
        var sequence = await MaxSequenceAsync(sessionId) + 1;
        await using var db = Db();
        db.TranscriptEntries.Add(ExpectationTestWorld.Transcript(sessionId, sequence, kind, DateTime.UtcNow, text));
        await db.SaveChangesAsync();
        return sequence;
    }

    /// <summary>A second Running session in the same schema, optionally owned by the standing agent.</summary>
    public async Task<Guid> AddSessionAsync(bool ownedByStandingAgent, bool makeCurrent)
    {
        var id = Guid.NewGuid();
        await using var db = Db();
        db.AgentSessions.Add(ExpectationTestWorld.Session(
            id, ownedByStandingAgent ? World.AgentId : null, DateTime.UtcNow.AddMinutes(-1), SessionStatus.Running));
        await db.SaveChangesAsync();
        if (makeCurrent)
        {
            await db.Agents.Where(a => a.Id == World.AgentId)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.PersistentSessionId, id.ToString("D")));
        }
        return id;
    }

    public async ValueTask DisposeAsync()
    {
        await Harness.DisposeAsync();
        await Schema.DisposeAsync();
    }

    /// <summary>Counts catch-up pulls; <see cref="OnCatchUp"/> can land a row during the pull.</summary>
    internal sealed class RecordingCatchUp : IExpectationCatchUp
    {
        public List<Guid> Pulled { get; } = [];
        public Func<Task>? OnCatchUp { get; set; }

        public async Task CatchUpAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken ct)
        {
            Pulled.AddRange(sessionIds);
            if (OnCatchUp is { } onCatchUp)
                await onCatchUp();
        }
    }
}
