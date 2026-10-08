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
/// CARD-1150 S2 repair 3 through the real queue and database. F7: a retry brief whose previous
/// report quotes this task's own pointer is inline, Pending or Working. F6: the bare-headline
/// pointer persisted before 563568e60 is a spill whose missing payload holds. F8: an undated
/// prompt is a receipt only above the row's delivery floor.
/// </summary>
public partial class DelegationDispatchRecoveryBoundaryTests
{
    [Test]
    [Arguments("retry-handoff-pending", "Reuse")]
    [Arguments("retry-handoff-working", "AttemptOwned")]
    [Arguments("legacy-missing-payload", "Unavailable")]
    [Arguments("legacy-intact-payload", "Reuse")]
    [Arguments("stale-undated-receipt", "Unavailable")]
    [Arguments("proven-undated-receipt", "Received")]
    [Arguments("stale-dated-receipt", "Unavailable")]
    public async Task C1150_Outer_envelope_and_receipt_floor_through_the_queue(string label, string expected)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seeded = await SeedCurrentAsync(schema.ConnectionString, "Keep the complete accepted goal.");
        try
        {
            var retry = label.StartsWith("retry-", StringComparison.Ordinal);
            var attempt = retry ? 2 : 1;
            var working = label == "retry-handoff-working";
            var marker = DelegationReportFormatter.TaskMarker(seeded.TaskId);
            var spill = Path.Combine(seeded.Directory, ".antiphon", "task-" + DelegationReportFormatter.Short(seeded.TaskId) + "-brief.md");
            var settings = new DelegationSettings { PtySingleChunkBytes = 86400, BriefInlineMaxBytes = 43200 };
            Guid rowId;
            byte[] bodyBefore;
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                var task = await db.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
                task.Attempt = attempt;
                var pointer = DelegationReportFormatter.BuildBriefPointer(task, settings, spill, 5000);
                var body = pointer;
                if (retry)
                {
                    task.Result = "The previous run received this pointer and could not read it:\n```text\n" + pointer + "\n```";
                    body = DelegationReportFormatter.BuildBrief(task, settings).Trim();
                    body.ShouldContain(marker + " YOUR BRIEF IS NOT IN THIS MESSAGE.", Case.Sensitive, label);
                    if (working)
                        task.Status = AgentTaskStatus.Working;
                }
                else if (label.StartsWith("legacy-", StringComparison.Ordinal))
                {
                    // The only difference 563568e60 made: the headline had no task marker.
                    body = pointer.Replace(marker + " YOUR BRIEF IS", "YOUR BRIEF IS", StringComparison.Ordinal);
                    if (label == "legacy-intact-payload")
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(spill)!);
                        await File.WriteAllTextAsync(spill, DelegationReportFormatter.BuildBrief(task, settings));
                    }
                }

                var row = HealthyRow(seeded, seeded.Goal);
                row.Body = body;
                if (working || !retry && !label.StartsWith("legacy-", StringComparison.Ordinal))
                {
                    row.DeliveryAttempts = 1;
                    row.LastDeliveryStartedAt = seeded.DispatchedAt;
                    row.LastDeliveryGeneration = seeded.StartedAt;
                    row.LastDeliveryBaselineSequence = 1;
                }

                if (label.EndsWith("-receipt", StringComparison.Ordinal))
                {
                    // Sequence 1 is at the row's floor; sequence 2 follows it.
                    var stale = label != "proven-undated-receipt";
                    db.TranscriptEntries.Add(new TranscriptEntry
                    {
                        Id = Guid.NewGuid(),
                        AgentSessionId = seeded.SessionId,
                        Sequence = stale ? 1 : 2,
                        Kind = TranscriptKinds.UserPrompt,
                        Text = pointer,
                        Timestamp = label == "stale-dated-receipt" ? seeded.DispatchedAt.AddDays(-1) : null,
                        CreatedAt = stale ? seeded.DispatchedAt.AddDays(-1) : seeded.DispatchedAt.AddSeconds(5),
                    });
                }

                db.SessionQueuedMessages.Add(row);
                await db.SaveChangesAsync();
                rowId = row.Id;
                bodyBefore = Encoding.UTF8.GetBytes(row.Body);
            }

            await using var queue = await OpenQueueAsync(schema.ConnectionString);
            var request = RequestFor(seeded) with { Attempt = attempt };
            var result = await queue.Queue.EnsureDispatchBriefAsync(request, CancellationToken.None);
            result.Kind.ToString().ShouldBe(expected, label);
            result.Inserted.ShouldBeFalse(label);

            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var stored = await verify.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
            var held = expected == "Unavailable";
            stored.Status.ShouldBe(held ? AgentTaskStatus.Blocked
                : working ? AgentTaskStatus.Working : AgentTaskStatus.Dispatched, label);
            stored.FailureReason.ShouldBe(held ? DispatchBriefEvidence.InputUnavailableReason : null, label);
            (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == seeded.TaskId
                && e.Type == AgentTaskEventType.Blocked)).ShouldBe(held ? 1 : 0, label);
            var kept = await SingleBriefAsync(schema.ConnectionString, seeded.TaskId, label);
            kept.Id.ShouldBe(rowId, label);
            Encoding.UTF8.GetBytes(kept.Body).ShouldBe(bodyBefore, label);
            (await verify.AgentSessions.SingleAsync(s => s.Id == seeded.SessionId)).Status
                .ShouldBe(SessionStatus.Running, label);
            queue.Adapter.SubmittedBodies.Count.ShouldBe(0, label);
        }
        finally
        {
            if (Directory.Exists(seeded.Directory))
                Directory.Delete(seeded.Directory, true);
        }
    }
}
