using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

internal static class Card1619ConsumerFixture
{
    internal sealed record SeededTask(Guid Id, Guid SessionId, Guid AgentId);

    internal sealed record Harness(
        AgentTaskDispatcher Dispatcher,
        FakeRunnerClient Runner,
        RecordingSessionStopper Stopper,
        DeadSessionFirstSeenState FirstSeen,
        BootWedgeRelaunchState BootWedge,
        FakeTimeProvider Clock);

    /// <summary>
    /// Seeds rows, remembers their ids, and deletes exactly those on dispose — the shared-database
    /// rule, mechanised the same way <c>AttentionServiceTests</c> does it.
    /// </summary>
    internal sealed class Scenario : IAsyncDisposable
    {
        private readonly IsolatedTestSchema _schema;
        private readonly List<ServiceProvider> _providers = [];
        private readonly List<IServiceScope> _scopes = [];
        private Scenario(IsolatedTestSchema schema) => _schema = schema;
        public static async Task<Scenario> CreateAsync() => new(await TestDbFixture.CreateIsolatedSchemaAsync());
        public string ConnectionString => _schema.ConnectionString;
        public AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions(_schema.ConnectionString));
        public AgentSessionRuntime Runtime { get; private set; } = null!;
        private readonly List<Guid> _tasks = [];
        private readonly List<Guid> _sessions = [];
        private readonly List<Guid> _agents = [];

        /// <summary>The one parent session every seeded task replies into, so the note has a home.</summary>
        public Guid ParentSessionId { get; } = Guid.NewGuid();

        private bool _parentSeeded;

        public Harness Harness(params Guid[] gone) =>
            HarnessWithProjectsRoot(
                Path.Combine(Path.GetTempPath(), "antiphon-deadsession-no-jsonl"), gone);

        public Harness HarnessWithProjectsRoot(string claudeProjectsRoot, params Guid[] gone)
        {
            var stopper = new RecordingSessionStopper();
            var runner = new FakeRunnerClient(_schema.ConnectionString);
            foreach (var id in gone)
                runner.Gone.Add(id);
            var firstSeen = new DeadSessionFirstSeenState();
            var bootWedge = new BootWedgeRelaunchState();
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(_schema.ConnectionString));
            services.AddSingleton<IEventBus, MockEventBus>();
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton(Options.Create(new SupervisionSettings()));
            services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
            // Default settings on purpose: DeadSessionFailGraceMinutes = 3 is the shipped window.
            services.AddSingleton(Options.Create(new DelegationSettings()));
            services.AddOptions<AgentRegistrySettings>();
            services.AddSingleton<AgentRegistry>();
            services.AddSingleton<AgentSessionLaunchQueue>();
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<SessionMessageQueueService>();
            services.AddSingleton<IDelegateSessionStopper>(stopper);
            services.AddSingleton<DelegationWorkspaceResolver>();
            services.AddDelegationWorktreeGraph(new GitSettings
            {
                WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-deadsession-wt"),
            });
            services.AddScoped<AgentTaskService>();
            services.AddSingleton<ISessionRunnerClient>(runner);
            services.AddSingleton(firstSeen);
            services.AddSingleton(bootWedge);
            // CARD-0085: same recovery gate as the delivery watchdog. Empty projects root so Arm B
            // cannot scan the machine's real ~/.claude/projects during these fleet-global sweeps
            // unless a test points it at a fixture (CARD-0551 R-2).
            services.AddSingleton<AgentTaskReplyService>();
            services.AddSingleton(Options.Create(new DelegateBindRefusalRecoverySettings
            {
                ClaudeProjectsRoot = claudeProjectsRoot,
            }));
            services.AddSingleton<DelegateBindRefusalRecovery>();
            services.AddScoped<AgentTaskDispatcher>();

            var provider = services.BuildServiceProvider();
            _providers.Add(provider);
            Runtime = provider.GetRequiredService<AgentSessionRuntime>();
            var scope = provider.CreateScope();
            _scopes.Add(scope);
            return new Harness(
                scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>(),
                runner, stopper, firstSeen, bootWedge, clock);
        }

        /// <summary>Two sweeps with the grace elapsed between them: observe, wait, act.</summary>
        public async Task PastGraceAsync(Harness harness)
        {
            await harness.Dispatcher.FailDeadSessionTasksAsync(CancellationToken.None);
            harness.Clock.Advance(TimeSpan.FromMinutes(5));
            await harness.Dispatcher.FailDeadSessionTasksAsync(CancellationToken.None);
        }

        public async Task<SeededTask> AddTaskAsync(
            AgentTaskStatus status,
            SessionStatus sessionStatus,
            string? failureReason = null,
            int? endedMinutesAgo = null,
            AgentTaskRole role = AgentTaskRole.Code,
            AgentTaskReplyTo replyTo = AgentTaskReplyTo.Session,
            bool isPoolDelegate = true,
            bool detachSession = false,
            WorkspaceMode workspace = WorkspaceMode.Shared,
            string? workingDirectory = null,
            string? title = null,
            string? worktreePath = null,
            string? worktreeBranch = null,
            string? mergeTargetRef = null,
            string? repoPath = null,
            string? sessionCwd = null,
            AgentKind agentKind = AgentKind.ClaudeCode,
            SessionTerminationSource terminationSource = SessionTerminationSource.Unknown,
            SessionLaunchBlock? launchBlock = null, Guid? nativeId = null, DateTime? nativeGeneration = null)
        {
            await EnsureParentSessionAsync();

            var sessionId = nativeId ?? Guid.NewGuid();
            var taskId = Guid.NewGuid();
            var agentId = Guid.NewGuid();
            var agentName = $"dead-{agentId:N}"[..16];
            // Recent on purpose: the delivery watchdog's own 10-minute window must not be able to
            // reach these rows, so a failure here can only ever have come from the sweep under test.
            var dispatched = DateTime.UtcNow.AddMinutes(-1);
            var cwd = sessionCwd ?? workingDirectory ?? Path.GetTempPath();

            await using var db = CreateContext();
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId,
                DefinitionName = "dead-session-test",
                AgentKind = agentKind,
                Status = sessionStatus,
                Cwd = cwd,
                Cols = 120,
                Rows = 30,
                CreatedAt = dispatched,
                StartedAt = nativeGeneration ?? dispatched,
                LastSeenAt = dispatched,
                EndedAt = endedMinutesAgo is { } ago ? DateTime.UtcNow.AddMinutes(-ago) : null,
                FailureReason = failureReason,
                TerminationSource = terminationSource,
                LaunchBlock = launchBlock,
            });
            db.Agents.Add(new Agent
            {
                Id = agentId,
                Name = agentName,
                Slug = agentName,
                WorkingDirectory = cwd,
                Details = "Dead-session reconciliation test delegate.",
                Status = AgentStatus.Running,
                ModelLevel = AgentModelLevel.High,
                IsPoolDelegate = isPoolDelegate,
                PersistentSessionId = sessionId.ToString("D"),
                CreatedAt = dispatched,
                UpdatedAt = dispatched,
            });
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = title ?? "Dead session reconciliation test",
                Goal = "Do the thing.",
                Kind = AgentTaskKind.Worker,
                Role = role,
                AgentKind = agentKind,
                ModelLevel = AgentModelLevel.High,
                Workspace = workspace,
                WorkingDirectory = workingDirectory ?? Path.GetTempPath(),
                RepoPath = repoPath,
                WorktreePath = worktreePath,
                WorktreeBranch = worktreeBranch,
                MergeTargetRef = mergeTargetRef,
                AgentId = agentId,
                AgentName = agentName,
                AgentSessionId = detachSession ? null : sessionId,
                Status = status,
                ReplyTo = replyTo,
                ParentSessionId = ParentSessionId,
                CreatedAt = dispatched,
                DispatchedAt = dispatched,
            });
            await db.SaveChangesAsync();

            _sessions.Add(sessionId);
            _agents.Add(agentId);
            _tasks.Add(taskId);
            return new SeededTask(taskId, sessionId, agentId);
        }

        public async Task AddMarkedReportAsync(Guid sessionId, Guid taskId)
        {
            var at = DateTime.UtcNow.AddMinutes(-1);
            var apiCallId = $"msg_{Guid.NewGuid():N}";
            await using var db = CreateContext();
            db.TranscriptEntries.AddRange(
                new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Sequence = 1,
                    Kind = TranscriptKinds.UserPrompt,
                    Uuid = $"dead-marked-{Guid.NewGuid():N}",
                    Role = "user",
                    Text = DelegationReportFormatter.TaskMarker(taskId) + "\n\nDo the thing.",
                    Timestamp = at,
                    CreatedAt = at,
                },
                new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Sequence = 2,
                    Kind = TranscriptKinds.AssistantText,
                    Uuid = $"dead-marked-{Guid.NewGuid():N}",
                    Role = "assistant",
                    Text = "Shipped the work.\n" + DelegationReportFormatter.ReportToken(taskId, "done"),
                    ApiCallId = apiCallId,
                    Timestamp = at,
                    CreatedAt = at,
                },
                new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Sequence = 3,
                    Kind = TranscriptKinds.TurnEnd,
                    Uuid = $"dead-marked-{Guid.NewGuid():N}",
                    Role = "assistant",
                    StopReason = TranscriptKinds.StopReasons.EndTurn,
                    ApiCallId = apiCallId,
                    Timestamp = at,
                    CreatedAt = at,
                });
            await db.SaveChangesAsync();
        }

        public async Task AddTranscriptNoiseAsync(Guid sessionId)
        {
            var at = DateTime.UtcNow.AddMinutes(-1);
            await using var db = CreateContext();
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = 1,
                Kind = TranscriptKinds.UserPrompt,
                Uuid = $"dead-noise-{Guid.NewGuid():N}",
                Role = "user",
                Text = "a human typed here",
                Timestamp = at,
                CreatedAt = at,
            });
            await db.SaveChangesAsync();
        }

        /// <summary>CARD-0547: a hand-seeded type-34 row, production-shaped (64-hex digest, At = start).</summary>
        public async Task<(Guid Id, string Digest)> AddObligationAsync(Guid taskId, int minutesAgo)
        {
            var id = Guid.NewGuid();
            var digest = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            await using var db = CreateContext();
            db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = id,
                AgentTaskId = taskId,
                Type = AgentTaskEventType.CommitRecoveryStarted,
                Detail = digest,
                At = DateTime.UtcNow.AddMinutes(-minutesAgo),
            });
            await db.SaveChangesAsync();
            return (id, digest);
        }

        public async Task DeleteSessionRowAsync(Guid sessionId)
        {
            await using var db = CreateContext();
            await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync();
        }

        public async Task SetSessionStatusAsync(Guid sessionId, SessionStatus status)
        {
            await using var db = CreateContext();
            await db.AgentSessions.Where(s => s.Id == sessionId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status));
        }

        public async Task<AgentTask> ReadTaskAsync(Guid taskId)
        {
            await using var db = CreateContext();
            return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        }

        public async Task<AgentTaskLiveness.SessionSnapshot?> ReadSessionSnapshotAsync(Guid sessionId)
        {
            await using var db = CreateContext();
            var row = await db.AgentSessions.AsNoTracking()
                .Where(s => s.Id == sessionId)
                .Select(s => new { s.Status, s.EndedAt, s.FailureReason, s.TerminationSource, s.ExitCode, s.LaunchBlock })
                .SingleOrDefaultAsync();
            return row is null
                ? null
                : new AgentTaskLiveness.SessionSnapshot(
                    row.Status, row.EndedAt, row.FailureReason, row.TerminationSource, row.ExitCode, row.LaunchBlock);
        }

        /// <summary>Everything queued into THIS scenario's parent session, and nothing else.</summary>
        public async Task<List<string>> ParentNoteBodiesAsync()
        {
            await using var db = CreateContext();
            return await db.SessionQueuedMessages.AsNoTracking()
                .Where(m => m.AgentSessionId == ParentSessionId)
                .Select(m => m.Body)
                .ToListAsync();
        }

        public async Task<List<AttentionItemDto>> AttentionItemsAsync()
        {
            await using var db = CreateContext();
            var service = new AttentionService(
                db, new FakeRunnerClient(_schema.ConnectionString), Options.Create(new SupervisionSettings()),
                Options.Create(new DelegationSettings()), TimeProvider.System,
                NullLogger<AttentionService>.Instance);
            var result = await service.GetAsync(CancellationToken.None);
            return result.Items.Where(i => i.TaskId is { } t && _tasks.Contains(t)).ToList();
        }

        private async Task EnsureParentSessionAsync()
        {
            if (_parentSeeded)
                return;
            _parentSeeded = true;

            await using var db = CreateContext();
            db.AgentSessions.Add(new AgentSession
            {
                Id = ParentSessionId,
                DefinitionName = "dead-session-test-parent",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = Path.GetTempPath(),
                Cols = 120,
                Rows = 30,
                CreatedAt = DateTime.UtcNow.AddHours(-1),
                StartedAt = DateTime.UtcNow.AddHours(-1),
                LastSeenAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            _sessions.Add(ParentSessionId);
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = CreateContext();
            await db.TranscriptEntries.Where(e => _sessions.Contains(e.AgentSessionId)).ExecuteDeleteAsync();
            await db.AgentTaskEvents.Where(e => _tasks.Contains(e.AgentTaskId)).ExecuteDeleteAsync();
            await db.AgentIncidents.Where(i => i.AgentId != null && _agents.Contains(i.AgentId.Value)).ExecuteDeleteAsync();
            await db.AgentTasks.Where(t => _tasks.Contains(t.Id)).ExecuteDeleteAsync();
            await db.SessionQueuedMessages
                .Where(m => _sessions.Contains(m.AgentSessionId)).ExecuteDeleteAsync();
            await db.Agents.Where(a => _agents.Contains(a.Id)).ExecuteDeleteAsync();
            await db.AgentSessions.Where(s => _sessions.Contains(s.Id)).ExecuteDeleteAsync();
            foreach (var scope in _scopes) scope.Dispose();
            foreach (var provider in _providers) await provider.DisposeAsync();
            await _schema.DisposeAsync();
        }
    }

    /// <summary>
    /// The runner's view, faked. It reports every session the database knows about — plus every one
    /// an open task names — as <c>Running</c>, EXCEPT the ids a test puts in <see cref="Gone"/>.
    ///
    /// <para>That is not laziness, it is the shared-database rule applied to a fleet-global sweep: a
    /// fake that returned an empty list would tell the sweep that every other suite's session is
    /// gone too, and it would dutifully fail their tasks. Here the only rows this sweep can reach are
    /// the ones the test itself declared dead.</para>
    /// </summary>
    internal sealed class FakeRunnerClient(string connectionString) : ISessionRunnerClient
    {
        private AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions(connectionString));
        public HashSet<Guid> Gone { get; } = [];
        public Exception? ListError { get; set; }
        public List<Guid> Killed { get; } = [];
        public int InputsSent { get; private set; }

        public async Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct)
        {
            if (ListError is not null)
                throw ListError;

            await using var db = CreateContext();
            var rows = await db.AgentSessions.AsNoTracking().Select(s => s.Id).ToListAsync(ct);
            var named = await db.AgentTasks.AsNoTracking()
                .Where(t => t.AgentSessionId != null)
                .Select(t => t.AgentSessionId!.Value)
                .Distinct()
                .ToListAsync(ct);

            return rows.Concat(named).Distinct()
                .Where(id => !Gone.Contains(id))
                .Select(id => new SessionRunnerSessionDto(
                    id, Pid: 4242, StartedAt: DateTime.UtcNow.AddHours(-1),
                    Status: "Running", ExitCode: null, ExitReason: AgentExitReason.Unknown,
                    LastSequence: 10, HostPid: 4243))
                .ToList();
        }

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct)
        {
            Killed.Add(sessionId);
            throw new NotSupportedException("nothing in this sweep may kill a session");
        }

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerBufferDto(sessionId, "> ", 10));

        // CARD-0228 (auditing CARD-0222's frozen-clock deadlock): this throw is why this
        // harness's frozen FakeTimeProvider(DateTimeOffset.UtcNow) is safe today — it short-circuits
        // SessionMessageQueueService's verify path before any Task.Delay(..., _timeProvider) wait
        // loop is reached. Giving this a real implementation exposes the CARD-0222 class here too;
        // switch the clock to the offset-over-real-clock pattern (HerdrAlwaysOnChannelParityTests /
        // AgentSupervisionTests) in the same change.
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
        {
            InputsSent++;
            throw new NotSupportedException();
        }

        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) =>
            throw new NotSupportedException();
    }

}
