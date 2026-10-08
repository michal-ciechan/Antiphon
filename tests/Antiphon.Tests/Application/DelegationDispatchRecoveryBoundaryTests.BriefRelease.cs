using System.Text;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1150 S2 repair 2 F5: a late producer's ensure, already in flight while the real queue stages a
/// runner spill, types its pointer, records the complete UserPrompt and releases the retained bytes,
/// must read that receipt before the released payload. Without the receipt the hold stays.
/// </summary>
public partial class DelegationDispatchRecoveryBoundaryTests
{
    [Test]
    [Arguments("complete")]
    [Arguments("clipped")]
    public async Task C1150_Late_ensure_after_remote_receipt_and_payload_release(string receipt)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seeded = await SeedCurrentAsync(
            schema.ConnectionString, "Remote brief goal stays whole.\ncafé ☃\n" + new string('r', 2400));
        try
        {
            var runnerCwd = "/runner/worktrees/c1150-f5-" + seeded.TaskId.ToString("N")[..8];
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                await db.AgentSessions.Where(s => s.Id == seeded.SessionId).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.RunnerId, "server2")
                    .SetProperty(x => x.RunnerStoreId, Guid.NewGuid())
                    .SetProperty(x => x.RunnerCwd, runnerCwd));
            }

            await using var producer = await OpenRemoteQueueAsync(schema.ConnectionString, seeded);
            await using var late = await OpenRemoteQueueAsync(schema.ConnectionString, null);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            late.Queue.BeforeDispatchBriefRowLock = async ct =>
            {
                started.TrySetResult();
                await proceed.Task.WaitAsync(TimeSpan.FromSeconds(60), ct);
            };
            var request = RequestFor(seeded);
            var lateEnsure = Task.Run(() => late.Queue.EnsureDispatchBriefAsync(request, CancellationToken.None));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(60));

            // The real dispatch producer: fit against the runner ceiling, stage, bind, commit, deliver.
            var first = await producer.Queue.EnsureDispatchBriefAsync(request, CancellationToken.None);
            first.Kind.ShouldBe(DispatchBriefKind.Absent, receipt);
            first.Inserted.ShouldBeTrue(receipt);
            if (await UserPromptCountAsync(schema.ConnectionString, seeded.SessionId) == 0)
                await producer.Queue.FlushSessionAsync(seeded.SessionId, CancellationToken.None);

            var marker = DelegationReportFormatter.TaskMarker(seeded.TaskId);
            var sent = await SingleBriefAsync(schema.ConnectionString, seeded.TaskId, receipt);
            sent.Id.ShouldBe(first.MessageId!.Value, receipt);
            sent.Status.ShouldBe(QueuedMessageStatus.Sent, receipt);
            sent.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered, receipt);
            sent.RemoteSpillRelativePath.ShouldBe(TypedBodySpill.InboxRelativePath(sent.Id.ToString("D")), receipt);
            sent.Body.ShouldContain(sent.RemoteSpillRelativePath!, Case.Sensitive, receipt);
            sent.Body.ShouldStartWith(marker, Case.Sensitive, receipt);
            sent.RemoteSpillBody.ShouldBeNull(receipt + ": the queue released the payload after the receipt");
            var prompts = await UserPromptsAsync(schema.ConnectionString, seeded.SessionId);
            prompts.Count.ShouldBe(1, receipt);
            PromptSubmissionMatch.IsCompleteIn(sent.Body, prompts[0]).ShouldBeTrue(receipt);
            producer.Adapter.SubmittedBodies.Count.ShouldBe(1, receipt);
            var bodyBefore = Encoding.UTF8.GetBytes(sent.Body);

            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                if (receipt == "clipped")
                {
                    // The reverse: the bytes are gone and no complete receipt remains.
                    var entry = await db.TranscriptEntries.SingleAsync(t =>
                        t.AgentSessionId == seeded.SessionId && t.Kind == TranscriptKinds.UserPrompt);
                    entry.Text = sent.Body[..(sent.Body.Length / 2)];
                }

                var working = await db.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
                working.Status = AgentTaskStatus.Working;
                await db.SaveChangesAsync();
            }

            proceed.SetResult();
            var result = await lateEnsure;
            result.Inserted.ShouldBeFalse(receipt);

            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var task = await verify.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
            var kept = await SingleBriefAsync(schema.ConnectionString, seeded.TaskId, receipt);
            kept.Id.ShouldBe(sent.Id, receipt);
            Encoding.UTF8.GetBytes(kept.Body).ShouldBe(bodyBefore, receipt);
            kept.Status.ShouldBe(QueuedMessageStatus.Sent, receipt);
            (await UserPromptCountAsync(schema.ConnectionString, seeded.SessionId)).ShouldBe(1, receipt);
            producer.Adapter.SubmittedBodies.Count.ShouldBe(1, receipt);
            late.Adapter.SubmittedBodies.ShouldBeEmpty(receipt);
            (await verify.AgentSessions.SingleAsync(s => s.Id == seeded.SessionId)).Status
                .ShouldBe(SessionStatus.Running, receipt);
            var blockedEvents = await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == seeded.TaskId
                && e.Type == AgentTaskEventType.Blocked);
            if (receipt == "complete")
            {
                result.Kind.ShouldBe(DispatchBriefKind.Received, receipt);
                result.MessageId.ShouldBe(sent.Id, receipt);
                task.Status.ShouldBe(AgentTaskStatus.Working, receipt);
                task.FailureReason.ShouldBeNull(receipt);
                blockedEvents.ShouldBe(0, receipt);
            }
            else
            {
                result.Kind.ShouldBe(DispatchBriefKind.Unavailable, receipt);
                task.Status.ShouldBe(AgentTaskStatus.Blocked, receipt);
                task.FailureReason.ShouldBe(DispatchBriefEvidence.InputUnavailableReason, receipt);
                blockedEvents.ShouldBe(1, receipt);
            }
        }
        finally
        {
            if (Directory.Exists(seeded.Directory))
                Directory.Delete(seeded.Directory, true);
        }
    }

    private static Task<BridgeQueueHarness> OpenRemoteQueueAsync(string connection, CurrentBrief? attached) =>
        BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ConnectionString = connection,
            PreserveDatabaseOnDispose = true,
            AttachSessionId = attached?.SessionId,
            AttachAgentId = attached?.AgentId,
            ConfigureServices = services => services.AddSingleton<RemoteSpillCourier>(),
        });
}
