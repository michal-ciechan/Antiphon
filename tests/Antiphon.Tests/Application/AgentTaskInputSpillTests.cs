using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("AgentQueue")]
public sealed class AgentTaskInputSpillTests
{
    [Test]
    public async Task Runner_codex_refinement_writes_complete_body_before_pointer()
    {
        await using var f = await TaskInputSpillFixture.CreateAsync();
        var message = "apply this change " + new string('x', 5000) + " tail-888";
        await f.Replies.RefineAsync(f.TaskId, message, CancellationToken.None);
        await using var db = f.Db();
        var row = await db.SessionQueuedMessages.SingleAsync();
        var input = await db.AgentTaskEvents.SingleAsync(e => e.Type == AgentTaskEventType.Refined);
        row.RemoteSpillRelativePath.ShouldBe(TypedBodySpill.InboxRelativePath(row.Id.ToString("D")),
            "relative-pointer");
        row.RemoteSpillBody.ShouldBe(input.InputBody, "runner-body-exact");
        input.InputBody.ShouldContain("tail-888");
        Directory.GetFiles(f.ServerRoot, "*.md", SearchOption.AllDirectories).ShouldBeEmpty(
            "server-spill-absent");
        await WriteOnRunnerAsync(f, row);
        File.Exists(Path.Combine(f.RunnerRoot, row.RemoteSpillRelativePath!)).ShouldBeTrue(
            "runner-file-present");
        (await File.ReadAllTextAsync(Path.Combine(f.RunnerRoot, row.RemoteSpillRelativePath!)))
            .ShouldBe(input.InputBody, "runner-body-exact");
        row.Body.ShouldContain(DelegationReportFormatter.TaskMarker(f.TaskId));
        row.Body.ShouldContain(row.RemoteSpillRelativePath!);
        System.Text.Encoding.UTF8.GetByteCount(row.Body).ShouldBeLessThanOrEqualTo(1024);

        await f.Replies.RefineAsync(f.TaskId, "short codex refinement", CancellationToken.None);
        var shortRow = await db.SessionQueuedMessages.AsNoTracking().OrderByDescending(r => r.Sequence).FirstAsync();
        shortRow.RemoteSpillBody.ShouldNotBeNull("short-codex-forced-spill");
        shortRow.Body.Contains('\n').ShouldBeFalse("join-safe-pointer");
    }

    [Test]
    public async Task Runner_claude_refinement_uses_the_same_inbox_route()
    {
        await using var f = await TaskInputSpillFixture.CreateAsync(AgentKind.ClaudeCode);
        var task = new AgentTask { Id = f.TaskId };
        foreach (var target in new[] { 899, 900, 901 })
        {
            var oneByte = System.Text.Encoding.UTF8.GetByteCount(
                DelegationReportFormatter.BuildRefinement(task, "x"));
            var message = new string('x', target - oneByte + 1);
            System.Text.Encoding.UTF8.GetByteCount(
                DelegationReportFormatter.BuildRefinement(task, message)).ShouldBe(target);
            await f.Replies.RefineAsync(f.TaskId, message, CancellationToken.None);
        }
        await using var db = f.Db();
        var rows = await db.SessionQueuedMessages.AsNoTracking().OrderBy(r => r.Sequence).ToListAsync();
        rows.Count.ShouldBe(3);
        rows[0].RemoteSpillBody.ShouldBeNull("899-inline");
        rows[1].RemoteSpillBody.ShouldBeNull("900-inline");
        rows[2].RemoteSpillBody.ShouldNotBeNull("901-spill");
        rows[2].Body.ShouldContain(rows[2].RemoteSpillRelativePath!);
    }

    [Test]
    public async Task Runner_grok_refinement_is_forced_to_a_join_safe_pointer()
    {
        await using var f = await TaskInputSpillFixture.CreateAsync(AgentKind.Grok);
        await f.Replies.RefineAsync(f.TaskId, "short", CancellationToken.None);
        await using var db = f.Db();
        var row = await db.SessionQueuedMessages.SingleAsync();
        row.RemoteSpillBody.ShouldContain("short", customMessage: "grok-forced-spill");
        row.Body.Contains('\n').ShouldBeFalse("join-safe-pointer");
        row.Body.ShouldContain(DelegationReportFormatter.TaskMarker(f.TaskId));
    }

    [Test]
    public async Task Runner_ceiling_ignores_the_modern_desktop_profile()
    {
        await using var f = await TaskInputSpillFixture.CreateAsync(AgentKind.ClaudeCode);
        await f.Replies.RefineAsync(f.TaskId, new string('z', 1800), CancellationToken.None);
        await using var db = f.Db();
        var row = await db.SessionQueuedMessages.SingleAsync();
        row.RemoteSpillBody.ShouldNotBeNull("runner-ceiling-spill");
        row.Body.ShouldNotContain(TypedBodySpill.PointerHeadline);
        System.Text.Encoding.UTF8.GetByteCount(row.Body).ShouldBeLessThanOrEqualTo(1024);
    }

    [Test]
    public void Bound_pointer_measures_utf8_after_guid_path_rewrite()
    {
        var task = new AgentTask { Id = Guid.NewGuid(), Title = new string('t', 700) };
        var eventId = Guid.NewGuid();
        var temporary = $".antiphon/i-{eventId:N}";
        var bound = TypedBodySpill.InboxRelativePath(Guid.Empty.ToString("D"));
        var wire = DelegationReportFormatter.BuildTaskInputPointer(task, eventId, temporary,
            4000, AgentKind.ClaudeCode, 1024, boundSpillPath: bound);
        wire.ShouldContain(DelegationReportFormatter.TaskMarker(task.Id));
        System.Text.Encoding.UTF8.GetByteCount(wire.Replace(temporary, bound, StringComparison.Ordinal))
            .ShouldBeLessThanOrEqualTo(1024, "final-wire-byte-limit");
    }

    [Test]
    public async Task Two_same_tick_refinements_keep_distinct_paths_and_bodies()
    {
        await using var f = await TaskInputSpillFixture.CreateAsync();
        await f.Replies.RefineAsync(f.TaskId, "first " + new string('a', 2000), CancellationToken.None);
        await f.Replies.RefineAsync(f.TaskId, "second " + new string('b', 2000), CancellationToken.None);
        await using var db = f.Db();
        var rows = await db.SessionQueuedMessages.AsNoTracking().OrderBy(r => r.Sequence).ToListAsync();
        rows.Count.ShouldBe(2);
        rows[0].RemoteSpillRelativePath.ShouldNotBe(rows[1].RemoteSpillRelativePath,
            "distinct-queue-paths");
        rows[0].ConversationKey.ShouldNotBe(rows[1].ConversationKey);
        rows[1].RemoteSpillBody.ShouldContain("second", customMessage: "second-runner-body-exact");
        rows[0].RemoteSpillBody.ShouldNotContain("second");
        (await db.AgentTaskEvents.CountAsync(e => e.InputBody != null)).ShouldBe(2);
    }

    [Test]
    public async Task Local_desktop_refinement_keeps_its_file_and_pointer_contract()
    {
        await using var f = await TaskInputSpillFixture.CreateAsync(AgentKind.ClaudeCode, remote: false);
        await f.Replies.RefineAsync(f.TaskId, new string('l', 3000), CancellationToken.None);
        await using var db = f.Db();
        var row = await db.SessionQueuedMessages.SingleAsync();
        var path = Directory.GetFiles(Path.Combine(f.ServerRoot, ".antiphon"), "*.md")
            .ShouldHaveSingleItem();
        row.Body.ShouldContain(path, customMessage: "desktop-absolute-pointer");
        row.RemoteSpillBody.ShouldBeNull();
        (await File.ReadAllTextAsync(path)).ShouldContain(new string('l', 3000));
    }

    [Test]
    public async Task Runner_blocked_reply_reaches_the_inbox_with_task_attribution()
    {
        await using var f = await TaskInputSpillFixture.CreateAsync(status: AgentTaskStatus.Blocked);
        await f.Replies.AnswerAsync(f.TaskId, new string('r', 3000), CancellationToken.None);
        await using var db = f.Db();
        var row = await db.SessionQueuedMessages.SingleAsync();
        var task = await db.AgentTasks.SingleAsync();
        task.Status.ShouldBe(AgentTaskStatus.Working);
        task.RepliedAt.ShouldNotBeNull();
        row.RemoteSpillBody.ShouldContain(new string('r', 3000), customMessage: "reply-runner-body-exact");
        row.Body.ShouldContain(DelegationReportFormatter.TaskMarker(f.TaskId));
        var input = await db.AgentTaskEvents.SingleAsync(e => e.Type == AgentTaskEventType.Replied);
        input.InputBody.ShouldBe(row.RemoteSpillBody);
    }

    [Test]
    public async Task Runner_small_reply_remains_inline()
    {
        await using var f = await TaskInputSpillFixture.CreateAsync(status: AgentTaskStatus.Blocked);
        await f.Replies.AnswerAsync(f.TaskId, "Use the existing design.", CancellationToken.None);
        await using var db = f.Db();
        var row = await db.SessionQueuedMessages.SingleAsync();
        row.RemoteSpillBody.ShouldBeNull("inline-answer-exact");
        row.Body.ShouldContain("Use the existing design.");
        row.Body.ShouldStartWith(DelegationReportFormatter.TaskMarker(f.TaskId));
    }

    [Test]
    public async Task Queued_refinement_still_amends_the_goal()
    {
        await using var f = await TaskInputSpillFixture.CreateAsync(status: AgentTaskStatus.Queued);
        await f.Replies.RefineAsync(f.TaskId, "add this", CancellationToken.None);
        await using var db = f.Db();
        (await db.AgentTasks.SingleAsync()).Goal.ShouldContain("add this");
        (await db.SessionQueuedMessages.CountAsync()).ShouldBe(0);
        (await db.AgentTaskEvents.SingleAsync()).InputBody.ShouldBeNull();
    }

    [Test]
    public async Task Fallback_input_body_survives_complete_prompt_and_restart()
    {
        await using var f = await TaskInputSpillFixture.CreateAsync();
        var message = new string('a', 4200) + "sentinel-input-tail-888";
        await f.Replies.RefineAsync(f.TaskId, message, CancellationToken.None);
        await using var db = f.Db();
        var row = await db.SessionQueuedMessages.SingleAsync();
        var input = await db.AgentTaskEvents.SingleAsync(e => e.Type == AgentTaskEventType.Refined);
        row.RemoteSpillBody = null;
        await db.SaveChangesAsync();
        await using var restartDb = f.Db();
        var service = new AgentTaskInputService(restartDb);
        var caller = new AgentTaskService.Caller(await restartDb.AgentTasks.SingleAsync(),
            f.SessionId, f.RunnerRoot);
        var read = await service.ReadAsync(f.TaskId, input.Id, caller, CancellationToken.None);
        read.ShouldContain("sentinel-input-tail-888", customMessage: "immutable-input-tail");
        read.ShouldBe(input.InputBody);
    }

    private static async Task WriteOnRunnerAsync(TaskInputSpillFixture f, SessionQueuedMessage row)
    {
        var writer = new RunnerWorkspaceService(Path.Combine(f.RunnerParent, "repo"), f.RunnerParent);
        await writer.WriteSpillAsync(f.RunnerRoot,
            new PhoneHomeInputSpill(row.RemoteSpillRelativePath!, row.RemoteSpillBody!, row.Id),
            CancellationToken.None);
    }
}
