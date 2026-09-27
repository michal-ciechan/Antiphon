using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Agents.Pty;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class SpecialistExecutionIdentityTests
{
    [Test]
    [Arguments("started", false)]
    [Arguments("session", true)]
    [Arguments("profile", false)]
    public async Task Card0758_legacy_Check_restart_profile_matrix(string drift, bool deadlinePolicy)
    {
        await using var f = await Card0758Fixture.CreateAsync(deadlinePolicy);
        var original = f.Task;
        original.SpecialistSessionId.ShouldBe(f.OriginalSessionId);
        original.SpecialistSessionStartedAt.ShouldBe(f.OriginalStartedAt);
        original.SpecialistProfileRevisionId.ShouldBe(f.Profile0);
        original.ExecutionDeadlineAt.HasValue.ShouldBe(deadlinePolicy);
        await f.ChangeGenerationAsync(drift);
        await using var bridge = await f.AttachBridgeAsync();
        using var provider = f.Dispatcher(bridge).Provider;
        await provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>()
            .TickAsync(CancellationToken.None);
        if (drift == "profile")
        {
            await f.AssertRefusedAsync(bridge);
            return;
        }
        await f.AssertAdoptedAsync(bridge, original);
        await provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>()
            .TickAsync(CancellationToken.None);
        await f.AssertAdoptedAsync(bridge, original);
    }

    [Test]
    [Arguments("profile-only")]
    [Arguments("add")]
    [Arguments("remove")]
    public async Task Card0758_profile_revision_changes_refuse_before_input(string change)
    {
        await using var f = await Card0758Fixture.CreateAsync();
        await using (var db = f.Db())
        {
            if (change == "add")
            {
                (await db.AgentTasks.SingleAsync(t => t.Id == f.Task.Id)).SpecialistProfileRevisionId = null;
                f.Task.SpecialistProfileRevisionId = null;
            }
            (await db.AgentSessions.SingleAsync(s => s.Id == f.CurrentSessionId)).TuiProfileRevisionId =
                change == "remove" ? null : f.Profile1;
            await db.SaveChangesAsync();
        }
        await using var bridge = await f.AttachBridgeAsync();
        await f.TickAsync(bridge);
        await f.AssertRefusedAsync(bridge);
    }

    [Test]
    [Arguments("started")]
    [Arguments("session")]
    [Arguments("profile")]
    public async Task Card0758_typed_Check_drift_is_never_rebound(string drift)
    {
        await using var f = await Card0758Fixture.CreateAsync(typedOwner: true);
        var policy = new SpecialistInputPolicy(1, f.Task.Id, f.OriginalSessionId,
            f.OriginalStartedAt, AgentKind.ClaudeCode, DeliveryBackend.ModernConPty,
            43_200, "synthetic-card0758-capability").Serialize();
        await using (var db = f.Db())
            await db.AgentTasks.Where(t => t.Id == f.Task.Id).ExecuteUpdateAsync(u =>
                u.SetProperty(t => t.SpecialistInputPolicyJson, policy));
        await f.ChangeGenerationAsync(drift == "profile" ? "profile-only" : drift);
        await using var bridge = await f.AttachBridgeAsync();
        await f.TickAsync(bridge);
        await f.AssertRefusedAsync(bridge);
        await using var verify = f.Db();
        (await verify.AgentTasks.SingleAsync(t => t.Id == f.Task.Id)).SpecialistInputPolicyJson.ShouldBe(policy);
    }

    [Test]
    [Arguments("task-role")]
    [Arguments("slug")]
    [Arguments("owner")]
    [Arguments("seat-role")]
    [Arguments("always-on")]
    [Arguments("pool")]
    public async Task Card0758_only_the_owned_primary_legacy_Check_can_rebind(string exclusion)
    {
        await using var f = await Card0758Fixture.CreateAsync();
        await f.ChangeGenerationAsync("started");
        await using (var db = f.Db())
        {
            var seat = await db.Agents.SingleAsync(a => a.Id == f.AgentId);
            var task = await db.AgentTasks.SingleAsync(t => t.Id == f.Task.Id);
            switch (exclusion)
            {
                case "task-role": task.Role = AgentTaskRole.Diagnose; break;
                case "slug": seat.Slug = $"foreign-{Guid.NewGuid():N}"; break;
                case "owner":
                    var foreign = new Agent { Id = Guid.NewGuid(), Name = "foreign owner",
                        Slug = $"foreign-{Guid.NewGuid():N}", Kind = AgentKind.ClaudeCode,
                        WorkingDirectory = seat.WorkingDirectory, CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow };
                    db.Agents.Add(foreign);
                    seat.StandingSpecialistOwnerId = foreign.Id;
                    break;
                case "seat-role": seat.StandingSpecialistRole = AgentTaskRole.Diagnose; break;
                case "always-on": seat.AlwaysOn = false; break;
                case "pool": seat.IsPoolDelegate = true; break;
            }
            await db.SaveChangesAsync();
        }
        await using var bridge = await f.AttachBridgeAsync();
        await f.TickAsync(bridge);
        if (exclusion == "pool") await f.AssertQueuedAsync(bridge);
        else await f.AssertRefusedAsync(bridge);
    }

    [Test]
    [Arguments(SessionStatus.Starting)]
    [Arguments(SessionStatus.Stopped)]
    public async Task Card0758_restart_waits_for_a_Running_generation(SessionStatus state)
    {
        await using var f = await Card0758Fixture.CreateAsync();
        await f.ChangeGenerationAsync("started");
        await using (var db = f.Db())
            await db.AgentSessions.Where(s => s.Id == f.CurrentSessionId).ExecuteUpdateAsync(u =>
                u.SetProperty(s => s.Status, state));
        await using var bridge = await f.AttachBridgeAsync();
        await f.TickAsync(bridge);
        await f.AssertQueuedAsync(bridge);
        await using (var db = f.Db())
            await db.AgentSessions.Where(s => s.Id == f.CurrentSessionId).ExecuteUpdateAsync(u =>
                u.SetProperty(s => s.Status, SessionStatus.Running));
        await f.TickAsync(bridge);
        await f.AssertAdoptedAsync(bridge, f.Task);
    }

    [Test]
    [Arguments(AgentTaskStatus.Dispatched)]
    [Arguments(AgentTaskStatus.Working)]
    public async Task Card0758_restarted_Check_waits_for_the_current_owner(AgentTaskStatus ownerStatus)
    {
        await using var f = await Card0758Fixture.CreateAsync();
        await f.ChangeGenerationAsync("started");
        var ownerId = Guid.NewGuid();
        var hookFired = false;
        async Task AddCurrentOwner(AppDbContext _, CancellationToken ct)
        {
            hookFired = true;
            await using var db = f.Db();
            db.AgentTasks.Add(new AgentTask
            {
                Id = ownerId, RootTaskId = ownerId, Title = "older owner", Goal = "older",
                Role = AgentTaskRole.Check, AgentId = f.AgentId, AgentSessionId = f.CurrentSessionId,
                AgentKind = AgentKind.ClaudeCode, Status = ownerStatus,
                WorkingDirectory = Path.GetTempPath(), CreatedAt = DateTime.UtcNow.AddHours(-1),
                DispatchedAt = DateTime.UtcNow.AddHours(-1),
            });
            await db.SaveChangesAsync(ct);
        }
        await using var bridge = await f.AttachBridgeAsync();
        await f.TickAsync(bridge, AddCurrentOwner);
        hookFired.ShouldBeTrue();
        await f.AssertQueuedAsync(bridge);
        await using (var db = f.Db())
            await db.AgentTasks.Where(t => t.Id == ownerId).ExecuteUpdateAsync(u =>
                u.SetProperty(t => t.Status, AgentTaskStatus.Succeeded));
        await f.TickAsync(bridge);
        await f.AssertAdoptedAsync(bridge, f.Task);
    }

    [Test]
    [Arguments("seat-kind")]
    [Arguments("tier")]
    [Arguments("exact-model")]
    [Arguments("effective-model")]
    [Arguments("alias")]
    public async Task Card0758_restart_keeps_every_execution_identity_guard(string drift)
    {
        await using var f = await Card0758Fixture.CreateAsync();
        await f.ChangeGenerationAsync("started");
        await using (var db = f.Db())
        {
            var seat = await db.Agents.SingleAsync(a => a.Id == f.AgentId);
            var session = await db.AgentSessions.SingleAsync(s => s.Id == f.CurrentSessionId);
            var task = await db.AgentTasks.SingleAsync(t => t.Id == f.Task.Id);
            switch (drift)
            {
                case "seat-kind": seat.Kind = AgentKind.Codex; break;
                case "tier": seat.ModelLevel = AgentModelLevel.Low; break;
                case "exact-model": task.SpecialistModelId = "opus"; break;
                case "effective-model": task.SpecialistEffectiveModelId = "opus"; break;
                case "alias": task.SpecialistModelAlias = "opus"; break;
            }
            await db.SaveChangesAsync();
        }
        await using var bridge = await f.AttachBridgeAsync();
        await f.TickAsync(bridge);
        await f.AssertRefusedAsync(bridge);
    }

    [Test]
    [Arguments("before-tick")]
    [Arguments("after-snapshot")]
    public async Task Card0758_expired_Check_is_never_rebound(string boundary)
    {
        await using var f = await Card0758Fixture.CreateAsync(deadlinePolicy: true);
        await f.ChangeGenerationAsync("started");
        if (boundary == "before-tick")
            await using (var db = f.Db())
                await db.AgentTasks.Where(t => t.Id == f.Task.Id).ExecuteUpdateAsync(u =>
                    u.SetProperty(t => t.ExecutionDeadlineAt, DateTime.UtcNow.AddSeconds(-1)));
        var hookFired = false;
        async Task ExpireAtSnapshot(AppDbContext _, CancellationToken ct)
        {
            hookFired = true;
            await using var edit = f.Db();
            await edit.AgentTasks.Where(t => t.Id == f.Task.Id).ExecuteUpdateAsync(u =>
                u.SetProperty(t => t.ExecutionDeadlineAt, DateTime.UtcNow.AddSeconds(-1)), ct);
        }
        await using var bridge = await f.AttachBridgeAsync();
        await f.TickAsync(bridge, boundary == "after-snapshot" ? ExpireAtSnapshot : null);
        hookFired.ShouldBe(boundary == "after-snapshot");
        await using var verify = f.Db();
        var row = await verify.AgentTasks.SingleAsync(t => t.Id == f.Task.Id);
        row.Status.ShouldBe(AgentTaskStatus.Canceled);
        row.CompletedAt.ShouldNotBeNull();
        row.SpecialistSessionId.ShouldBe(f.OriginalSessionId);
        row.SpecialistSessionStartedAt.ShouldBe(f.OriginalStartedAt);
        row.AgentSessionId.ShouldBeNull();
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == row.Id
            && e.Type == AgentTaskEventType.Warning)).ShouldBe(0);
        (await verify.SessionQueuedMessages.CountAsync(m => m.ExecutionTaskId == row.Id)).ShouldBe(0);
        bridge.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    [Arguments(AgentTaskStatus.Dispatched)]
    [Arguments(AgentTaskStatus.Working)]
    [Arguments(AgentTaskStatus.Blocked)]
    [Arguments(AgentTaskStatus.Succeeded)]
    [Arguments(AgentTaskStatus.Failed)]
    [Arguments(AgentTaskStatus.Canceled)]
    public async Task Card0758_nonqueued_Check_is_not_resurrected(AgentTaskStatus status)
    {
        await using var f = await Card0758Fixture.CreateAsync();
        await f.ChangeGenerationAsync("started");
        var hookFired = false;
        async Task ChangeStatus(AppDbContext _, CancellationToken ct)
        {
            hookFired = true;
            await using var edit = f.Db();
            await edit.AgentTasks.Where(t => t.Id == f.Task.Id).ExecuteUpdateAsync(u =>
                u.SetProperty(t => t.Status, status)
                    .SetProperty(t => t.Result, "saved mirror result")
                    .SetProperty(t => t.FailureReason, "saved mirror reason"), ct);
        }
        await using var bridge = await f.AttachBridgeAsync();
        await f.TickAsync(bridge, ChangeStatus);
        hookFired.ShouldBeTrue();
        await using var verify = f.Db();
        var row = await verify.AgentTasks.SingleAsync(t => t.Id == f.Task.Id);
        row.Status.ShouldBe(status);
        row.Result.ShouldBe("saved mirror result");
        row.FailureReason.ShouldBe("saved mirror reason");
        row.SpecialistSessionId.ShouldBe(f.OriginalSessionId);
        row.SpecialistSessionStartedAt.ShouldBe(f.OriginalStartedAt);
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == row.Id
            && e.Type == AgentTaskEventType.Warning)).ShouldBe(0);
        (await verify.SessionQueuedMessages.CountAsync(m => m.ExecutionTaskId == row.Id)).ShouldBe(0);
        bridge.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task Card0758_changed_claim_token_leaves_the_Check_queued()
    {
        await using var f = await Card0758Fixture.CreateAsync();
        await f.ChangeGenerationAsync("started");
        var token = Guid.NewGuid();
        var hookFired = false;
        async Task ChangeToken(AppDbContext _, CancellationToken ct)
        {
            hookFired = true;
            await using var edit = f.Db();
            await edit.AgentTasks.Where(t => t.Id == f.Task.Id).ExecuteUpdateAsync(u =>
                u.SetProperty(t => t.ConcurrencyToken, token), ct);
        }
        await using var bridge = await f.AttachBridgeAsync();
        await f.TickAsync(bridge, ChangeToken);
        hookFired.ShouldBeTrue();
        await f.AssertQueuedAsync(bridge);
        await using (var verify = f.Db())
            (await verify.AgentTasks.SingleAsync(t => t.Id == f.Task.Id)).ConcurrencyToken.ShouldBe(token);
        await f.TickAsync(bridge);
        await f.AssertAdoptedAsync(bridge, f.Task);
    }

    internal sealed class Card0758Fixture : IAsyncDisposable
    {
        private readonly IsolatedTestSchema _schema;
        private readonly AppDbContext _producerDb;
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromMinutes(6));
        private readonly Task<SpecialistRun> _pending;
        public string ConnectionString => _schema.ConnectionString;
        public DelegationSettings Settings { get; }
        public AgentTask Task { get; }
        public Guid AgentId { get; }
        public Guid OriginalSessionId { get; }
        public DateTime OriginalStartedAt { get; }
        public Guid Profile0 { get; }
        public Guid Profile1 { get; }
        public Guid CurrentSessionId { get; private set; }
        public DateTime CurrentStartedAt { get; private set; }

        private Card0758Fixture(IsolatedTestSchema schema, AppDbContext producerDb,
            CancellationTokenSource stop, Task<SpecialistRun> pending, DelegationSettings settings,
            AgentTask task, Guid agentId, Guid sessionId, DateTime startedAt, Guid p0, Guid p1)
        {
            _schema = schema;
            _producerDb = producerDb;
            _stop = stop;
            _pending = pending;
            Settings = settings;
            Task = task;
            AgentId = agentId;
            OriginalSessionId = CurrentSessionId = sessionId;
            OriginalStartedAt = CurrentStartedAt = startedAt;
            Profile0 = p0;
            Profile1 = p1;
        }

        public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(ConnectionString));

        public static async Task<Card0758Fixture> CreateAsync(bool deadlinePolicy = false, bool typedOwner = false)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var now = DateTime.UtcNow;
            var settings = new DelegationSettings
            {
                CheckInterpreterAgentSlug = $"card0758-{Guid.NewGuid():N}",
                MaxConcurrentTasks = 512,
                PoolIdleRetireMinutes = 525_600,
                PoolMaxIdlePerDirectory = int.MaxValue,
                RolePolicy = new(StringComparer.OrdinalIgnoreCase),
                FinalMessageGraceSeconds = 0,
                SubagentGraceMinutes = 0,
            };
            var profile = new AgentTuiProfile
            {
                Id = Guid.NewGuid(), DisplayName = settings.CheckInterpreterAgentSlug,
                Kind = AgentKind.ClaudeCode, IsEnabled = true, Source = AgentTuiProfileSource.Operator,
                CreatedAt = now, UpdatedAt = now,
            };
            db.AgentTuiProfiles.Add(profile);
            await db.SaveChangesAsync();
            var p0 = new AgentTuiProfileRevision
            {
                Id = Guid.NewGuid(), ProfileId = profile.Id, RevisionNumber = 1,
                Executable = "claude", AuthenticationMode = AgentTuiAuthenticationMode.WrapperManaged,
                CreatedAt = now,
            };
            var p1 = new AgentTuiProfileRevision
            {
                Id = Guid.NewGuid(), ProfileId = profile.Id, RevisionNumber = 2,
                Executable = "claude", AuthenticationMode = AgentTuiAuthenticationMode.WrapperManaged,
                CreatedAt = now.AddSeconds(1),
            };
            db.AgentTuiProfileRevisions.AddRange(p0, p1);
            await db.SaveChangesAsync();
            profile.ActiveRevisionId = p0.Id;
            await db.SaveChangesAsync();
            var session = new AgentSession
            {
                Id = Guid.NewGuid(), AgentKind = AgentKind.ClaudeCode, DefinitionName = "claude",
                Status = SessionStatus.Running, Cwd = Path.GetTempPath(), EffectiveModelId = "sonnet",
                TuiProfileRevisionId = p0.Id, CreatedAt = now.AddMinutes(-2),
                StartedAt = now.AddMinutes(-2), LastSeenAt = now,
            };
            var seat = new Agent
            {
                Id = Guid.NewGuid(), Name = settings.CheckInterpreterAgentSlug,
                Slug = settings.CheckInterpreterAgentSlug, Kind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.High, ModelId = "sonnet", WorkingDirectory = session.Cwd,
                PersistentSessionId = session.Id.ToString("D"), AlwaysOn = true,
                Status = AgentStatus.Running, TuiProfileId = profile.Id,
                CreatedAt = now, UpdatedAt = now,
            };
            if (typedOwner)
            {
                seat.StandingSpecialistRole = AgentTaskRole.Check;
                seat.StandingSpecialistOwnerId = seat.Id;
            }
            db.AgentSessions.Add(session);
            db.Agents.Add(seat);
            await db.SaveChangesAsync();
            await db.Entry(session).ReloadAsync();
            var stop = new CancellationTokenSource(TimeSpan.FromMinutes(6));
            var runner = new SpecialistTaskRunner(db, TimeProvider.System, NullLogger.Instance,
                modelAvailability: new RecordingAvailability());
            var spec = CheckInterpreterProvisioner.Spec(settings);
            var pending = deadlinePolicy
                ? runner.RunWithPolicyAsync(spec, "restart facts", "line one\nline two", 3,
                    _ => System.Threading.Tasks.Task.FromResult<Agent?>(seat),
                    new(DateTimeOffset.UtcNow.AddMinutes(5)), stop.Token)
                : runner.RunAsync(spec, "restart facts", "line one\nline two", TimeSpan.FromMinutes(5), 3,
                    _ => System.Threading.Tasks.Task.FromResult<Agent?>(seat), stop.Token);
            AgentTask? task = null;
            await SpecialistTaskRunnerDeadlineTests.UntilAsync(async () =>
            {
                await using var read = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
                task = await read.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.AgentId == seat.Id);
                return task is not null;
            });
            return new(schema, db, stop, pending, settings, task!, seat.Id, session.Id,
                session.StartedAt, p0.Id, p1.Id);
        }

        public async Task ChangeGenerationAsync(string drift)
        {
            await using var db = Db();
            if (drift == "session")
            {
                var previous = await db.AgentSessions.SingleAsync(s => s.Id == OriginalSessionId);
                previous.Status = SessionStatus.Stopped;
                var replacement = new AgentSession
                {
                    Id = Guid.NewGuid(), AgentKind = AgentKind.ClaudeCode, DefinitionName = "claude",
                    Status = SessionStatus.Running, Cwd = previous.Cwd, EffectiveModelId = "sonnet",
                    TuiProfileRevisionId = Profile0, CreatedAt = DateTime.UtcNow.AddMinutes(-1),
                    StartedAt = DateTime.UtcNow.AddMinutes(-1), LastSeenAt = DateTime.UtcNow,
                };
                db.AgentSessions.Add(replacement);
                (await db.Agents.SingleAsync(a => a.Id == AgentId)).PersistentSessionId = replacement.Id.ToString("D");
                await db.SaveChangesAsync();
                CurrentSessionId = replacement.Id;
            }
            else
            {
                var session = await db.AgentSessions.SingleAsync(s => s.Id == OriginalSessionId);
                session.StartedAt = OriginalStartedAt.AddMinutes(1);
                if (drift is "profile" or "profile-only") session.TuiProfileRevisionId = Profile1;
                if (drift == "profile-only") session.StartedAt = OriginalStartedAt;
                await db.SaveChangesAsync();
            }
            CurrentStartedAt = (await db.AgentSessions.AsNoTracking()
                .SingleAsync(s => s.Id == CurrentSessionId)).StartedAt;
        }

        public Task<BridgeQueueHarness> AttachBridgeAsync() => BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = ConnectionString, AttachAgentId = AgentId,
            AttachSessionId = CurrentSessionId, Delegation = Settings,
            ConfigureDeliveryVerification = v =>
            {
                v.TranscriptConfirmTimeoutSeconds = 1;
                v.PostFailureConfirmGraceSeconds = 0;
                v.PostEvidenceSettleMs = 0;
            },
            ConfigureServices = services => services.AddSingleton(sp => SyntheticModernProfile(sp, Settings)),
        });

        // These fixtures use an attached fake adapter: they certify the queue/receipt path, not a
        // host pseudoconsole. Linux cannot load the Windows redistributable that gates production
        // modern ceilings, so set only this fake profile's private cached verdict for the test.
        internal static PtyDeliveryProfile SyntheticModernProfile(IServiceProvider sp, DelegationSettings settings)
        {
            var profile = new PtyDeliveryProfile(sp.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<PtyDeliveryProfile>.Instance, Options.Create(settings), backendOverride: "modern");
            typeof(PtyDeliveryProfile).GetField("_ceilings",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(profile, settings.CeilingsFor(PtyBackend.ModernConPty, "synthetic attached adapter"));
            return profile;
        }

        public (AgentTaskDispatcher Dispatcher, ServiceProvider Provider) Dispatcher(BridgeQueueHarness bridge) =>
            AgentTaskStandingAgentDispatchTests.CreateHarness(connectionString: ConnectionString,
                bridge: bridge, delegation: Settings);

        public async Task TickAsync(BridgeQueueHarness bridge,
            Func<AppDbContext, CancellationToken, Task>? afterSnapshot = null,
            Func<Guid, string, CancellationToken, Task>? enqueueOverride = null)
        {
            var (dispatcher, provider) = Dispatcher(bridge);
            using (provider)
            {
                dispatcher.AfterQueuedSnapshotAsync = afterSnapshot;
                dispatcher.ReuseEnqueueOverride = enqueueOverride;
                await dispatcher.TickAsync(CancellationToken.None);
            }
        }

        public async Task AssertQueuedAsync(BridgeQueueHarness bridge)
        {
            await using var db = Db();
            var row = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == Task.Id);
            row.Status.ShouldBe(AgentTaskStatus.Queued);
            row.AgentSessionId.ShouldBeNull();
            row.SpecialistSessionId.ShouldBe(OriginalSessionId);
            row.SpecialistSessionStartedAt.ShouldBe(OriginalStartedAt);
            row.SpecialistProfileRevisionId.ShouldBe(Task.SpecialistProfileRevisionId);
            (await db.SessionQueuedMessages.CountAsync(m => m.ExecutionTaskId == Task.Id)).ShouldBe(0);
            (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == Task.Id
                && e.Type == AgentTaskEventType.Warning)).ShouldBe(0);
            bridge.Adapter.Inputs.ShouldBeEmpty();
            bridge.Adapter.SubmittedBodies.ShouldBeEmpty();
        }

        public async Task AssertAdoptedAsync(BridgeQueueHarness bridge, AgentTask original)
        {
            await using var db = Db();
            var row = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == Task.Id);
            row.Status.ShouldBe(AgentTaskStatus.Dispatched, row.FailureReason);
            row.FailureCode.ShouldBeNull();
            row.AgentId.ShouldBe(AgentId);
            row.AgentSessionId.ShouldBe(CurrentSessionId);
            row.SpecialistSessionId.ShouldBe(CurrentSessionId);
            row.SpecialistSessionStartedAt.ShouldBe(CurrentStartedAt);
            row.SpecialistProfileRevisionId.ShouldBe(Profile0);
            row.SpecialistModelAlias.ShouldBe(original.SpecialistModelAlias);
            row.SpecialistModelId.ShouldBe(original.SpecialistModelId);
            row.SpecialistEffectiveModelId.ShouldBe(original.SpecialistEffectiveModelId);
            row.ExecutionDeadlineAt.ShouldBe(original.ExecutionDeadlineAt);
            row.Goal.ShouldBe(original.Goal);
            row.SpecialistInputPolicyJson.ShouldBeNull();
            var events = await db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == Task.Id).ToListAsync();
            events.Count(e => e.Type == AgentTaskEventType.Dispatched).ShouldBe(1);
            var warnings = events.Where(e => e.Type == AgentTaskEventType.Warning).ToList();
            warnings.Count.ShouldBe(1);
            warnings[0].Detail.ShouldContain(OriginalSessionId.ToString("D"));
            warnings[0].Detail.ShouldContain(CurrentSessionId.ToString("D"));
            warnings[0].Detail.ShouldContain(OriginalStartedAt.ToString("O"));
            warnings[0].Detail.ShouldContain(CurrentStartedAt.ToString("O"));
            var queued = await db.SessionQueuedMessages.AsNoTracking()
                .Where(m => m.ExecutionTaskId == Task.Id).ToListAsync();
            queued.Count.ShouldBe(1);
            queued[0].AgentSessionId.ShouldBe(CurrentSessionId);
            var expected = DelegationReportFormatter.BuildBrief(row, Settings,
                Settings.ModernPtyReplyInlineMaxChars).ReplaceLineEndings("\n").Trim();
            queued[0].Body.ShouldBe(expected);
            queued[0].DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
            bridge.Adapter.SubmittedBodies.ShouldBe([expected]);
            bridge.Adapter.Inputs.ShouldBe(["\u001b[200~" + expected + "\u001b[201~", "\r"]);
            (await db.TranscriptEntries.CountAsync(e => e.AgentSessionId == CurrentSessionId
                && e.Kind == TranscriptKinds.UserPrompt && e.Text == expected)).ShouldBe(1);
        }

        public async Task AssertRefusedAsync(BridgeQueueHarness bridge)
        {
            await using var db = Db();
            var row = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == Task.Id);
            row.Status.ShouldBe(AgentTaskStatus.Failed);
            row.FailureCode.ShouldBe(AgentTaskFailureCode.SpecialistIdentityMismatch);
            row.AgentSessionId.ShouldBeNull();
            row.SpecialistSessionId.ShouldBe(OriginalSessionId);
            row.SpecialistSessionStartedAt.ShouldBe(OriginalStartedAt);
            row.SpecialistProfileRevisionId.ShouldBe(Task.SpecialistProfileRevisionId);
            (await db.SessionQueuedMessages.CountAsync(m => m.ExecutionTaskId == Task.Id)).ShouldBe(0);
            (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == Task.Id
                && e.Type == AgentTaskEventType.Warning)).ShouldBe(0);
            bridge.Adapter.Inputs.ShouldBeEmpty();
            bridge.Adapter.SubmittedBodies.ShouldBeEmpty();
            var result = await _pending;
            result.Outcome.ShouldBe(SpecialistRunOutcome.IdentityMismatch);
            result.Reason.ShouldContain(Settings.CheckInterpreterAgentSlug);
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            try { await _pending; } catch (OperationCanceledException) { }
            _stop.Dispose();
            await _producerDb.DisposeAsync();
            await _schema.DisposeAsync();
        }
    }

    [Test]
    public Task Card0415_V01_non_low_exact_model() =>
        Card0415_V01_execution_snapshot_and_dispatch_drift(AgentKind.Codex, true, "none");

    [Test]
    [Arguments("started")]
    [Arguments("public-kind")]
    public Task Card0415_V01_generation_and_public_pin_guards(string drift) =>
        Card0415_V01_execution_snapshot_and_dispatch_drift(AgentKind.Codex, false, drift);

    [Test]
    [Arguments(AgentKind.ClaudeCode, false, "none")]
    [Arguments(AgentKind.ClaudeCode, true, "none")]
    [Arguments(AgentKind.Codex, false, "none")]
    [Arguments(AgentKind.Codex, true, "none")]
    [Arguments(AgentKind.Codex, false, "model")]
    [Arguments(AgentKind.Codex, true, "tier")]
    [Arguments(AgentKind.Codex, false, "started")]
    [Arguments(AgentKind.Codex, true, "session")]
    [Arguments(AgentKind.Codex, false, "kind")]
    [Arguments(AgentKind.Codex, true, "live-model")]
    [Arguments(AgentKind.Codex, false, "public-kind")]
    public async Task Card0415_V01_execution_snapshot_and_dispatch_drift(
        AgentKind kind, bool deadlinePolicy, string drift)
    {
        await using var database = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database.ConnectionString).Options;
        await using var db = new AppDbContext(options);
        var now = DateTime.UtcNow;
        var exact = kind == AgentKind.Codex ? "gpt-5.6-terra" : "sonnet";
        var session = new AgentSession
        {
            Id = Guid.NewGuid(), AgentKind = kind, DefinitionName = kind == AgentKind.Codex ? "codex" : "claude",
            Status = SessionStatus.Running, Cwd = Path.GetTempPath(), EffectiveModelId = exact,
            CreatedAt = now, StartedAt = now, LastSeenAt = now,
        };
        var seat = new Agent
        {
            Id = Guid.NewGuid(), Name = "snapshot-seat", Slug = "snapshot-seat", Kind = kind,
            ModelLevel = AgentModelLevel.High, ModelId = exact, WorkingDirectory = session.Cwd,
            PersistentSessionId = session.Id.ToString("D"), AlwaysOn = true,
            Status = AgentStatus.Running, CreatedAt = now, UpdatedAt = now,
        };
        db.AgentSessions.Add(session);
        db.Agents.Add(seat);
        await db.SaveChangesAsync();
        // Use the database's timestamp precision as the selection evidence.
        await db.Entry(session).ReloadAsync();
        var availability = new RecordingAvailability();
        var runner = new SpecialistTaskRunner(db, TimeProvider.System, NullLogger.Instance,
            modelAvailability: availability);
        var spec = CheckInterpreterProvisioner.Spec(new DelegationSettings()) with { Slug = seat.Slug };
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var pending = deadlinePolicy
            ? runner.RunWithPolicyAsync(spec, "snapshot", "synthetic facts", 3,
                _ => Task.FromResult<Agent?>(seat), new(DateTimeOffset.UtcNow.AddSeconds(60)), stop.Token)
            : runner.RunAsync(spec, "snapshot", "synthetic facts", TimeSpan.FromSeconds(60), 3,
                _ => Task.FromResult<Agent?>(seat), stop.Token);
        try
        {
            AgentTask? task = null;
            await SpecialistTaskRunnerDeadlineTests.UntilAsync(async () =>
            {
                await using var read = new AppDbContext(options);
                task = await read.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.AgentId == seat.Id);
                return task is not null;
            });
            task!.AgentKind.ShouldBe(kind);
            task.ModelLevel.ShouldBe(AgentModelLevel.High);
            task.SpecialistModelAlias.ShouldBe(exact);
            task.SpecialistModelId.ShouldBe(exact);
            task.SpecialistSessionId.ShouldBe(session.Id);
            task.SpecialistSessionStartedAt.ShouldBe(session.StartedAt);
            await using (var edit = new AppDbContext(options))
            {
                (await edit.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == task.Id)).ModelLevel
                    .ShouldBe(AgentModelLevel.High);
                if (drift == "model") await edit.Agents.Where(a => a.Id == seat.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.ModelId, "gpt-5.6-sol"));
                if (drift == "tier") await edit.Agents.Where(a => a.Id == seat.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.ModelLevel, AgentModelLevel.Low));
                if (drift == "kind") await edit.AgentSessions.Where(s => s.Id == session.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.AgentKind, AgentKind.ClaudeCode));
                if (drift == "started") await edit.AgentSessions.Where(s => s.Id == session.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.StartedAt, session.StartedAt.AddSeconds(1)));
                if (drift == "live-model") await edit.AgentSessions.Where(s => s.Id == session.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.EffectiveModelId, "gpt-5.6-sol"));
                if (drift == "public-kind") await edit.AgentTasks.Where(t => t.Id == task.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.AgentKind, AgentKind.ClaudeCode)
                        .SetProperty(t => t.SpecialistModelAlias, (string?)null));
                if (drift == "session")
                {
                    var replacement = new AgentSession
                    {
                        Id = Guid.NewGuid(), AgentKind = kind, DefinitionName = "codex", EffectiveModelId = exact,
                        Status = SessionStatus.Running, Cwd = session.Cwd, StartedAt = session.StartedAt,
                        CreatedAt = now, LastSeenAt = now,
                    };
                    edit.AgentSessions.Add(replacement);
                    await edit.SaveChangesAsync();
                    await edit.Agents.Where(a => a.Id == seat.Id).ExecuteUpdateAsync(s =>
                        s.SetProperty(a => a.PersistentSessionId, replacement.Id.ToString("D")));
                }
            }
            if (deadlinePolicy)
                availability.Calls.ShouldContain(c => c.Kind == kind && c.Alias == exact);
            var (dispatcher, provider) = AgentTaskStandingAgentDispatchTests.CreateHarness(connectionString: database.ConnectionString);
            using (provider) await dispatcher.TickAsync(stop.Token);
            await using var verify = new AppDbContext(options);
            var result = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
            result.AgentKind.ShouldBe(drift == "public-kind" ? AgentKind.ClaudeCode : kind);
            result.ModelLevel.ShouldBe(AgentModelLevel.High);
            if (drift == "none")
            {
                result.Status.ShouldBe(AgentTaskStatus.Dispatched, result.FailureReason);
                result.AgentSessionId.ShouldBe(session.Id);
            }
            else
            {
                result.Status.ShouldBe(AgentTaskStatus.Failed);
                result.FailureCode.ShouldBe(AgentTaskFailureCode.SpecialistIdentityMismatch);
                (await verify.SessionQueuedMessages.CountAsync()).ShouldBe(0);
                var failure = await pending;
                failure.Outcome.ShouldBe(SpecialistRunOutcome.IdentityMismatch);
                failure.Reason.ShouldNotBeNullOrWhiteSpace();
                failure.Reason.ShouldContain("snapshot-seat", Case.Insensitive);
            }
        }
        finally
        {
            stop.Cancel();
            try { await pending; } catch (OperationCanceledException) { }
        }
    }

    private sealed class RecordingAvailability : IModelAvailability
    {
        public List<(AgentKind Kind, string Alias)> Calls { get; } = [];
        public Task<bool> IsHeldAsync(AgentKind kind, string alias, CancellationToken ct)
        {
            Calls.Add((kind, alias));
            return Task.FromResult(false);
        }
    }

    [Test]
    public async Task Card0415_V01_legacy_Codex_producer_reaches_the_live_standing_session()
    {
        await using var database = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database.ConnectionString).Options;
        await using var db = new AppDbContext(options);
        var now = DateTime.UtcNow;
        var session = new AgentSession
        {
            Id = Guid.NewGuid(), AgentKind = AgentKind.Codex, DefinitionName = "codex",
            Status = SessionStatus.Running, Cwd = Path.GetTempPath(),
            CreatedAt = now, StartedAt = now, LastSeenAt = now,
        };
        var seat = new Agent
        {
            Id = Guid.NewGuid(), Name = "identity-canary", Slug = "identity-canary",
            Kind = AgentKind.Codex, ModelLevel = AgentModelLevel.Low,
            WorkingDirectory = session.Cwd, PersistentSessionId = session.Id.ToString("D"),
            AlwaysOn = true, Status = AgentStatus.Running, CreatedAt = now, UpdatedAt = now,
        };
        db.AgentSessions.Add(session);
        db.Agents.Add(seat);
        await db.SaveChangesAsync();
        var runner = new SpecialistTaskRunner(db, TimeProvider.System, NullLogger.Instance);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var spec = CheckInterpreterProvisioner.Spec(new DelegationSettings());
        var pending = runner.RunAsync(spec, "identity reproduction", "synthetic facts",
            TimeSpan.FromSeconds(60), 3, _ => Task.FromResult<Agent?>(seat), stop.Token);
        try
        {
            AgentTask? task = null;
            await SpecialistTaskRunnerDeadlineTests.UntilAsync(async () =>
            {
                await using var read = new AppDbContext(options);
                task = await read.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.AgentId == seat.Id);
                return task is not null;
            });
            var (dispatcher, provider) = AgentTaskStandingAgentDispatchTests.CreateHarness(connectionString: database.ConnectionString);
            using (provider) await dispatcher.TickAsync(stop.Token);
            await using var verify = new AppDbContext(options);
            var result = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task!.Id);
            result.Status.ShouldBe(AgentTaskStatus.Dispatched, result.FailureReason);
            result.AgentKind.ShouldBe(AgentKind.Codex);
            result.AgentSessionId.ShouldBe(session.Id);
            (await verify.SessionQueuedMessages.Where(m => m.AgentSessionId == session.Id).ToListAsync())
                .ShouldContain(m => m.Body.Contains(DelegationReportFormatter.TaskMarker(result.Id)));
            (await verify.AgentSessions.CountAsync()).ShouldBe(1, "standing dispatch must not launch another session");
        }
        finally
        {
            stop.Cancel();
            try { await pending; } catch (OperationCanceledException) { }
        }
    }
}
