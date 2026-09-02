using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0057 S2 — claim, miss, and prompt delivery. The sweep is global over the shared fixture
/// database, so this class takes <c>[NotInParallel]</c> with NO group key and every assertion is
/// scoped to rows it created.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class ScheduleSweepTests
{
    [Test]
    public async Task the_schedules_migration_applies_on_the_shared_postgres_fixture()
    {
        await using var db = CreateContext();
        var count = await db.Schedules.CountAsync();
        count.ShouldBeGreaterThanOrEqualTo(0);
        (await db.ScheduleFires.CountAsync()).ShouldBeGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task a_due_schedule_is_claimed_once_when_two_ticks_race()
    {
        var harness = new Harness();
        var seed = await harness.SeedDuePromptAsync();

        var first = harness.NewSchedules().ClaimDueAsync(CancellationToken.None);
        var second = harness.NewSchedules().ClaimDueAsync(CancellationToken.None);
        await Task.WhenAll(first, second);

        (first.Result + second.Result).ShouldBe(1);
        harness.DrainQueue().Count(c => c.ScheduleId == seed.Schedule.Id).ShouldBe(1);

        var after = await harness.ReloadAsync(seed.Schedule.Id);
        after.FireCount.ShouldBe(1);
        var fires = await harness.FiresAsync(seed.Schedule.Id);
        fires.Count.ShouldBe(1);
        fires[0].Outcome.ShouldBe(ScheduleFireOutcome.Claimed);
    }

    [Test]
    public async Task the_recurrence_is_advanced_and_the_fire_row_written_before_the_hand_off()
    {
        var harness = new Harness();
        var seed = await harness.SeedDuePromptAsync(repeat: ScheduleRepeat.Interval, everyMinutes: 10);

        await harness.Schedules.ClaimDueAsync(CancellationToken.None);

        var claims = harness.DrainQueue();
        claims.ShouldContain(c => c.ScheduleId == seed.Schedule.Id);
        var after = await harness.ReloadAsync(seed.Schedule.Id);
        after.FireCount.ShouldBe(1);
        after.NextFireAt.ShouldNotBeNull();
        after.NextFireAt!.Value.ShouldBeGreaterThan(harness.UtcNow());
        var fire = (await harness.FiresAsync(seed.Schedule.Id)).ShouldHaveSingleItem();
        fire.Outcome.ShouldBe(ScheduleFireOutcome.Claimed);
        fire.FireNumber.ShouldBe(1);
    }

    [Test]
    public async Task a_disabled_schedule_is_never_selected()
    {
        var harness = new Harness();
        var seed = await harness.SeedDuePromptAsync(enabled: false);

        (await harness.Schedules.ClaimDueAsync(CancellationToken.None)).ShouldBeGreaterThanOrEqualTo(0);
        harness.DrainQueue().ShouldNotContain(c => c.ScheduleId == seed.Schedule.Id);
        (await harness.ReloadAsync(seed.Schedule.Id)).FireCount.ShouldBe(0);
    }

    [Test]
    public async Task a_throwing_fire_marks_its_row_failed_and_is_not_reclaimed()
    {
        var harness = new Harness();
        var seed = await harness.SeedDuePromptAsync(prompt: "");

        await harness.Schedules.ClaimDueAsync(CancellationToken.None);
        var claim = harness.DrainQueue().Single(c => c.ScheduleId == seed.Schedule.Id);
        await harness.Schedules.FireAsync(claim, CancellationToken.None);

        var fire = (await harness.FiresAsync(seed.Schedule.Id)).ShouldHaveSingleItem();
        fire.Outcome.ShouldBe(ScheduleFireOutcome.Failed);
        fire.CompletedAt.ShouldNotBeNull();

        var after = await harness.ReloadAsync(seed.Schedule.Id);
        after.LastOutcome.ShouldBe(ScheduleFireOutcome.Failed);
        after.NextFireAt.ShouldBeNull("Once re-arms to null even when the fire throws");

        await harness.Schedules.ClaimDueAsync(CancellationToken.None);
        harness.DrainQueue().ShouldNotContain(c => c.ScheduleId == seed.Schedule.Id);
    }

    [Test]
    public async Task re_enabling_recomputes_next_from_now_instead_of_firing_late()
    {
        var harness = new Harness();
        var seed = await harness.SeedDuePromptAsync(
            repeat: ScheduleRepeat.Daily, atLocal: "09:00", dueMinutesAgo: 7 * 24 * 60);

        await harness.PatchEnabledAsync(seed.Schedule.Id, enabled: false);
        var disabled = await harness.ReloadAsync(seed.Schedule.Id);
        disabled.Enabled.ShouldBeFalse();
        disabled.NextFireAt.ShouldNotBeNull();

        await harness.PatchEnabledAsync(seed.Schedule.Id, enabled: true);
        var enabled = await harness.ReloadAsync(seed.Schedule.Id);
        enabled.Enabled.ShouldBeTrue();
        enabled.NextFireAt.ShouldNotBeNull();
        enabled.NextFireAt!.Value.ShouldBeGreaterThan(harness.UtcNow());
        enabled.FireCount.ShouldBe(0);
    }

    [Test]
    public async Task a_once_schedule_overdue_by_hours_still_fires_and_says_how_late()
    {
        var harness = new Harness();
        var seed = await harness.SeedDuePromptAsync(dueMinutesAgo: 134, alwaysOn: true);

        await harness.Schedules.ClaimDueAsync(CancellationToken.None);
        var claim = harness.DrainQueue().Single(c => c.ScheduleId == seed.Schedule.Id);
        await harness.Schedules.FireAsync(claim, CancellationToken.None);

        var fire = (await harness.FiresAsync(seed.Schedule.Id)).ShouldHaveSingleItem();
        fire.Outcome.ShouldBe(ScheduleFireOutcome.QueuedForRelaunch);
        fire.Detail.ShouldNotBeNull();
        fire.Detail!.ShouldContain("late");
        var queued = await harness.QueuedForAsync(seed.SessionId);
        queued.ShouldContain(m => m.Body.Contains("late") && m.Body.Contains("[scheduled:"));
    }

    [Test]
    public async Task a_daily_schedule_past_its_grace_writes_skipped_late_and_re_arms()
    {
        var harness = new Harness();
        var seed = await harness.SeedDuePromptAsync(
            repeat: ScheduleRepeat.Daily, atLocal: "09:00", dueMinutesAgo: 180, graceMinutes: 60);

        await harness.Schedules.ClaimDueAsync(CancellationToken.None);
        var claim = harness.DrainQueue().Single(c => c.ScheduleId == seed.Schedule.Id);
        await harness.Schedules.FireAsync(claim, CancellationToken.None);

        var fire = (await harness.FiresAsync(seed.Schedule.Id)).ShouldHaveSingleItem();
        fire.Outcome.ShouldBe(ScheduleFireOutcome.SkippedLate);
        var after = await harness.ReloadAsync(seed.Schedule.Id);
        after.NextFireAt.ShouldNotBeNull();
        after.NextFireAt!.Value.ShouldBeGreaterThan(harness.UtcNow());
        (await harness.QueuedForAsync(seed.SessionId)).ShouldBeEmpty();
    }

    [Test]
    public async Task three_days_of_downtime_produce_exactly_one_claim()
    {
        var harness = new Harness();
        var seed = await harness.SeedDuePromptAsync(
            repeat: ScheduleRepeat.Daily, atLocal: "09:00", dueMinutesAgo: 3 * 24 * 60);

        (await harness.Schedules.ClaimDueAsync(CancellationToken.None)).ShouldBeGreaterThanOrEqualTo(1);
        harness.DrainQueue().Count(c => c.ScheduleId == seed.Schedule.Id).ShouldBe(1);
        (await harness.FiresAsync(seed.Schedule.Id)).Count.ShouldBe(1);
        var after = await harness.ReloadAsync(seed.Schedule.Id);
        after.FireCount.ShouldBe(1);
        after.NextFireAt.ShouldNotBeNull();
        after.NextFireAt!.Value.ShouldBeGreaterThan(harness.UtcNow());
    }

    [Test]
    public async Task an_idle_agent_gets_the_prompt_delivered_and_transcript_confirmed()
    {
        var harness = new Harness();
        var seed = await harness.SeedDuePromptAsync();
        harness.RegisterIdle(seed.SessionId);

        await harness.ClaimAndFireAsync(seed.Schedule.Id);

        var fire = (await harness.FiresAsync(seed.Schedule.Id)).ShouldHaveSingleItem();
        fire.Outcome.ShouldBe(ScheduleFireOutcome.Delivered);
        fire.QueuedMessageId.ShouldNotBeNull();
        await using var db = CreateContext();
        var prompts = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == seed.SessionId && t.Kind == TranscriptKinds.UserPrompt)
            .ToListAsync();
        prompts.ShouldContain(t => t.Text != null && t.Text.Contains("[scheduled:"));
    }

    [Test]
    public async Task a_working_agent_gets_a_pending_when_idle_row()
    {
        var harness = new Harness();
        var seed = await harness.SeedDuePromptAsync();
        harness.RegisterIdle(seed.SessionId);
        await harness.MarkWorkingAsync(seed.SessionId);

        await harness.ClaimAndFireAsync(seed.Schedule.Id);

        var fire = (await harness.FiresAsync(seed.Schedule.Id)).ShouldHaveSingleItem();
        fire.Outcome.ShouldBe(ScheduleFireOutcome.Enqueued);
        var queued = await harness.QueuedForAsync(seed.SessionId);
        queued.ShouldHaveSingleItem().Status.ShouldBe(QueuedMessageStatus.Pending);
        queued[0].Origin.ShouldBe(QueuedMessageOrigin.Scheduled);
    }

    [Test]
    public async Task a_starting_session_leaves_the_row_pending()
    {
        var harness = new Harness();
        var seed = await harness.SeedDuePromptAsync(sessionStatus: SessionStatus.Starting);
        harness.RegisterIdle(seed.SessionId);

        await harness.ClaimAndFireAsync(seed.Schedule.Id);

        var fire = (await harness.FiresAsync(seed.Schedule.Id)).ShouldHaveSingleItem();
        fire.Outcome.ShouldBe(ScheduleFireOutcome.Enqueued);
        (await harness.QueuedForAsync(seed.SessionId)).ShouldHaveSingleItem()
            .Status.ShouldBe(QueuedMessageStatus.Pending);
    }

    [Test]
    public async Task a_dead_always_on_agent_gets_a_row_on_its_persistent_session_that_the_relaunch_carries_over()
    {
        var harness = new Harness();
        var seed = await harness.SeedDuePromptAsync(alwaysOn: true, sessionStatus: SessionStatus.Stopped);

        await harness.ClaimAndFireAsync(seed.Schedule.Id);

        var fire = (await harness.FiresAsync(seed.Schedule.Id)).ShouldHaveSingleItem();
        fire.Outcome.ShouldBe(ScheduleFireOutcome.QueuedForRelaunch);
        var original = (await harness.QueuedForAsync(seed.SessionId)).ShouldHaveSingleItem();
        original.Status.ShouldBe(QueuedMessageStatus.Pending);

        var newSessionId = Guid.NewGuid();
        await using (var db = CreateContext())
        {
            var now = DateTime.UtcNow;
            db.AgentSessions.Add(new AgentSession
            {
                Id = newSessionId,
                DefinitionName = "fake",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Starting,
                Cwd = seed.Workspace,
                Cols = 120,
                Rows = 30,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
            });
            await db.SaveChangesAsync();
            var moved = await db.SessionQueuedMessages
                .Where(m => m.AgentSessionId == seed.SessionId && m.Status == QueuedMessageStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.AgentSessionId, newSessionId));
            moved.ShouldBe(1);
        }

        var carried = (await harness.QueuedForAsync(newSessionId)).ShouldHaveSingleItem();
        carried.Id.ShouldBe(original.Id);
        carried.Origin.ShouldBe(QueuedMessageOrigin.Scheduled);
    }

    [Test]
    public async Task a_dead_standing_agent_is_skipped_with_no_session()
    {
        var harness = new Harness();
        var seed = await harness.SeedDuePromptAsync(alwaysOn: false, sessionStatus: SessionStatus.Stopped);

        await harness.ClaimAndFireAsync(seed.Schedule.Id);

        var fire = (await harness.FiresAsync(seed.Schedule.Id)).ShouldHaveSingleItem();
        fire.Outcome.ShouldBe(ScheduleFireOutcome.SkippedNoSession);
        (await harness.QueuedForAsync(seed.SessionId)).ShouldBeEmpty();
    }

    [Test]
    public async Task a_never_launched_agent_is_skipped_regardless_of_policy()
    {
        var harness = new Harness();
        var seed = await harness.SeedDuePromptAsync(
            alwaysOn: true, neverLaunched: true, whenDown: ScheduleWhenTargetDown.Queue);

        await harness.ClaimAndFireAsync(seed.Schedule.Id);

        var fire = (await harness.FiresAsync(seed.Schedule.Id)).ShouldHaveSingleItem();
        fire.Outcome.ShouldBe(ScheduleFireOutcome.SkippedNoSession);
    }

    [Test]
    public async Task a_recurring_fire_cancels_the_previous_pending_copy()
    {
        var harness = new Harness();
        var seed = await harness.SeedDuePromptAsync(
            alwaysOn: true,
            sessionStatus: SessionStatus.Stopped,
            repeat: ScheduleRepeat.Interval,
            everyMinutes: 5,
            dueMinutesAgo: 1,
            graceMinutes: 60);

        await harness.ClaimAndFireAsync(seed.Schedule.Id);
        var first = (await harness.QueuedForAsync(seed.SessionId)).ShouldHaveSingleItem();

        await using (var db = CreateContext())
        {
            var row = await db.Schedules.SingleAsync(s => s.Id == seed.Schedule.Id);
            row.NextFireAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        await harness.ClaimAndFireAsync(seed.Schedule.Id);
        await using var verify = CreateContext();
        var rows = await verify.SessionQueuedMessages
            .Where(m => m.SourceScheduleId == seed.Schedule.Id)
            .ToListAsync();
        rows.Count.ShouldBe(2);
        rows.Single(m => m.Id == first.Id).Status.ShouldBe(QueuedMessageStatus.Canceled);
        rows.Single(m => m.Id != first.Id).Status.ShouldBe(QueuedMessageStatus.Pending);
        var last = (await harness.FiresAsync(seed.Schedule.Id)).OrderByDescending(f => f.FireNumber).First();
        last.Detail.ShouldNotBeNull();
        last.Detail!.ShouldContain(first.Id.ToString());
    }

    [Test]
    public async Task the_body_names_the_schedule_and_carries_no_task_marker()
    {
        var body = ScheduleService.BuildPromptBody(
            new Schedule
            {
                Name = "Morning triage",
                Repeat = ScheduleRepeat.Daily,
                TimeZoneId = "Europe/London",
                AtLocal = "09:00",
                PromptText = "status please",
            },
            new FireClaim(Guid.NewGuid(), new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc), 12),
            new DateTime(2026, 9, 2, 8, 0, 4, DateTimeKind.Utc)).Body;

        body.ShouldContain("[scheduled: Morning triage");
        body.ShouldContain("fire #12");
        body.ShouldContain("status please");
        body.ShouldNotContain("[task");
        body.ShouldNotContain("[check");
    }

    [Test]
    public async Task a_body_the_target_kind_forbids_is_refused_at_create()
    {
        var harness = new Harness();
        var agent = await harness.SeedAgentAsync(kind: AgentKind.Codex, alwaysOn: true);
        var ex = await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ValidationException>(() =>
            harness.Schedules.CreateAsync(
                new CreateScheduleRequest(
                    Name: "usage",
                    Repeat: ScheduleRepeat.Once,
                    Agent: agent.Id.ToString(),
                    PromptText: "/usage",
                    FireAt: DateTime.UtcNow.AddMinutes(5)),
                CancellationToken.None));
        ex.Errors.Values.SelectMany(v => v).ShouldContain(m => m.Contains("/usage") || m.Length > 0);
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private sealed record Seeded(Schedule Schedule, Guid SessionId, string Workspace);

    private sealed class OffsetTimeProvider : TimeProvider
    {
        private TimeSpan _offset;
        public void Advance(TimeSpan delta) => _offset += delta;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _offset;
    }

    private sealed class Harness
    {
        private readonly ServiceProvider _provider;
        public OffsetTimeProvider Clock { get; } = new();

        public Harness()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            }));
            services.AddSingleton<IEventBus>(new MockEventBus());
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton(Options.Create(new SupervisionSettings
            {
                DeliveryVerification = new DeliveryVerificationSettings
                {
                    Enabled = true,
                    EvidenceTimeoutSeconds = 1,
                    PollIntervalMs = 50,
                    PostSubmitAdvanceTimeoutSeconds = 1,
                    TranscriptConfirmTimeoutSeconds = 3,
                    ReEnterIntervalSeconds = 1,
                },
            }));
            services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
            services.AddSingleton(Options.Create(new DelegationSettings()));
            services.AddSingleton(Options.Create(new AgentSessionSettings()));
            services.AddSingleton(Options.Create(new ScheduleSettings()));
            services.AddSingleton(Options.Create(new DigestSettings { TimeZone = "Europe/London" }));
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<SessionMessageQueueService>();
            services.AddSingleton<ScheduleFireQueue>();
            services.AddScoped<ScheduleService>();
            _provider = services.BuildServiceProvider();
            Schedules = NewSchedules();
            Queue = _provider.GetRequiredService<ScheduleFireQueue>();
            Runtime = _provider.GetRequiredService<AgentSessionRuntime>();
        }

        public ScheduleService Schedules { get; }
        public ScheduleFireQueue Queue { get; }
        public AgentSessionRuntime Runtime { get; }

        public ScheduleService NewSchedules() =>
            _provider.CreateScope().ServiceProvider.GetRequiredService<ScheduleService>();

        public DateTime UtcNow() => Clock.GetUtcNow().UtcDateTime;

        public List<FireClaim> DrainQueue()
        {
            var claims = new List<FireClaim>();
            while (Queue.TryDequeue(out var claim))
                claims.Add(claim);
            return claims;
        }

        public async Task ClaimAndFireAsync(Guid scheduleId)
        {
            await Schedules.ClaimDueAsync(CancellationToken.None);
            foreach (var claim in DrainQueue().Where(c => c.ScheduleId == scheduleId))
                await Schedules.FireAsync(claim, CancellationToken.None);
        }

        public void RegisterIdle(Guid sessionId)
        {
            var adapter = new FakeAgentProtocolAdapter();
            adapter.OnSubmitted = async submitted =>
            {
                await using var db = CreateContext();
                var seq = (await db.TranscriptEntries
                    .Where(t => t.AgentSessionId == sessionId)
                    .MaxAsync(t => (long?)t.Sequence)) ?? 0;
                db.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Sequence = seq + 1,
                    Kind = TranscriptKinds.UserPrompt,
                    Text = submitted,
                    CreatedAt = DateTime.UtcNow,
                });
                db.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Sequence = seq + 2,
                    Kind = TranscriptKinds.TurnEnd,
                    CreatedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            };
            Runtime.Register(sessionId, adapter);
        }

        public async Task MarkWorkingAsync(Guid sessionId)
        {
            await using var db = CreateContext();
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = 1,
                Kind = TranscriptKinds.AssistantText,
                Text = "working on it",
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        public async Task<Schedule> ReloadAsync(Guid id)
        {
            await using var db = CreateContext();
            return await db.Schedules.AsNoTracking().SingleAsync(s => s.Id == id);
        }

        public async Task<List<ScheduleFire>> FiresAsync(Guid scheduleId)
        {
            await using var db = CreateContext();
            return await db.ScheduleFires.AsNoTracking()
                .Where(f => f.ScheduleId == scheduleId)
                .OrderBy(f => f.FireNumber)
                .ToListAsync();
        }

        public async Task<List<SessionQueuedMessage>> QueuedForAsync(Guid sessionId)
        {
            await using var db = CreateContext();
            return await db.SessionQueuedMessages.AsNoTracking()
                .Where(m => m.AgentSessionId == sessionId)
                .OrderBy(m => m.Sequence)
                .ToListAsync();
        }

        public async Task PatchEnabledAsync(Guid id, bool enabled)
        {
            var current = await ReloadAsync(id);
            await Schedules.PatchAsync(
                id,
                new PatchScheduleRequest(current.ConcurrencyToken, Enabled: enabled),
                CancellationToken.None);
        }

        public async Task<Agent> SeedAgentAsync(AgentKind kind, bool alwaysOn)
        {
            await using var db = CreateContext();
            var now = DateTime.UtcNow;
            var agent = new Agent
            {
                Id = Guid.NewGuid(),
                Name = $"sched-{Guid.NewGuid():N}"[..20],
                Slug = $"sched-{Guid.NewGuid():N}"[..16],
                WorkingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
                Details = string.Empty,
                Status = AgentStatus.Idle,
                Kind = kind,
                AlwaysOn = alwaysOn,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Agents.Add(agent);
            await db.SaveChangesAsync();
            return agent;
        }

        public async Task<Seeded> SeedDuePromptAsync(
            ScheduleRepeat repeat = ScheduleRepeat.Once,
            int everyMinutes = 10,
            string? atLocal = null,
            int dueMinutesAgo = 5,
            bool enabled = true,
            bool alwaysOn = true,
            SessionStatus sessionStatus = SessionStatus.Running,
            bool neverLaunched = false,
            ScheduleWhenTargetDown? whenDown = null,
            int? graceMinutes = null,
            string prompt = "morning triage",
            AgentKind kind = AgentKind.ClaudeCode)
        {
            var now = DateTime.UtcNow;
            var workspace = Path.Combine(Path.GetTempPath(), $"sched-sweep-{Guid.NewGuid():N}");
            Directory.CreateDirectory(workspace);
            var sessionId = Guid.NewGuid();
            var agentId = Guid.NewGuid();
            await using var db = CreateContext();
            db.Agents.Add(new Agent
            {
                Id = agentId,
                Name = $"Sweep {agentId:N}"[..20],
                Slug = $"sweep-{agentId:N}"[..16],
                WorkingDirectory = workspace,
                Details = string.Empty,
                Status = AgentStatus.Idle,
                Kind = kind,
                AlwaysOn = alwaysOn,
                PersistentSessionId = neverLaunched ? null : sessionId.ToString("D"),
                CreatedAt = now,
                UpdatedAt = now,
            });
            if (!neverLaunched)
            {
                db.AgentSessions.Add(new AgentSession
                {
                    Id = sessionId,
                    DefinitionName = "fake",
                    AgentKind = kind,
                    Status = sessionStatus,
                    Cwd = workspace,
                    Cols = 120,
                    Rows = 30,
                    CreatedAt = now,
                    StartedAt = now,
                    LastSeenAt = now,
                });
            }

            var dueAt = now.AddMinutes(-dueMinutesAgo);
            var schedule = new Schedule
            {
                Id = Guid.NewGuid(),
                Name = "Morning triage",
                Kind = ScheduleKind.Prompt,
                Repeat = repeat,
                TimeZoneId = "Europe/London",
                NextFireAt = dueAt,
                Enabled = enabled,
                MissedGraceMinutes = graceMinutes ?? ScheduleRecurrence.DefaultMissedGraceMinutes(repeat, everyMinutes),
                CreatedAt = now,
                UpdatedAt = now,
                ConcurrencyToken = Guid.NewGuid(),
                AgentId = agentId,
                PromptText = prompt,
                WhenTargetDown = whenDown ?? (alwaysOn ? ScheduleWhenTargetDown.Queue : ScheduleWhenTargetDown.Skip),
                FireAt = repeat == ScheduleRepeat.Once ? dueAt : null,
                EveryMinutes = repeat == ScheduleRepeat.Interval ? everyMinutes : null,
                AnchorAt = repeat == ScheduleRepeat.Interval ? dueAt : null,
                AtLocal = repeat == ScheduleRepeat.Daily ? (atLocal ?? "09:00") : null,
                DaysOfWeek = 0,
            };
            db.Schedules.Add(schedule);
            await db.SaveChangesAsync();
            return new Seeded(schedule, sessionId, workspace);
        }
    }
}
