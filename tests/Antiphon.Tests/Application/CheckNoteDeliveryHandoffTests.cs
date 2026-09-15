using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0501 review F2 — the handoff nothing owned: a check note PRODUCED by the real
/// <see cref="AgentTaskCheckService"/> must arrive in the recipient's transcript as one complete,
/// correlated <c>UserPrompt</c>.
///
/// <para>The existing coverage stops one step short on each side of this seam.
/// <c>AgentTaskCheckInterpreterTests</c> runs real production but reads the result back off the
/// QUEUE ROW (<c>NotesToCallerAsync</c>) — its caller session has no adapter, so nothing is ever
/// typed. <c>SessionMessageQueueWedgedHeadTests</c> and its siblings run real delivery and recovery
/// but hand-seed the queue row. CARD-0501's live failure lived exactly between them: real notes were
/// produced for a session whose queue head could not move, and no test could have noticed, because
/// no test ever carried one note from production to a prompt.</para>
///
/// <para>Both recipients in the live incident are covered: one BUSY at production time (the note
/// waits for a flush) and one already ELIGIBLE (the enqueue delivers inline). Both then go through
/// the recovery path this card changed — a first delivery whose Enter is swallowed leaves the body
/// standing in the composer, and the Enter-only recovery is what finishes it — so the assertion is
/// on the prompt the agent really received, not on the queue's own verdict.</para>
///
/// <para>The interpretation half stays with <c>AgentTaskCheckInterpreterTests</c>: here the
/// interpreter is deliberately not wired, which is the plain slice-3 digest note — byte for byte
/// the shape of the 65 rows that were stranded on session <c>cea73d57</c>.</para>
/// </summary>
[Category("Integration")]
[Category("Slow")]
[NotInParallel("MessageQueue")]
public class CheckNoteDeliveryHandoffTests
{
    private static AppDbContext CreateContext() => BridgeQueueHarness.CreateContext();

    private static Task<BridgeQueueHarness> CreateAsync() =>
        BridgeQueueHarness.CreateAsync(new()
        {
            // A failed first delivery must not kill the recipient out from under the test; the
            // subject here is the note's journey, not the always-on recovery rule.
            AlwaysOn = false,
            ConfigureDeliveryVerification = v => v.PostFailureConfirmGraceSeconds = 0,
            ConfigureServices = services =>
            {
                services.AddScoped<DelegateCheckProbe>();
                services.AddScoped<AgentTaskCheckService>();
            },
        });

    private static AgentTaskCheckService ChecksFor(BridgeQueueHarness h) =>
        h.Provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskCheckService>();

    /// <summary>A Dispatched delegate task whose caller is the harness's live session.</summary>
    private static async Task<Guid> SeedCheckedDelegateAsync(BridgeQueueHarness h)
    {
        var delegateSessionId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var dispatched = DateTime.UtcNow.AddMinutes(-11);

        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = delegateSessionId,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = Path.GetTempPath(),
            Cols = 120,
            Rows = 30,
            CreatedAt = dispatched,
            StartedAt = dispatched,
            LastSeenAt = dispatched,
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId,
            RootTaskId = taskId,
            ParentSessionId = h.SessionId,
            ReplyTo = AgentTaskReplyTo.Session,
            Title = "CARD-0501 F2 checked delegate",
            Goal = "do the checked thing",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            ModelLevel = AgentModelLevel.Frontier,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = Path.GetTempPath(),
            AgentSessionId = delegateSessionId,
            Status = AgentTaskStatus.Dispatched,
            ExpectedDurationMinutes = 10,
            CreatedAt = dispatched,
            DispatchedAt = dispatched,
        });
        await db.SaveChangesAsync();
        return taskId;
    }

    private static async Task<SessionQueuedMessage> CheckRowAsync(BridgeQueueHarness h)
    {
        await using var db = CreateContext();
        return await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == h.SessionId && m.Origin == QueuedMessageOrigin.Check)
            .SingleAsync();
    }

    private static async Task<List<string>> UserPromptsAsync(BridgeQueueHarness h)
    {
        await using var db = CreateContext();
        return await db.TranscriptEntries.AsNoTracking()
            .Where(e => e.AgentSessionId == h.SessionId && e.Kind == TranscriptKinds.UserPrompt)
            .OrderBy(e => e.Sequence)
            .Select(e => e.Text!)
            .ToListAsync();
    }

    // The invariant both cases end on: ONE prompt, whole, and traceable back to the task the check
    // was about. A containment match is not enough — a merged or truncated prompt would pass that.
    private static async Task AssertNoteArrivedWholeAsync(BridgeQueueHarness h, Guid taskId)
    {
        var row = await CheckRowAsync(h);
        row.ConversationKey.ShouldBe(AgentTaskCheckService.ConversationKey(taskId));
        row.SourceTaskId.ShouldBe(taskId);
        row.Status.ShouldBe(QueuedMessageStatus.Sent);
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);

        var prompts = await UserPromptsAsync(h);
        prompts.ShouldBe([row.Body], "exactly the produced note, once, and nothing riding with it");
        row.Body.ShouldStartWith($"[check {DelegationReportFormatter.Short(taskId)} ");
        h.Adapter.SubmittedBodies.ShouldBe([row.Body]);
    }

    [Test]
    public async Task A_check_note_reaches_an_already_eligible_recipient_as_one_whole_prompt()
    {
        await using var h = await CreateAsync();
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        var taskId = await SeedCheckedDelegateAsync(h);

        // The recipient is idle, so production's own enqueue types it. The first Enter is
        // swallowed: the body stands in the composer and the row falls back to Pending.
        h.Adapter.SwallowSubmits = 99;
        (await ChecksFor(h).RunCheckAsync(taskId, CancellationToken.None))
            .ShouldBe(AgentTaskCheckService.CheckOutcome.Delivered);

        var attempted = await CheckRowAsync(h);
        attempted.Status.ShouldBe(QueuedMessageStatus.Pending);
        attempted.DeliveryAttempts.ShouldBe(1);
        (await UserPromptsAsync(h)).ShouldBeEmpty("nothing was submitted yet");

        // Recovery: the head is standing whole in THIS generation, so the sweep Enters it rather
        // than retyping — and that is what finally puts the note in the transcript.
        h.Adapter.SwallowSubmits = 0;
        var typedBefore = h.Adapter.Inputs.Count(i => i != "\r");
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        h.Adapter.Inputs.Count(i => i != "\r").ShouldBe(typedBefore, "recovery never retypes the body");
        await AssertNoteArrivedWholeAsync(h, taskId);
    }

    [Test]
    public async Task A_check_note_reaches_a_busy_recipient_as_one_whole_prompt_after_recovery()
    {
        await using var h = await CreateAsync();
        var taskId = await SeedCheckedDelegateAsync(h);

        // Busy at production time: the note is persisted and nothing is typed.
        await h.MarkWorkingAsync();
        h.Adapter.SwallowSubmits = 99;
        (await ChecksFor(h).RunCheckAsync(taskId, CancellationToken.None))
            .ShouldBe(AgentTaskCheckService.CheckOutcome.Delivered);

        var queued = await CheckRowAsync(h);
        queued.Status.ShouldBe(QueuedMessageStatus.Pending);
        queued.DeliveryAttempts.ShouldBe(0, "a busy recipient is not typed into");
        h.Adapter.Inputs.ShouldBeEmpty();

        // The turn ends; the first flush types it and has its Enter swallowed.
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        (await CheckRowAsync(h)).DeliveryAttempts.ShouldBe(1);
        (await UserPromptsAsync(h)).ShouldBeEmpty();

        h.Adapter.SwallowSubmits = 0;
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        await AssertNoteArrivedWholeAsync(h, taskId);
    }
}
