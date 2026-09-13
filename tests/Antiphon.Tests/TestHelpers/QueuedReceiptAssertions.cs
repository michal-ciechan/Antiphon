using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0499: promoted from PostLandMutationDeliveryTests.ConfirmQueuedReceiptAsync.
/// Recipient acceptance is a runtime-produced UserPrompt, never a test-inserted row.
/// </summary>
internal static class QueuedReceiptAssertions
{
    public static async Task ConfirmQueuedReceiptAsync(
        string connection,
        BridgeQueueHarness h,
        SessionQueuedMessage queued,
        Guid sessionId,
        bool busy,
        string cut = "after-receipt")
    {
        var adapter = h.Adapter;
        BindRuntimeTranscript(adapter, sessionId, connection);
        if (!h.Runtime.ListLiveSessions().Contains(sessionId))
            h.Runtime.Register(sessionId, adapter);
        await MarkRecipientLiveAsync(connection, sessionId);

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        queued = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == queued.Id);

        BridgeQueueHarness? recovered = null;
        var queue = h.Queue;
        try
        {
            if (cut is "queue-inserted" or "lost-wakeup")
            {
                (await CountUserPromptsAsync(connection, sessionId)).ShouldBe(0,
                    "queue-insert/lost-wakeup recovery starts before any recipient transcript exists");
                adapter.SubmittedBodies.ShouldBeEmpty(
                    "a persisted queue row is not recipient acceptance");
                queued.Status.ShouldBe(QueuedMessageStatus.Pending);

                recovered = await BridgeQueueHarness.CreateAsync(new()
                {
                    AlwaysOn = false,
                    ConnectionString = connection,
                    Delegation = h.Delegation,
                });
                BindRuntimeTranscript(adapter, sessionId, connection);
                recovered.Runtime.Register(sessionId, adapter);
                await MarkRecipientLiveAsync(connection, sessionId);
                queue = recovered.Queue;
            }

            if (busy) await SetWorkingAsync(connection, sessionId, true);

            if (queued.Status == QueuedMessageStatus.Pending && queued.DeliveryAttempts == 0)
            {
                await queue.FlushIfIdleAsync(sessionId, CancellationToken.None);
                if (busy)
                {
                    adapter.Inputs.ShouldBeEmpty();
                    (await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == queued.Id))
                        .Status.ShouldBe(QueuedMessageStatus.Pending);
                    (await CountUserPromptsAsync(connection, sessionId)).ShouldBe(0,
                        "a busy caller must not receive the note until it is idle");
                    await SetWorkingAsync(connection, sessionId, false);
                    await queue.FlushIfIdleAsync(sessionId, CancellationToken.None);
                }
            }

            adapter.SubmittedBodies.ShouldNotBeEmpty(
                "queue insertion or adapter-less flush is not recipient acceptance");
            var submitted = await AssertSubmittedAsync(h, connection, sessionId, queued, adapter);

            var prompt = await WaitForUserPromptAsync(connection, sessionId, submitted);
            (await CountUserPromptsAsync(connection, sessionId)).ShouldBe(1);
            PromptSubmissionMatch.IsCompleteIn(submitted, prompt.Text!).ShouldBeTrue();
            PromptSubmissionMatch.Normalize(prompt.Text!).ShouldBe(PromptSubmissionMatch.Normalize(submitted));
            if (cut is "queue-inserted" or "lost-wakeup")
            {
                var recoveredRow = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == queued.Id);
                recoveredRow.Status.ShouldBeOneOf(QueuedMessageStatus.Pending, QueuedMessageStatus.Sent);
            }
        }
        finally
        {
            if (recovered is not null)
                await recovered.DisposeAsync();
        }
    }

    private static async Task<string> AssertSubmittedAsync(
        BridgeQueueHarness h, string connection, Guid sessionId, SessionQueuedMessage queued,
        FakeAgentProtocolAdapter adapter)
    {
        var typed = adapter.SubmittedBodies[^1];
        if (typed == queued.Body)
            return typed;
        typed.ShouldContain(TypedBodySpill.PointerHeadline);
        var relative = typed.Split('\n').Select(l => l.Trim().Trim('\'', '`'))
            .First(l => l.Contains(".antiphon") && l.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var cwd = (await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId)).Cwd;
        var absolute = Path.IsPathRooted(relative)
            ? relative
            : Path.GetFullPath(Path.Combine(cwd, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(absolute))
        {
            absolute = Path.IsPathRooted(relative)
                ? relative
                : Path.GetFullPath(Path.Combine(h.TempRoot, "workspace", relative.Replace('/', Path.DirectorySeparatorChar)));
        }
        (await File.ReadAllTextAsync(absolute)).ShouldBe(queued.Body);
        return typed;
    }

    /// <summary>
    /// Attach the fake TUI: a dispatched session is created Starting and never becomes Running
    /// without a real launch. Receipt confirmation drives the adapter, so the row must accept input.
    /// </summary>
    private static async Task MarkRecipientLiveAsync(string connection, Guid sessionId)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var session = await db.AgentSessions.SingleAsync(s => s.Id == sessionId);
        if (session.Status == SessionStatus.Running)
            return;
        session.Status = SessionStatus.Running;
        if (session.StartedAt == default) session.StartedAt = DateTime.UtcNow;
        session.LastSeenAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The fake TUI records the body it actually submitted. This is not a fallback that
    /// synthesizes a UserPrompt from the queued row when delivery never happened.
    /// </summary>
    private static void BindRuntimeTranscript(FakeAgentProtocolAdapter adapter, Guid sessionId, string connection)
    {
        adapter.OnSubmitted = async submitted =>
        {
            await BridgeQueueHarness.InsertEntryAsync(
                sessionId, TranscriptKinds.UserPrompt, submitted, timestamp: DateTime.UtcNow,
                connectionString: connection);
            await BridgeQueueHarness.InsertEntryAsync(
                sessionId, TranscriptKinds.TurnEnd, stopReason: TranscriptKinds.StopReasons.EndTurn,
                connectionString: connection);
        };
    }

    private static async Task<TranscriptEntry> WaitForUserPromptAsync(
        string connection, Guid sessionId, string queuedBody)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!budget.Token.IsCancellationRequested)
        {
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
            var prompts = await db.TranscriptEntries.AsNoTracking()
                .Where(e => e.AgentSessionId == sessionId && e.Kind == TranscriptKinds.UserPrompt)
                .ToListAsync();
            var match = prompts.FirstOrDefault(p =>
                p.Text is not null && PromptSubmissionMatch.IsCompleteIn(queuedBody, p.Text));
            if (match is not null)
                return match;
            try { await Task.Delay(50, budget.Token); }
            catch (OperationCanceledException) { break; }
        }

        throw new TimeoutException(
            "no runtime-produced UserPrompt matching the queued body appeared on the recipient session");
    }

    private static async Task<int> CountUserPromptsAsync(string connection, Guid sessionId)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        return await db.TranscriptEntries.CountAsync(e =>
            e.AgentSessionId == sessionId && e.Kind == TranscriptKinds.UserPrompt);
    }

    private static async Task SetWorkingAsync(string connection, Guid sessionId, bool working)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var seq = ((await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId).MaxAsync(t => (long?)t.Sequence)) ?? 0) + 1;
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = seq,
            Kind = working ? TranscriptKinds.AssistantText : TranscriptKinds.TurnEnd,
            Text = working ? "busy" : null,
            StopReason = working ? null : TranscriptKinds.StopReasons.EndTurn,
            CreatedAt = DateTime.UtcNow,
            Timestamp = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
