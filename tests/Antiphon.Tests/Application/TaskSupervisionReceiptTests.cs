using System.Data.Common;
using System.Reflection;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public sealed class TaskSupervisionReceiptTests
{
    [Test]
    [Arguments("eligible")]
    [Arguments("busy")]
    [Arguments("crash")]
    public Task C1610_Hold_parent_has_complete_UserPrompt(string mode) => ReceiptAsync("hold", mode);

    [Test]
    [Arguments("eligible")]
    [Arguments("busy")]
    [Arguments("crash")]
    public Task C1610_Failed_delegate_has_complete_UserPrompt(string mode) => ReceiptAsync("failure", mode);

    [Test]
    [Arguments("eligible")]
    [Arguments("busy")]
    [Arguments("crash")]
    public Task C1610_Failed_late_report_parent_has_complete_UserPrompt(string mode) => ReceiptAsync("late", mode);

    private static async Task ReceiptAsync(string purpose, string mode)
    {
        await using var h = await TaskCeilingHarness.CreateAsync(role: AgentTaskRole.Deploy,
            kind: purpose == "hold" ? TranscriptKinds.ToolCall : TranscriptKinds.Thinking,
            lastAgeMinutes: purpose == "hold" ? 6 : 21);
        var fault = new NoticeInsertFault();
        var recipientId = purpose == "failure" ? h.SessionId : h.ParentId;
        var recipient = await OpenAsync(h, recipientId, fault);
        await using var decoy = await OpenAsync(h);
        try
        {
            if (mode == "busy") await recipient.MarkWorkingAsync();
            var floor = await recipient.CurrentTranscriptMaxSequenceAsync();
            await h.SweepAsync();
            if (purpose == "late") await h.FinishAsync();
            var before = await h.TaskAsync();
            before.Status.ShouldBe(purpose == "hold" ? AgentTaskStatus.Working : AgentTaskStatus.Failed);
            var expectedPurpose = purpose switch { "hold" => TaskSupervisionNoticePurpose.CeilingParent,
                "failure" => TaskSupervisionNoticePurpose.FailureDelegate, _ => TaskSupervisionNoticePurpose.LateFailedParent };
            fault.Armed = mode == "crash";
            await RecoverAsync(recipient);
            if (mode == "crash")
            {
                fault.Fired.ShouldBeTrue("a durable obligation must survive the first queue-insert fault");
                await using (var db = h.Context())
                    (await db.TaskSupervisionNotices.CountAsync(n => n.TaskId == h.TaskId && n.Purpose == expectedPurpose))
                        .ShouldBe(1, "producer obligation committed before enqueue");
                await recipient.DisposeAsync();
                recipient = await OpenAsync(h, recipientId);
            }
            await RecoverAsync(recipient);
            if (mode == "busy")
            {
                (await PromptsAsync(h, recipientId, floor)).ShouldBeEmpty("no mid-turn supervision input");
                await using (var db = h.Context())
                    (await db.TaskSupervisionNotices.SingleAsync(n => n.TaskId == h.TaskId && n.Purpose == expectedPurpose))
                        .State.ShouldBe(TaskSupervisionNoticeState.Pending);
                await recipient.MarkIdleAsync();
                await recipient.Queue.OnTurnEndAsync(recipientId, CancellationToken.None);
            }
            await RecoverAsync(recipient);
            TaskSupervisionNotice notice;
            await using (var db = h.Context())
                notice = await db.TaskSupervisionNotices.SingleAsync(n => n.TaskId == h.TaskId && n.Purpose == expectedPurpose);
            var prompts = await PromptsAsync(h, recipientId, floor);
            var receipt = prompts.Where(p => p.Text?.Contains(notice.Body, StringComparison.Ordinal) == true).ShouldHaveSingleItem();
            receipt.Text.ShouldContain(h.TaskId.ToString("D"));
            receipt.Sequence.ShouldBeGreaterThan(floor);
            if (purpose == "hold") receipt.Text.ShouldContain("next=decide");
            else if (purpose == "failure") receipt.Text.ShouldContain("Your task is Failed. Stop starting further work");
            else
            {
                receipt.Text.ShouldContain("Task remains Failed; late report for triage only");
                receipt.Text.ShouldContain("next=decide");
                receipt.Text.ShouldContain("Completed the authorized work");
                (await h.TaskAsync()).Result.ShouldBe(before.Result);
                (await h.TaskAsync()).CompletedAt.ShouldBe(before.CompletedAt);
                (await h.TaskAsync()).FailureReason.ShouldBe(before.FailureReason);
            }
            notice.State.ShouldBe(TaskSupervisionNoticeState.Told);
            notice.ConfirmingPromptSequence.ShouldBe(receipt.Sequence);
            await RecoverAsync(recipient);
            (await PromptsAsync(h, recipientId, floor)).Count(p => p.Text?.Contains(notice.Body, StringComparison.Ordinal) == true).ShouldBe(1);
            (await PromptsAsync(h, decoy.SessionId, 0)).ShouldBeEmpty();
            decoy.Adapter.SubmittedBodies.ShouldBeEmpty();
            await using (var db = h.Context())
            {
                (await db.AgentTaskStageOutcomes.CountAsync()).ShouldBe(0);
                (await db.PipelineHandoffActions.CountAsync()).ShouldBe(0);
                (await db.AgentTasks.CountAsync()).ShouldBe(1);
            }
            h.Stopper.Killed.ShouldBeEmpty();
        }
        finally { await recipient.DisposeAsync(); }
    }

    [Test]
    [Arguments("retry")]
    [Arguments("generation-replacement")]
    [Arguments("settled-hold")]
    public async Task C1610_Stale_notice_is_not_delivered(string arm)
    {
        await using var h = await TaskCeilingHarness.CreateAsync();
        await using var parent = await OpenAsync(h, h.ParentId);
        await parent.MarkWorkingAsync();
        await h.SweepAsync();
        await RecoverAsync(parent);
        if (arm == "settled-hold") await h.FinishAsync();
        else if (arm == "retry") await h.MutateAsync(t => t.Attempt++);
        else
        {
            await using var db = h.Context();
            var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
            session.StartedAt = session.StartedAt.AddMinutes(1);
            await db.SaveChangesAsync();
        }
        await parent.MarkIdleAsync();
        await parent.Queue.OnTurnEndAsync(parent.SessionId, CancellationToken.None);
        await RecoverAsync(parent);
        var prompts = await PromptsAsync(h, parent.SessionId, 0);
        prompts.Any(p => p.Text?.Contains("[task " + DelegationReportFormatter.Short(h.TaskId) + " held]", StringComparison.Ordinal) == true)
            .ShouldBeFalse("stale hold guidance must not consume the next turn");
        await using (var db = h.Context())
            (await db.TaskSupervisionNotices.SingleAsync(n => n.TaskId == h.TaskId)).State.ShouldBe(TaskSupervisionNoticeState.Superseded);
        h.Stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    [Arguments("retry")]
    [Arguments("generation-reuse")]
    [Arguments("pre-failure-report")]
    public async Task C1610_Late_report_identity_is_fenced(string arm)
    {
        await using var h = await TaskCeilingHarness.CreateAsync(role: AgentTaskRole.Deploy,
            kind: TranscriptKinds.Thinking, lastAgeMinutes: 21);
        await using var parent = await OpenAsync(h, h.ParentId);
        if (arm == "pre-failure-report")
        {
            await h.AppendAsync(TranscriptKinds.AssistantText, "Already complete.\n" + DelegationReportFormatter.ReportToken(h.TaskId, "done"));
            // Persist a complete pre-failure report without calling the settlement path.
            await h.AppendAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
            // Retain an actually overdue model-wait tail for the production failure path.
            h.Clock.Advance(TimeSpan.FromMinutes(21));
            await h.AppendAsync(TranscriptKinds.Thinking, "waiting");
            h.Clock.Advance(TimeSpan.FromMinutes(21));
        }
        await h.SweepAsync();
        (await h.TaskAsync()).Status.ShouldBe(AgentTaskStatus.Failed);
        if (arm == "retry") await h.MutateAsync(t => t.Attempt++);
        else if (arm == "generation-reuse")
        {
            await using var db = h.Context();
            var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
            session.StartedAt = session.StartedAt.AddMinutes(1);
            await db.SaveChangesAsync();
        }
        if (arm != "pre-failure-report") await h.FinishAsync();
        await RecoverAsync(parent);
        await RecoverAsync(parent);
        await using (var db = h.Context())
        {
            (await db.TaskSupervisionNotices.CountAsync(n => n.TaskId == h.TaskId && n.Purpose == TaskSupervisionNoticePurpose.FailureDelegate))
                .ShouldBe(1, "the real failure writer must record its accepted generation");
            (await db.TaskSupervisionNotices.CountAsync(n => n.TaskId == h.TaskId && n.Purpose == TaskSupervisionNoticePurpose.LateFailedParent))
                .ShouldBe(0, "foreign-attempt, reused-generation and pre-failure evidence cannot be forwarded");
        }
        (await h.TaskAsync()).Status.ShouldBe(AgentTaskStatus.Failed);
        h.Stopper.Killed.ShouldBeEmpty();
    }

    private static Task<BridgeQueueHarness> OpenAsync(TaskCeilingHarness h, Guid? session = null, NoticeInsertFault? fault = null) =>
        BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ConnectionString = h.ConnectionString, PreserveDatabaseOnDispose = true, ClaudeDialogObservation = true,
            AttachSessionId = session, AttachAgentId = session is null ? null : Guid.NewGuid(),
            ConfigureDbContext = fault is null ? null : b => b.AddInterceptors(fault),
        });

    private static async Task RecoverAsync(BridgeQueueHarness h)
    {
        var worker = new CompletionNoteWorkHostedService(h.Provider.GetRequiredService<IServiceScopeFactory>(),
            new CompletionNoteFlushQueue(), new SpecialistFailureQueue(), h.Clock,
            NullLogger<CompletionNoteWorkHostedService>.Instance);
        await using var scope = h.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Antiphon.Server.Infrastructure.Data.AppDbContext>();
        await (Task)typeof(CompletionNoteWorkHostedService).GetMethod("RecoverMissingAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(worker, [scope.ServiceProvider, db, null, CancellationToken.None])!;
    }

    private static async Task<List<TranscriptEntry>> PromptsAsync(TaskCeilingHarness h, Guid session, long floor)
    {
        await using var db = h.Context();
        return await db.TranscriptEntries.AsNoTracking().Where(t => t.AgentSessionId == session
            && t.Kind == TranscriptKinds.UserPrompt && t.Sequence > floor).ToListAsync();
    }

    private sealed class NoticeInsertFault : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public bool Fired { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Armed && !Fired && command.CommandText.Contains("INSERT INTO \"SessionQueuedMessages\"", StringComparison.Ordinal))
            {
                Fired = true;
                throw new InvalidOperationException("C1610 post-commit queue insertion fault");
            }
            return ValueTask.FromResult(result);
        }
    }
}
