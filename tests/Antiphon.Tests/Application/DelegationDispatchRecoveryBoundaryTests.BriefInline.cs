using System.Text;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1150 S2 repair 2 F4: complete inline input from the real brief formatter that mentions
/// <c>.antiphon</c> paths, in any separator, rooted or relative, quoted, or inside a quoted pointer
/// of another task, is inline. It is reused while Pending and received after its complete
/// UserPrompt, and the task is never held.
/// </summary>
public partial class DelegationDispatchRecoveryBoundaryTests
{
    [Test]
    [Arguments("relative-forward", "pending")]
    [Arguments("relative-forward", "received")]
    [Arguments("relative-forward", "working")]
    [Arguments("relative-backslash", "pending")]
    [Arguments("relative-backslash", "received")]
    [Arguments("relative-backslash", "working")]
    [Arguments("absolute-native-existing", "pending")]
    [Arguments("absolute-native-existing", "received")]
    [Arguments("absolute-native-existing", "working")]
    [Arguments("windows-quoted", "pending")]
    [Arguments("windows-quoted", "received")]
    [Arguments("windows-quoted", "working")]
    [Arguments("other-task-pointer", "pending")]
    [Arguments("other-task-pointer", "received")]
    [Arguments("other-task-pointer", "working")]
    public async Task C1150_Inline_brief_mentioning_spill_paths_is_not_held(string mention, string state)
    {
        var label = mention + "/" + state;
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seeded = await SeedCurrentAsync(schema.ConnectionString, "placeholder goal");
        try
        {
            // The supported modern transport types this whole brief inline.
            var settings = new DelegationSettings { PtySingleChunkBytes = 86400, BriefInlineMaxBytes = 43200 };
            var existing = Path.Combine(seeded.Directory, ".antiphon", "inbox", "notes.md");
            var text = mention switch
            {
                "relative-forward" => ".antiphon/inbox/notes.md",
                "relative-backslash" => @".antiphon\inbox\notes.md",
                "absolute-native-existing" => existing,
                "windows-quoted" => @"'C:\Antiphon\work tree\c1150\.antiphon\task-c1150-brief.md'",
                _ => DelegationReportFormatter.BuildBriefPointer(new AgentTask
                {
                    Id = Guid.NewGuid(),
                    Title = "Quoted pointer of another task",
                    Goal = "Another goal",
                    Role = AgentTaskRole.Custom,
                    ModelLevel = AgentModelLevel.Frontier,
                    Workspace = WorkspaceMode.Shared,
                }, settings, ".antiphon/task-0badc0de-brief.md", 5000),
            };
            var goal = text + " opens this goal.\nKeep the middle " + text + " exactly.\nThe inline goal ends with " + text;
            byte[]? fileBefore = null;
            if (mention == "absolute-native-existing")
            {
                // A readable unrelated file at the mentioned path is not this brief's payload.
                Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
                await File.WriteAllTextAsync(existing, "unrelated notes without the task marker");
                fileBefore = await File.ReadAllBytesAsync(existing);
            }

            Guid rowId;
            byte[] bodyBefore;
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                var stored = await db.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
                stored.Goal = goal;
                var row = HealthyRow(seeded, goal);
                row.Body = DelegationReportFormatter.BuildBrief(stored, settings).Trim();
                row.Body.ShouldContain(".antiphon", Case.Sensitive, label);
                db.SessionQueuedMessages.Add(row);
                await db.SaveChangesAsync();
                rowId = row.Id;
                bodyBefore = Encoding.UTF8.GetBytes(row.Body);
            }

            await using var queue = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
            {
                ConnectionString = schema.ConnectionString,
                PreserveDatabaseOnDispose = true,
                AttachSessionId = seeded.SessionId,
                AttachAgentId = seeded.AgentId,
                Delegation = settings,
            });
            var expectedPrompts = 0;
            if (state != "pending")
            {
                await queue.Queue.FlushSessionAsync(seeded.SessionId, CancellationToken.None);
                var sent = await SingleBriefAsync(schema.ConnectionString, seeded.TaskId, label);
                sent.Status.ShouldBe(QueuedMessageStatus.Sent, label);
                sent.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered, label);
                var delivered = await UserPromptsAsync(schema.ConnectionString, seeded.SessionId);
                delivered.Count.ShouldBe(1, label);
                PromptSubmissionMatch.IsCompleteIn(sent.Body, delivered[0]).ShouldBeTrue(label);
                expectedPrompts = 1;
                if (state == "working")
                {
                    await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
                    var working = await db.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
                    working.Status = AgentTaskStatus.Working;
                    await db.SaveChangesAsync();
                }
            }

            var result = await queue.Queue.EnsureDispatchBriefAsync(RequestFor(seeded), CancellationToken.None);
            result.Inserted.ShouldBeFalse(label);
            result.Kind.ShouldBe(state == "pending" ? DispatchBriefKind.Reuse : DispatchBriefKind.Received, label);
            result.MessageId.ShouldBe(rowId, label);

            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var task = await verify.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
            task.Status.ShouldBe(state == "working" ? AgentTaskStatus.Working : AgentTaskStatus.Dispatched, label);
            task.FailureReason.ShouldBeNull(label);
            (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == seeded.TaskId
                && e.Type == AgentTaskEventType.Blocked)).ShouldBe(0, label);
            var kept = await SingleBriefAsync(schema.ConnectionString, seeded.TaskId, label);
            kept.Id.ShouldBe(rowId, label);
            Encoding.UTF8.GetBytes(kept.Body).ShouldBe(bodyBefore, label);
            kept.Status.ShouldBe(state == "pending" ? QueuedMessageStatus.Pending : QueuedMessageStatus.Sent, label);
            (await UserPromptCountAsync(schema.ConnectionString, seeded.SessionId)).ShouldBe(expectedPrompts, label);
            queue.Adapter.SubmittedBodies.Count.ShouldBe(expectedPrompts, label);
            if (fileBefore is not null)
                (await File.ReadAllBytesAsync(existing)).ShouldBe(fileBefore, label);
        }
        finally
        {
            if (Directory.Exists(seeded.Directory))
                Directory.Delete(seeded.Directory, true);
        }
    }
}
