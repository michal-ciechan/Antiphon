using System.Text;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
namespace Antiphon.Tests.Application;

/// <summary>CARD-1150 S2 repair F2: a local spill pointer in either separator form must name a present, intact payload.</summary>
public partial class DelegationDispatchRecoveryBoundaryTests
{
    [Test]
    [Arguments("native", "missing")]
    [Arguments("native", "corrupt")]
    [Arguments("native", "unreadable")]
    [Arguments("windows", "missing")]
    [Arguments("posix", "missing")]
    public async Task C1150_Local_spill_must_be_present_and_intact(string form, string state)
    {
        var label = form + "/" + state;
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seeded = await SeedCurrentAsync(schema.ConnectionString, "Required retained input must be present and intact.");
        try
        {
            var marker = DelegationReportFormatter.TaskMarker(seeded.TaskId);
            var name = "task-" + DelegationReportFormatter.Short(seeded.TaskId) + "-brief.md";
            var path = form switch
            {
                "native" => Path.Combine(seeded.Directory, ".antiphon", name),
                "windows" => @"C:\Antiphon\missing-" + seeded.TaskId.ToString("N") + @"\.antiphon\" + name,
                _ => "/tmp/antiphon-missing-" + seeded.TaskId.ToString("N") + "/.antiphon/" + name,
            };
            byte[]? fileBefore = null;
            if (state == "corrupt")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, marker + "\nthe goal was truncated away");
                fileBefore = await File.ReadAllBytesAsync(path);
            }
            else if (state == "unreadable")
            {
                Directory.CreateDirectory(path);
            }

            Guid rowId;
            byte[] bodyBefore;
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == seeded.TaskId);
                var row = HealthyRow(seeded, seeded.Goal);
                row.Body = DelegationReportFormatter.BuildBriefPointer(task, new DelegationSettings(), path, 5000);
                db.SessionQueuedMessages.Add(row);
                await db.SaveChangesAsync();
                rowId = row.Id;
                bodyBefore = Encoding.UTF8.GetBytes(row.Body);
            }

            await using var queue = await OpenQueueAsync(schema.ConnectionString);
            var result = await queue.Queue.EnsureDispatchBriefAsync(RequestFor(seeded), CancellationToken.None);
            result.Kind.ShouldBe(DispatchBriefKind.Unavailable, label);
            result.Inserted.ShouldBeFalse(label);

            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var held = await verify.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
            held.Status.ShouldBe(AgentTaskStatus.Blocked, label);
            held.FailureReason.ShouldBe(DispatchBriefEvidence.InputUnavailableReason, label);
            var rows = await verify.SessionQueuedMessages
                .Where(m => m.ExecutionTaskId == seeded.TaskId || m.AgentSessionId == seeded.SessionId).ToListAsync();
            rows.Count.ShouldBe(1, label);
            rows[0].Id.ShouldBe(rowId, label);
            Encoding.UTF8.GetBytes(rows[0].Body).ShouldBe(bodyBefore, label);
            rows[0].Status.ShouldBe(QueuedMessageStatus.Pending, label);
            (await UserPromptCountAsync(schema.ConnectionString, seeded.SessionId)).ShouldBe(0, label);
            if (fileBefore is not null)
                (await File.ReadAllBytesAsync(path)).ShouldBe(fileBefore, label);
            if (state == "unreadable")
                Directory.Exists(path).ShouldBeTrue(label);
        }
        finally
        {
            if (Directory.Exists(seeded.Directory))
                Directory.Delete(seeded.Directory, true);
        }
    }

    [Test]
    [Arguments("posix", "intact", "Reuse")]
    [Arguments("posix", "missing", "Unavailable")]
    [Arguments("posix", "corrupt", "Unavailable")]
    [Arguments("posix", "unreadable", "Unavailable")]
    [Arguments("windows", "intact", "Reuse")]
    [Arguments("windows", "missing", "Unavailable")]
    [Arguments("windows", "corrupt", "Unavailable")]
    [Arguments("windows", "unreadable", "Unavailable")]
    [Arguments("windows-quoted", "intact", "Reuse")]
    [Arguments("windows-quoted", "missing", "Unavailable")]
    [Arguments("windows-quoted", "corrupt", "Unavailable")]
    [Arguments("windows-quoted", "unreadable", "Unavailable")]
    public Task C1150_Spill_pointer_forms_fail_closed(string form, string state, string expected)
    {
        var label = form + "/" + state;
        var taskId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var dispatched = Pg(DateTime.UtcNow.AddMinutes(-5));
        const string goal = "Spill pointer goal stays exact.";
        var marker = DelegationReportFormatter.TaskMarker(taskId);
        var name = "task-" + DelegationReportFormatter.Short(taskId) + "-brief.md";
        var path = form switch
        {
            "posix" => "/srv/antiphon/worker/.antiphon/" + name,
            "windows" => @"C:\Antiphon\worktrees\worker\.antiphon\" + name,
            _ => @"C:\Antiphon\work tree\worker\.antiphon\" + name,
        };
        var task = new AgentTask
        {
            Id = taskId,
            RootTaskId = taskId,
            Title = "Spill pointer form",
            Goal = goal,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Frontier,
            Role = AgentTaskRole.Custom,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = "/srv/antiphon/worker",
        };
        // A join-safe kind quotes the path; the Windows path with a space must still resolve whole.
        var body = DelegationReportFormatter.BuildBriefPointer(
            task, new DelegationSettings(), path, 5000,
            form == "windows-quoted" ? AgentKind.Codex : AgentKind.ClaudeCode);
        var row = new DispatchBriefRowEvidence(
            Guid.NewGuid(), sessionId, QueuedMessageOrigin.Delegation, QueuedMessageStatus.Pending,
            dispatched, taskId, null, null, null, null, body, null, null, null,
            0, null, null, null, null, null);
        var reads = new List<string>();
        string? Read(string requested)
        {
            reads.Add(requested);
            if (!string.Equals(requested, path, StringComparison.Ordinal))
                return null;
            return state switch
            {
                "intact" => marker + "\n\n" + goal + "\nfull brief",
                "corrupt" => marker + "\nthe goal was truncated away",
                "unreadable" => throw new UnauthorizedAccessException("injected unreadable spill"),
                _ => null,
            };
        }

        var decision = DispatchBriefEvidence.Classify(
            new DispatchBriefEnsureRequest(taskId, 1, sessionId, dispatched, dispatched),
            new DispatchBriefTaskSnapshot(AgentTaskStatus.Dispatched, 1, sessionId, dispatched, dispatched, goal),
            [row],
            [],
            Read);
        DispatchBriefEvidence.AbsoluteSpillPath(body).ShouldBe(path, label);
        reads.ShouldContain(path, label);
        decision.Kind.ToString().ShouldBe(expected, label);
        decision.Hold.ShouldBe(expected != "Reuse", label);
        if (expected == "Unavailable")
            decision.Reason.ShouldBe(DispatchBriefEvidence.InputUnavailableReason, label);
        return Task.CompletedTask;
    }
}
