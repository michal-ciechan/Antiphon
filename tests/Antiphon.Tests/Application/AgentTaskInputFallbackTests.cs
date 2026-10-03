using System.Net;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("AgentQueue")]
public sealed class AgentTaskInputFallbackTests
{
    [Test]
    [Arguments("send-now")]
    [Arguments("flush")]
    [Arguments("enqueue-now")]
    public async Task Runner_write_failure_types_only_the_durable_api_pointer(string path)
    {
        var ownership = new AdmittedInputOwnership();
        await using var f = await TaskInputDeliveryFixture.CreateAsync(o => o.AddInterceptors(ownership));
        await f.Replies.RefineAsync(f.TaskId, new string('a', 4200) + "fallback-tail-888",
            CancellationToken.None);
        var initial = await RowAsync(f);
        // An ordinary file blocks the required directory on every OS, including root.
        await File.WriteAllTextAsync(Path.Combine(f.RunnerCwd, ".antiphon"), "directory blocker");
        var runtime = new RecordingInputRuntime();
        var dispatcher = new PhoneHomeCommandDispatcher(runtime, new PhoneHomeSettings
        {
            AllowedCwd = Path.GetDirectoryName(Path.GetDirectoryName(f.RunnerCwd))!,
            RunnerRepository = Path.Combine(Path.GetDirectoryName(f.RunnerCwd)!, "repo"),
            CapacityStatePath = Path.Combine(Path.GetDirectoryName(f.RunnerCwd)!, "capacity"),
        });
        var ready = new RunnerReadySignal();
        await using var transport = await PhoneHomeTestHost.StartAsync(observer: ready,
            configureServices: services => services.AddLogging(b => b.AddProvider(ready)));
        ready.Directory = transport.Directory;
        var ticket = await transport.RegisterAsync();
        await using var peer = new PhoneHomeScriptedPeer();
        peer.Socket.Options.SetRequestHeader(PhoneHomeProtocol.TicketHeader, ticket.Ticket);
        await peer.Socket.ConnectAsync(transport.ConnectUri, CancellationToken.None);
        var live = await ready.Live.Task.WaitAsync(TimeSpan.FromSeconds(10));
        live.RunnerStoreId.ShouldBe(transport.StoreId, "readiness-expected-store");
        peer.Epoch = live.Epoch;
        peer.Start();
        live.DispatchEligible = true;
        PhoneHomeFrame? writerReply = null;
        peer.Reply = frame =>
        {
            if (frame.Operation != PhoneHomeOperation.Input) return null;
            return writerReply = dispatcher.DispatchAsync(frame, CancellationToken.None)
                .GetAwaiter().GetResult();
        };
        var runnerClient = new PhoneHomeRunnerClient(live,
            f.Provider.GetRequiredService<RemoteSpillCourier>());
        f.Adapter.BeforeInput = (text, ct) => runnerClient.SendInputAsync(f.SessionId, text, ct);
        if (path == "flush")
        {
            await f.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd);
            await f.Queue.FlushSessionAsync(f.SessionId, CancellationToken.None);
        }
        else if (path == "enqueue-now")
        {
            // Seed the admitted ownership at the persistence boundary; this API is also used
            // for ordinary question overlays, which intentionally have no task-input key.
            await using (var setup = f.Db())
                await setup.SessionQueuedMessages.ExecuteDeleteAsync();
            ownership.Key = initial.ConversationKey;
            f.Provider.GetRequiredService<RemoteSpillCourier>().Stage(f.SessionId, f.RunnerCwd,
                new PhoneHomeInputSpill(initial.RemoteSpillRelativePath!, initial.RemoteSpillBody!));
            await Should.ThrowAsync<ConflictException>(() =>
                f.Queue.EnqueueDeliveringNowAsync(f.SessionId, initial.Body, CancellationToken.None,
                    QueuedMessageOrigin.Delegation));
        }
        else
            await Should.ThrowAsync<ConflictException>(() =>
                f.Queue.SendNowAsync(f.SessionId, initial.Id, CancellationToken.None));
        writerReply?.ErrorCode.ShouldBe(PhoneHomeProblemTypes.SpillWriteFailedBeforeInput,
            "real-writer-before-input-code");
        f.Adapter.Inputs.ShouldBeEmpty("file-pointer-input-count=0");
        runtime.Inputs.ShouldBeEmpty("real-runtime-input-count=0");
        await using var db = f.Db();
        var changed = await db.SessionQueuedMessages.AsNoTracking().SingleAsync();
        if (path != "enqueue-now") changed.Id.ShouldBe(initial.Id);
        else
        {
            ownership.RowId.ShouldNotBeNull("enqueue-persisted-row-observed");
            changed.Id.ShouldBe(ownership.RowId.Value, "enqueue-fallback-same-persisted-row");
        }
        changed.ConversationKey.ShouldBe(initial.ConversationKey, "fallback-owned-key");
        changed.Status.ShouldBe(QueuedMessageStatus.Pending);
        changed.Body.ShouldContain("/api/agent-tasks/", customMessage: "api-only-persisted-pointer");
        changed.Body.ShouldNotContain(".antiphon/inbox/", customMessage: "fallback-file-pointer-absent");
        changed.RemoteSpillBody.ShouldContain("fallback-tail-888");
        (await db.AgentTaskEvents.CountAsync(e => e.Type == AgentTaskEventType.Warning)).ShouldBe(1,
            "fallback-warning-count=1");
        f.Adapter.BeforeInput = null;
        await f.Queue.SendNowAsync(f.SessionId, changed.Id, CancellationToken.None);
        f.Adapter.SubmittedBodies.ShouldContain(changed.Body, customMessage: "api-only-typed-pointer");
        var input = await db.AgentTaskEvents.SingleAsync(e => e.Type == AgentTaskEventType.Refined);
        const string token = "c965-writer-fallback-test-token";
        (await db.AgentTasks.SingleAsync()).TokenHash = AgentTaskService.HashToken(token);
        await db.SaveChangesAsync();
        await using var host = new TaskInputWebAppFactory(db.Database.GetConnectionString()!);
        using var client = host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, AgentTaskInputService.Route(f.TaskId, input.Id));
        request.Headers.Add("X-Antiphon-Task-Token", token);
        using var response = await client.SendAsync(request);
        (await response.Content.ReadAsStringAsync()).ShouldBe(input.InputBody, "fallback-authorized-whole-body");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task Runner_path_refusal_never_writes_outside_the_mirror()
    {
        var parent = Directory.CreateTempSubdirectory("c888-refusal-").FullName.Replace('\\', '/');
        try
        {
            var mirror = Path.Combine(parent, "worktrees", "task-00000001")
                .Replace('\\', '/');
            Directory.CreateDirectory(mirror);
            var writer = new RunnerWorkspaceService(
                Path.Combine(parent, "repo").Replace('\\', '/'), parent);
            var refusal = await Should.ThrowAsync<PhoneHomeAdmissionException>(() =>
                writer.WriteSpillAsync(mirror,
                    new PhoneHomeInputSpill("../outside.md", "secret"), CancellationToken.None));
            refusal.Message.ShouldContain("Spill relative path is not admitted.",
                customMessage: "traversal-refusal-not-workspace-refusal");
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
        const string otherToken = "c888-other-task-test-token";
        var otherTaskId = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = otherTaskId, RootTaskId = otherTaskId,
            AgentSessionId = f.SessionId,
            Title = "unrelated task", Goal = "Other work.",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code,
            AgentKind = AgentKind.Codex, ModelLevel = AgentModelLevel.High,
            Workspace = WorkspaceMode.Worktree, WorkingDirectory = f.ServerRoot,
            Status = AgentTaskStatus.Working, CreatedAt = DateTime.UtcNow,
            TokenHash = AgentTaskService.HashToken(otherToken),
        });
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
        (await missing.Content.ReadAsStringAsync()).ShouldNotContain("exact " + new string('d', 1800),
            customMessage: "missing-token-body-absent");
        missing.StatusCode.ShouldBe(HttpStatusCode.Forbidden, "missing-token-403");
        using var staleRequest = new HttpRequestMessage(HttpMethod.Get, route);
        staleRequest.Headers.Add("X-Antiphon-Task-Token", "stale-c888-test-token");
        using var stale = await client.SendAsync(staleRequest);
        stale.StatusCode.ShouldBe(HttpStatusCode.Forbidden, "stale-token-403");
        (await stale.Content.ReadAsStringAsync()).ShouldNotContain(input.InputBody!);
        using var otherRequest = new HttpRequestMessage(HttpMethod.Get, route);
        otherRequest.Headers.Add("X-Antiphon-Task-Token", otherToken);
        using var other = await client.SendAsync(otherRequest);
        other.StatusCode.ShouldBe(HttpStatusCode.Forbidden, "other-task-token-403");
        (await other.Content.ReadAsStringAsync()).ShouldNotContain(input.InputBody!);
        const string capabilityToken = "c965-valid-capability-test-token";
        db.DelegationCapabilities.Add(new DelegationCapability
        {
            Id = Guid.NewGuid(), Name = "input-capability", CreatedAt = DateTime.UtcNow,
            TokenHash = AgentTaskService.HashToken(capabilityToken),
            RootsJson = JsonSerializer.Serialize(new[] { f.ServerRoot }),
        });
        await db.SaveChangesAsync();
        // Prove this is a valid capability, rather than another stale bearer.
        using (var scope = host.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<AgentTaskService>()
                .AuthenticateAsync(capabilityToken, CancellationToken.None)).CapabilityId
                .ShouldNotBeNull("capability-positive-control");
        using var capRequest = new HttpRequestMessage(HttpMethod.Get, route);
        capRequest.Headers.Add("X-Antiphon-Task-Token", capabilityToken);
        using var capResponse = await client.SendAsync(capRequest);
        (await capResponse.Content.ReadAsStringAsync()).ShouldNotContain(input.InputBody!,
            customMessage: "capability-token-body-absent");
        capResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden, "capability-token-403");
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
        var legacy = new AgentTaskEvent
        {
            Id = Guid.NewGuid(), AgentTaskId = f.TaskId, AgentSessionId = f.SessionId,
            Type = AgentTaskEventType.Refined, Detail = "legacy truncated detail",
            At = DateTime.UtcNow,
        };
        var nonInput = new AgentTaskEvent
        {
            Id = Guid.NewGuid(), AgentTaskId = f.TaskId, AgentSessionId = f.SessionId,
            Type = AgentTaskEventType.Warning, Detail = "not input",
            InputBody = "must stay private", At = DateTime.UtcNow,
        };
        db.AgentTaskEvents.AddRange(legacy, nonInput);
        await db.SaveChangesAsync();
        (await reader.ReadAsync(f.TaskId, legacy.Id, caller, CancellationToken.None))
            .ShouldBeNull("legacy-null-404");
        (await reader.ReadAsync(f.TaskId, nonInput.Id, caller, CancellationToken.None))
            .ShouldBeNull("non-input-404");
        await Should.ThrowAsync<ForbiddenException>(() =>
            reader.ReadAsync(Guid.NewGuid(), input.Id, caller, CancellationToken.None));
        task.AgentSessionId = Guid.NewGuid();
        await Should.ThrowAsync<ForbiddenException>(() =>
            reader.ReadAsync(f.TaskId, input.Id, caller, CancellationToken.None));
    }

    [Test]
    public async Task Input_body_is_absent_from_task_summary_events_and_logs()
    {
        var logs = new CapturingLoggerProvider();
        await using var f = await TaskInputSpillFixture.CreateAsync(logs: logs);
        var summary = await f.Replies.RefineAsync(f.TaskId,
            new string('f', 4300) + "private-tail-888", CancellationToken.None);
        JsonSerializer.Serialize(summary).ShouldNotContain("private-tail-888",
            customMessage: "summary-body-tail-absent");
        await using var db = f.Db();
        var input = await db.AgentTaskEvents.SingleAsync(e => e.Type == AgentTaskEventType.Refined);
        input.Detail.ShouldNotContain("private-tail-888");
        input.InputBody.ShouldContain("private-tail-888", customMessage: "exact-body-tail-retained");
        const string token = "c965-private-input-test-token";
        (await db.AgentTasks.SingleAsync()).TokenHash = AgentTaskService.HashToken(token);
        await db.SaveChangesAsync();
        await using var host = new TaskInputWebAppFactory(f.ConnectionString, logs);
        using var client = host.CreateClient();
        using var publicResponse = await client.GetAsync($"/api/agent-tasks/{f.TaskId:D}");
        publicResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await publicResponse.Content.ReadAsStringAsync()).ShouldNotContain("private-tail-888",
            customMessage: "public-events-tail-absent");
        using var request = new HttpRequestMessage(HttpMethod.Get, AgentTaskInputService.Route(f.TaskId, input.Id));
        request.Headers.Add("X-Antiphon-Task-Token", token);
        using var authorized = await client.SendAsync(request);
        (await authorized.Content.ReadAsStringAsync()).ShouldBe(input.InputBody,
            "authorized-private-tail-exact");
        logs.Entries.ShouldNotBeEmpty("log-capture-active");
        foreach (var entry in logs.Entries)
        {
            entry.Message.ShouldNotContain("private-tail-888", customMessage: "log-tail-absent");
            JsonSerializer.Serialize(entry.Properties).ShouldNotContain("private-tail-888",
                customMessage: "structured-log-tail-absent");
        }
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
        var reverted = await db.SessionQueuedMessages.SingleAsync();
        reverted.Body.ShouldBe(row.Body);
        reverted.Status.ShouldBe(QueuedMessageStatus.Pending, "ordinary-spill-stays-pending");
        reverted.DeliveryAttempts.ShouldBe(1, "ordinary-spill-attempt-retained");
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

    private sealed class TaskInputWebAppFactory(string connectionString,
        CapturingLoggerProvider? logs = null) : AntiphonWebAppFactory
    {
        protected override string ConnectionString => connectionString;
        protected override void ApplyTestOverrides(IServiceCollection services)
        {
            if (logs is not null) services.AddLogging(b => b.AddProvider(logs));
        }
    }

    private sealed class RecordingInputRuntime : IPhoneHomeRuntimeSurface
    {
        public List<string> Inputs { get; } = [];
        public RunnerCapabilitiesDto Capabilities() => new("InboxConhost", "inbox", "test", false, Features: []);
        public string Health() => "Healthy";
        public IReadOnlyList<RunnerSessionDto> List() => [];
        public int OwnedSessionCount => 0;
        public Task SendInputAsync(Guid id, string input, CancellationToken ct) { Inputs.Add(input); return Task.CompletedTask; }
        public Task<RunnerSessionDto> GetAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct) => throw new NotSupportedException();
        public RunnerBufferDto GetBuffer(Guid id) => throw new NotSupportedException();
        public RunnerSnapshotDto GetSnapshot(Guid id) => throw new NotSupportedException();
        public RunnerTranscriptDto GetTranscript(Guid id) => throw new NotSupportedException();
        public Task<RunnerConditionalInputResult> SendConditionalInputAsync(Guid id, RunnerConditionalInputRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task ClearLiveBufferAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task ResizeAsync(Guid id, int cols, int rows, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunnerKillGenerationResult> KillGenerationAsync(Guid id, DateTime startedAt, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class AdmittedInputOwnership : SaveChangesInterceptor
    {
        public string? Key { get; set; }
        public Guid? RowId { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Key is not null && data.Context is { } db)
                foreach (var entry in db.ChangeTracker.Entries<SessionQueuedMessage>()
                    .Where(e => e.State == EntityState.Added))
                {
                    entry.Entity.ConversationKey = Key;
                    RowId = entry.Entity.Id;
                }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RunnerReadySignal : IRunnerEligibilityObserver, ILoggerProvider, ILogger
    {
        public PhoneHomeRunnerDirectory? Directory { get; set; }
        public TaskCompletionSource<PhoneHomeLiveConnection> Live { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Changed(string runnerId)
        {
            if (Directory?.SnapshotLive(runnerId) is { DispatchEligible: true } live)
                Live.TrySetResult(live);
        }
        public ILogger CreateLogger(string categoryName) => this;
        public void Dispose() { }
        public bool IsEnabled(LogLevel level) => level == LogLevel.Information;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // This lightweight host has no recovery worker. Observe the real endpoint's
            // accepted-connection event, then admit this synthetic empty runner explicitly.
            if (state is IEnumerable<KeyValuePair<string, object?>> properties
                && properties.Any(p => p.Key == "{OriginalFormat}" && p.Value is string template
                    && template.StartsWith("Phone-home connection {RunnerId} epoch {Epoch} accepted:",
                        StringComparison.Ordinal)))
            {
                var runnerId = properties.Single(p => p.Key == "RunnerId").Value as string;
                Directory!.MarkRecovered(Directory.SnapshotLive(runnerId));
            }
        }
    }
}
