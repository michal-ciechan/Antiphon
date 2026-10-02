using System.Net;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("AgentQueue")]
public sealed class AgentTaskInputFallbackTests
{
    [Test]
    public async Task Runner_write_failure_types_only_the_durable_api_pointer()
    {
        await using var f = await TaskInputDeliveryFixture.CreateAsync();
        await f.Replies.RefineAsync(f.TaskId, new string('a', 4200) + "fallback-tail-888",
            CancellationToken.None);
        f.Adapter.BeforeInput = (_, _) => throw new RunnerSpillWriteException();
        var initial = await RowAsync(f);
        await Should.ThrowAsync<ConflictException>(() =>
            f.Queue.SendNowAsync(f.SessionId, initial.Id, CancellationToken.None));
        f.Adapter.Inputs.ShouldBeEmpty("file-pointer-input-count=0");
        await using var db = f.Db();
        var changed = await db.SessionQueuedMessages.AsNoTracking().SingleAsync();
        changed.Id.ShouldBe(initial.Id);
        changed.Status.ShouldBe(QueuedMessageStatus.Pending);
        changed.Body.ShouldContain("/api/agent-tasks/", customMessage: "api-only-persisted-pointer");
        changed.Body.ShouldNotContain(".antiphon/inbox/");
        changed.RemoteSpillBody.ShouldContain("fallback-tail-888");
        (await db.AgentTaskEvents.CountAsync(e => e.Type == AgentTaskEventType.Warning)).ShouldBe(1,
            "fallback-warning-count=1");
        f.Adapter.BeforeInput = null;
        await f.Queue.SendNowAsync(f.SessionId, changed.Id, CancellationToken.None);
        f.Adapter.SubmittedBodies.ShouldContain(changed.Body, customMessage: "api-only-typed-pointer");
    }

    [Test]
    public async Task Runner_path_refusal_never_writes_outside_the_mirror()
    {
        var parent = Directory.CreateTempSubdirectory("c888-refusal-").FullName;
        try
        {
            var mirror = Path.Combine(parent, "worktrees", "task-00000001");
            Directory.CreateDirectory(mirror);
            var writer = new RunnerWorkspaceService(Path.Combine(parent, "repo"), parent);
            await Should.ThrowAsync<PhoneHomeAdmissionException>(() =>
                writer.WriteSpillAsync(mirror,
                    new PhoneHomeInputSpill("../outside.md", "secret"), CancellationToken.None));
            File.Exists(Path.Combine(parent, "worktrees", "outside.md")).ShouldBeFalse(
                "refused-path-input-count=0");
        }
        finally { Directory.Delete(parent, recursive: true); }
    }

    [Test]
    public async Task Input_failure_after_write_never_selects_fallback()
    {
        await using var f = await TaskInputDeliveryFixture.CreateAsync();
        await f.Replies.RefineAsync(f.TaskId, new string('b', 2300), CancellationToken.None);
        var initial = await RowAsync(f);
        f.Adapter.BeforeInput = (input, _) => input == "\r"
            ? throw new RunnerSpillWriteException() : Task.CompletedTask;
        await Should.ThrowAsync<RunnerSpillWriteException>(() =>
            f.Queue.SendNowAsync(f.SessionId, initial.Id, CancellationToken.None));
        await using var db = f.Db();
        var after = await db.SessionQueuedMessages.AsNoTracking().SingleAsync();
        after.Body.ShouldBe(initial.Body, "retype-wire-unchanged");
        after.DeliveryAttempts.ShouldBeGreaterThan(0, "prior-attempt-wire-unchanged");
        (await db.AgentTaskEvents.CountAsync(e => e.Type == AgentTaskEventType.Warning))
            .ShouldBe(0, "fallback-warning-count=0");
    }

    [Test]
    public async Task Restart_after_fallback_commit_replays_the_same_api_pointer()
    {
        await using var f = await TaskInputDeliveryFixture.CreateAsync();
        await f.Replies.RefineAsync(f.TaskId, new string('c', 2300), CancellationToken.None);
        var original = await RowAsync(f);
        f.Adapter.BeforeInput = (_, _) => throw new RunnerSpillWriteException();
        await Should.ThrowAsync<ConflictException>(() =>
            f.Queue.SendNowAsync(f.SessionId, original.Id, CancellationToken.None));
        await using var restartedDb = f.Db();
        var persisted = await restartedDb.SessionQueuedMessages.AsNoTracking().SingleAsync();
        persisted.Id.ShouldBe(original.Id);
        persisted.Body.ShouldContain(AgentTaskInputService.Route(f.TaskId,
            (await restartedDb.AgentTaskEvents.SingleAsync(e => e.Type == AgentTaskEventType.Refined)).Id),
            customMessage: "restart-api-wire-exact");
        persisted.Body.ShouldNotContain(original.RemoteSpillRelativePath!);
        (await restartedDb.AgentTaskEvents.CountAsync(e => e.Type == AgentTaskEventType.Warning)).ShouldBe(1);
    }

    [Test]
    public async Task Input_endpoint_requires_the_recipient_task_token()
    {
        await using var f = await TaskInputSpillFixture.CreateAsync();
        await f.Replies.RefineAsync(f.TaskId, "exact " + new string('d', 1800), CancellationToken.None);
        const string recipientToken = "c888-recipient-test-token";
        await using var db = f.Db();
        var task = await db.AgentTasks.SingleAsync();
        task.TokenHash = AgentTaskService.HashToken(recipientToken);
        await db.SaveChangesAsync();
        var input = await db.AgentTaskEvents.SingleAsync(e => e.Type == AgentTaskEventType.Refined);
        await using var host = new TaskInputWebAppFactory(f.ConnectionString);
        using var client = host.CreateClient();
        var route = AgentTaskInputService.Route(f.TaskId, input.Id);
        using var correct = new HttpRequestMessage(HttpMethod.Get, route);
        correct.Headers.Add("X-Antiphon-Task-Token", recipientToken);
        using var response = await client.SendAsync(correct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, "owner-http-200");
        (await response.Content.ReadAsStringAsync()).ShouldBe(input.InputBody,
            "owner-exact-input-body");
        response.Content.Headers.ContentType?.ToString().ShouldBe("text/plain; charset=utf-8");
        response.Headers.CacheControl?.NoStore.ShouldBeTrue("private-body-no-store");
        using var missing = await client.GetAsync(route);
        missing.StatusCode.ShouldBe(HttpStatusCode.Forbidden, "missing-token-403");
        using var staleRequest = new HttpRequestMessage(HttpMethod.Get, route);
        staleRequest.Headers.Add("X-Antiphon-Task-Token", "stale-c888-test-token");
        using var stale = await client.SendAsync(staleRequest);
        stale.StatusCode.ShouldBe(HttpStatusCode.Forbidden, "stale-token-403");
        (await stale.Content.ReadAsStringAsync()).ShouldNotContain(input.InputBody!);
    }

    [Test]
    public async Task Input_endpoint_binds_task_event_and_recipient_session()
    {
        await using var f = await TaskInputSpillFixture.CreateAsync();
        await f.Replies.RefineAsync(f.TaskId, new string('e', 1800), CancellationToken.None);
        await using var db = f.Db();
        var task = await db.AgentTasks.SingleAsync();
        var input = await db.AgentTaskEvents.SingleAsync(e => e.Type == AgentTaskEventType.Refined);
        var reader = new AgentTaskInputService(db);
        var caller = new AgentTaskService.Caller(task, f.SessionId, f.RunnerRoot);
        (await reader.ReadAsync(f.TaskId, Guid.NewGuid(), caller, CancellationToken.None))
            .ShouldBeNull("wrong-event-404");
        await Should.ThrowAsync<ForbiddenException>(() =>
            reader.ReadAsync(Guid.NewGuid(), input.Id, caller, CancellationToken.None));
        task.AgentSessionId = Guid.NewGuid();
        await Should.ThrowAsync<ForbiddenException>(() =>
            reader.ReadAsync(f.TaskId, input.Id, caller, CancellationToken.None));
    }

    [Test]
    public async Task Input_body_is_absent_from_task_summary_events_and_logs()
    {
        await using var f = await TaskInputSpillFixture.CreateAsync();
        var summary = await f.Replies.RefineAsync(f.TaskId,
            new string('f', 4300) + "private-tail-888", CancellationToken.None);
        JsonSerializer.Serialize(summary).ShouldNotContain("private-tail-888",
            customMessage: "summary-body-tail-absent");
        await using var db = f.Db();
        var input = await db.AgentTaskEvents.SingleAsync(e => e.Type == AgentTaskEventType.Refined);
        input.Detail.ShouldNotContain("private-tail-888");
        input.InputBody.ShouldContain("private-tail-888", customMessage: "exact-body-tail-retained");
    }

    [Test]
    public async Task Ordinary_spill_errors_keep_their_existing_delivery_behavior()
    {
        await using var f = await TaskInputDeliveryFixture.CreateAsync();
        await f.Queue.EnqueueAsync(f.SessionId, "ordinary body", MessageSendMode.WhenIdle,
            CancellationToken.None, QueuedMessageOrigin.Ui);
        var row = await RowAsync(f);
        f.Adapter.BeforeInput = (_, _) => throw new RunnerSpillWriteException();
        await Should.ThrowAsync<ConflictException>(() =>
            f.Queue.SendNowAsync(f.SessionId, row.Id, CancellationToken.None));
        await using var db = f.Db();
        (await db.SessionQueuedMessages.SingleAsync()).Body.ShouldBe(row.Body);
        (await db.AgentTaskEvents.CountAsync(e => e.Type == AgentTaskEventType.Warning))
            .ShouldBe(0, "unowned-fallback-warning-count=0");
    }

    [Test]
    public async Task Fallback_commit_failure_sends_no_replacement_input()
    {
        await using var f = await TaskInputDeliveryFixture.CreateAsync(o =>
            o.AddInterceptors(new FailFallbackSave()));
        await f.Replies.RefineAsync(f.TaskId, new string('p', 2300), CancellationToken.None);
        var original = await RowAsync(f);
        f.Adapter.BeforeInput = (_, _) => throw new RunnerSpillWriteException();
        await Should.ThrowAsync<InvalidOperationException>(() =>
            f.Queue.SendNowAsync(f.SessionId, original.Id, CancellationToken.None));
        f.Adapter.Inputs.ShouldBeEmpty("replacement-input-count=0");
        await using var db = f.Db();
        var persisted = await db.SessionQueuedMessages.AsNoTracking().SingleAsync();
        persisted.Body.ShouldBe(original.Body, "source-wire-recoverable");
        persisted.RemoteSpillBody.ShouldBe(original.RemoteSpillBody);
        (await db.AgentTaskEvents.CountAsync(e => e.Type == AgentTaskEventType.Warning))
            .ShouldBe(0, "fallback-warning-not-committed");
    }

    private sealed class FailFallbackSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<AgentTaskEvent>().Any(e =>
                    e.State == EntityState.Added && e.Entity.Type == AgentTaskEventType.Warning) == true)
                throw new InvalidOperationException("controlled fallback persistence failure");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private static async Task<SessionQueuedMessage> RowAsync(TaskInputDeliveryFixture f)
    {
        await using var db = f.Db();
        return await db.SessionQueuedMessages.AsNoTracking().SingleAsync();
    }

    private sealed class TaskInputWebAppFactory(string connectionString) : AntiphonWebAppFactory
    {
        protected override string ConnectionString => connectionString;
    }
}
