using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0077: the reuse path's two enqueues (refocus /compact, then the brief) must be independently
/// fault-isolated. A single try around both used to lose the brief silently — a non-OCE throw
/// logged a false "queued" claim, and an HttpClient timeout (TaskCanceledException) escaped the
/// catch entirely.
/// </summary>
[Category("Integration")]
[NotInParallel("AgentQueue")]
public class AgentTaskReuseEnqueueTests
{
    [Test]
    public async Task a_refocus_failure_still_enqueues_the_brief()
    {
        // CARD-0117 S3: pinned to ClaudeCode — Codex no longer enqueues a compact, so this
        // isolation pair would be silently vacuous without an explicit kind.
        var (dispatcher, queue, logs) = CreateHarness();
        var task = await SeedReuseDispatchAsync(AgentKind.ClaudeCode);
        var attempted = new List<string>();
        dispatcher.ReuseEnqueueOverride = async (session, body, ct) =>
        {
            attempted.Add(body);
            if (body.StartsWith("/compact", StringComparison.Ordinal))
                throw new InvalidOperationException("compact inline delivery blew up");
            await queue.EnqueueAsync(
                session, body, MessageSendMode.WhenIdle, ct, QueuedMessageOrigin.Delegation);
        };

        await dispatcher.DeliverReuseMessagesAsync(task, CancellationToken.None);

        attempted.Count.ShouldBe(2, "the brief enqueue must still run after the compact throws");
        attempted[0].ShouldStartWith("/compact");
        attempted[1].ShouldContain(DelegationReportFormatter.TaskMarker(task.Id));

        await using var verify = CreateContext();
        var rows = await verify.SessionQueuedMessages
            .Where(m => m.AgentSessionId == task.AgentSessionId)
            .ToListAsync();
        rows.ShouldContain(
            m => m.Body.Contains(DelegationReportFormatter.TaskMarker(task.Id)),
            "the brief row exists — it was not swallowed by the compact's throw");
        rows.ShouldNotContain(
            m => m.Body.StartsWith("/compact"),
            "the compact threw before persist; only the brief should be queued");

        var incident = await verify.AgentIncidents.SingleAsync(
            i => i.SessionId == task.AgentSessionId
                && i.Kind == AgentIncidentKind.DeliveryTransportFailed);
        incident.Message.ShouldContain("refocus compact");
        incident.Message.ShouldContain("was not queued");
        incident.Message.ShouldNotContain("queued but could not be delivered yet");
        logs.ShouldContain(l => l.Contains("refocus compact") && l.Contains("was not queued"));
        logs.ShouldNotContain(l => l.Contains("queued but could not be delivered yet"));
    }

    [Test]
    public async Task a_brief_failure_does_not_undo_a_queued_refocus()
    {
        var (dispatcher, queue, logs) = CreateHarness();
        var task = await SeedReuseDispatchAsync(AgentKind.ClaudeCode);
        dispatcher.ReuseEnqueueOverride = async (session, body, ct) =>
        {
            if (body.StartsWith("/compact", StringComparison.Ordinal))
            {
                await queue.EnqueueAsync(
                    session, body, MessageSendMode.WhenIdle, ct, QueuedMessageOrigin.Delegation);
                return;
            }

            throw new InvalidOperationException("brief enqueue blew up");
        };

        await dispatcher.DeliverReuseMessagesAsync(task, CancellationToken.None);

        await using var verify = CreateContext();
        var rows = await verify.SessionQueuedMessages
            .Where(m => m.AgentSessionId == task.AgentSessionId)
            .ToListAsync();
        rows.ShouldContain(m => m.Body.StartsWith("/compact"), "compact survived the brief's throw");
        rows.ShouldNotContain(m => m.Body.Contains(DelegationReportFormatter.TaskMarker(task.Id)));

        var incident = await verify.AgentIncidents.SingleAsync(
            i => i.SessionId == task.AgentSessionId
                && i.Kind == AgentIncidentKind.DeliveryTransportFailed);
        incident.Message.ShouldContain("brief");
        incident.Message.ShouldContain("was not queued");
        logs.ShouldContain(l => l.Contains("reuse brief was not queued"));
        logs.ShouldNotContain(l => l.Contains("queued but could not be delivered yet"));
    }

    [Test]
    public async Task an_http_timeout_on_the_refocus_does_not_swallow_the_brief()
    {
        // HttpClient timeout is a TaskCanceledException, an OCE subclass, with nothing actually
        // cancelled — the standing CLAUDE.md rule. The old `when (ex is not OperationCanceledException)`
        // filter let it escape, and TickAsync's identically filtered catch logged a warning that
        // named no task.
        var (dispatcher, queue, _) = CreateHarness();
        var task = await SeedReuseDispatchAsync(AgentKind.ClaudeCode);
        dispatcher.ReuseEnqueueOverride = async (session, body, ct) =>
        {
            if (body.StartsWith("/compact", StringComparison.Ordinal))
                throw new TaskCanceledException(
                    "The request was canceled due to the configured HttpClient.Timeout");
            await queue.EnqueueAsync(
                session, body, MessageSendMode.WhenIdle, ct, QueuedMessageOrigin.Delegation);
        };

        await dispatcher.DeliverReuseMessagesAsync(task, CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.SessionQueuedMessages.AnyAsync(
            m => m.AgentSessionId == task.AgentSessionId
                && m.Body.Contains(DelegationReportFormatter.TaskMarker(task.Id))))
            .ShouldBeTrue("a timeout OCE is a transient failure, not shutdown");
        (await verify.AgentIncidents.AnyAsync(
            i => i.SessionId == task.AgentSessionId
                && i.Kind == AgentIncidentKind.DeliveryTransportFailed
                && i.Message.Contains("refocus compact")))
            .ShouldBeTrue();
    }

    [Test]
    public async Task a_genuine_cancellation_still_propagates_and_skips_the_brief()
    {
        var (dispatcher, _, _) = CreateHarness();
        var task = await SeedReuseDispatchAsync(AgentKind.ClaudeCode);
        var attempted = new List<string>();
        using var cts = new CancellationTokenSource();
        dispatcher.ReuseEnqueueOverride = (_, body, _) =>
        {
            attempted.Add(body);
            // Cancel the caller's token, then throw — the catch filter keys on ct.IsCancellationRequested,
            // not on the exception type alone (an HttpClient timeout is also an OCE).
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        };

        await Should.ThrowAsync<OperationCanceledException>(
            () => dispatcher.DeliverReuseMessagesAsync(task, cts.Token));

        attempted.Count.ShouldBe(1, "shutdown must not continue on to the brief");
        attempted[0].ShouldStartWith("/compact");

        await using var verify = CreateContext();
        (await verify.AgentIncidents.AnyAsync(i => i.SessionId == task.AgentSessionId))
            .ShouldBeFalse("a real cancel is not a delivery failure");
    }

    [Test]
    public async Task both_reuse_messages_enqueue_when_nothing_throws()
    {
        var (dispatcher, _, _) = CreateHarness();
        var task = await SeedReuseDispatchAsync(AgentKind.ClaudeCode);

        await dispatcher.DeliverReuseMessagesAsync(task, CancellationToken.None);

        await using var verify = CreateContext();
        var messages = await verify.SessionQueuedMessages
            .Where(m => m.AgentSessionId == task.AgentSessionId)
            .OrderBy(m => m.Sequence)
            .ToListAsync();
        messages.Count.ShouldBe(2);
        messages[0].Body.ShouldStartWith("/compact");
        messages[1].Body.ShouldContain(DelegationReportFormatter.TaskMarker(task.Id));
    }

    [Test]
    public async Task a_codex_reuse_enqueue_throw_still_raises_its_own_incident()
    {
        // CARD-0117 S3: Codex types no compact, so the single brief enqueue is the whole path.
        // A throw there must still raise DeliveryTransportFailed — the fault-isolation pair is
        // not Claude-only.
        var (dispatcher, _, logs) = CreateHarness();
        var task = await SeedReuseDispatchAsync(AgentKind.Codex);
        dispatcher.ReuseEnqueueOverride = (_, _, _) =>
            throw new InvalidOperationException("brief enqueue blew up");

        await dispatcher.DeliverReuseMessagesAsync(task, CancellationToken.None);

        await using var verify = CreateContext();
        var rows = await verify.SessionQueuedMessages
            .Where(m => m.AgentSessionId == task.AgentSessionId)
            .ToListAsync();
        rows.ShouldBeEmpty("the only enqueue threw before persist");
        rows.ShouldNotContain(m => m.Body.StartsWith("/compact"));

        var incident = await verify.AgentIncidents.SingleAsync(
            i => i.SessionId == task.AgentSessionId
                && i.Kind == AgentIncidentKind.DeliveryTransportFailed);
        incident.Message.ShouldContain("brief");
        incident.Message.ShouldContain("was not queued");
        logs.ShouldContain(l => l.Contains("reuse brief was not queued"));
    }

    private static (AgentTaskDispatcher Dispatcher, SessionMessageQueueService Queue, List<string> Logs)
        CreateHarness()
    {
        var logs = new List<string>();
        var stopper = new RecordingSessionStopper();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings()));
        services.AddOptions<AgentRegistrySettings>();
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper>(stopper);
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddSingleton(Options.Create(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-reuse-enq-wt"),
        }));
        services.AddSingleton<IWorktreeManager, Antiphon.Server.Infrastructure.Git.WorktreeManager>();
        services.AddSingleton<IGitService, Antiphon.Server.Infrastructure.Git.GitService>();
        services.AddScoped<DelegationWorktreeService>();
        services.AddScoped<AgentTaskService>();
        services.AddSingleton<ILogger<AgentTaskDispatcher>>(new ListLogger<AgentTaskDispatcher>(logs));
        services.AddScoped<AgentTaskDispatcher>();

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope().ServiceProvider;
        return (
            scope.GetRequiredService<AgentTaskDispatcher>(),
            scope.GetRequiredService<SessionMessageQueueService>(),
            logs);
    }

    private static async Task<AgentTask> SeedReuseDispatchAsync(AgentKind kind = AgentKind.ClaudeCode)
    {
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var priorId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var agentName = $"reuse-{agentId:N}"[..16];
        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = kind,
            Status = SessionStatus.Running,
            Cwd = Path.GetTempPath(),
            Cols = 120,
            Rows = 30,
            CreatedAt = now.AddHours(-1),
            StartedAt = now.AddHours(-1),
            LastSeenAt = now,
        });
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = agentName,
            Slug = agentName,
            WorkingDirectory = Path.GetTempPath(),
            Details = "CARD-0077 reuse enqueue test.",
            Status = AgentStatus.Running,
            Kind = kind,
            ModelLevel = AgentModelLevel.Medium,
            IsPoolDelegate = true,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now.AddHours(-1),
            UpdatedAt = now,
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = priorId,
            RootTaskId = priorId,
            Title = "the previous work",
            Goal = "the previous work",
            Role = AgentTaskRole.Docs,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = Path.GetTempPath(),
            AgentId = agentId,
            AgentSessionId = sessionId,
            Status = AgentTaskStatus.Succeeded,
            CreatedAt = now.AddMinutes(-30),
            DispatchedAt = now.AddMinutes(-29),
            CompletedAt = now.AddMinutes(-5),
        });
        var task = new AgentTask
        {
            Id = taskId,
            RootTaskId = taskId,
            Title = "the new work",
            Goal = "migrate the compose file to Postgres 18",
            Role = AgentTaskRole.Docs,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = Path.GetTempPath(),
            AgentId = agentId,
            AgentName = agentName,
            AgentSessionId = sessionId,
            Status = AgentTaskStatus.Dispatched,
            CreatedAt = now,
            DispatchedAt = now,
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private sealed class ListLogger<T>(List<string> sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (sink)
                sink.Add($"{formatter(state, exception)}");
        }
    }
}
