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
/// CARD-0312 S2 — WHO gets the synthetic boot probe, which is the whole of the card's cost
/// question. The answer is one launch shape: an unattended launch that typed nothing at all.
///
/// <para>Wherever a launch already types a real prompt, that prompt IS the probe — it exercises
/// composer, submit, transcript and reply through the identical path — so a synthetic "reply OK"
/// after it would be a second turn buying no evidence the first already carries. That is why
/// <b>pool delegates pay nothing</b>: a delegate always carries a brief.</para>
///
/// <para>N1 lives next door in <c>SessionHealthTests</c>, unmodified: the periodic pong probe was
/// deleted on 2026-07-23 for spending model turns on healthy idle sessions, and nothing here may
/// resurrect it. This probe fires inside a launch, at most once, never on a schedule.</para>
/// </summary>
[Category("Integration")]
[NotInParallel("AgentSessionLaunchFailure")]
public class BootLivenessProbeScopeTests
{
    [Test]
    public async Task an_unattended_launch_that_typed_nothing_gets_exactly_one_probe()
    {
        await using var fixture = await ProbeFixture.CreateAsync(alwaysOn: true);

        await fixture.LaunchAsync();

        var probes = await fixture.QueuedBodiesAsync();
        probes.Count.ShouldBe(1, "at most once per launch — this is not a periodic probe");
        probes[0].ShouldBe(new AgentSessionSettings().BootProbeBody);
    }

    [Test]
    public async Task a_channel_bound_agent_that_typed_nothing_is_unattended_too()
    {
        await using var fixture = await ProbeFixture.CreateAsync(alwaysOn: false);
        await fixture.BindChannelAsync();

        await fixture.LaunchAsync();

        (await fixture.QueuedBodiesAsync()).Count.ShouldBe(1);
    }

    [Test]
    public async Task an_attended_interactive_launch_gets_nothing_at_all()
    {
        // A human started it and is watching it. The probe population is the one the card
        // describes: "the operator discovering it hours later".
        await using var fixture = await ProbeFixture.CreateAsync(alwaysOn: false);

        await fixture.LaunchAsync();

        (await fixture.QueuedBodiesAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task a_launch_that_already_typed_a_prompt_pays_nothing_extra()
    {
        // N5, the cost regression guard, and the delegate case in one: a launch carrying a body
        // sends EXACTLY one prompt, and the boot-reply watch rides that prompt for free.
        await using var fixture = await ProbeFixture.CreateAsync(alwaysOn: true);

        await fixture.LaunchAsync(initialPrompt: "do the work");

        var bodies = await fixture.QueuedBodiesAsync();
        bodies.Count.ShouldBe(1);
        bodies[0].ShouldBe("do the work");
        bodies.ShouldNotContain(new AgentSessionSettings().BootProbeBody);
    }

    [Test]
    public async Task a_delegate_brief_queued_before_the_launch_suppresses_the_probe()
    {
        // The pool-delegate shape exactly: the brief is enqueued at dispatch, before the process
        // exists, and the launch flushes it. Structurally excluded from the probe.
        await using var fixture = await ProbeFixture.CreateAsync(alwaysOn: true);
        await fixture.QueueBriefAsync("[antiphon-task:deadbeef] do the work");

        await fixture.LaunchAsync();

        (await fixture.QueuedBodiesAsync())
            .ShouldNotContain(new AgentSessionSettings().BootProbeBody);
    }

    [Test]
    public async Task the_kill_switch_stops_the_probe_and_leaves_the_watch_alone()
    {
        await using var fixture = await ProbeFixture.CreateAsync(
            alwaysOn: true, sessionSettings: new AgentSessionSettings { BootProbeEnabled = false });

        await fixture.LaunchAsync();

        (await fixture.QueuedBodiesAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task a_session_with_no_transcript_ground_truth_is_never_probed()
    {
        // N3 at the launch scope: an OpenCode/Raw session cannot answer a probe in a way anything
        // could read, so sending one would spend a turn for nothing.
        await using var fixture = await ProbeFixture.CreateAsync(
            alwaysOn: true, sessionKind: AgentKind.OpenCode);

        await fixture.LaunchAsync();

        (await fixture.QueuedBodiesAsync()).ShouldBeEmpty();
    }

    [Test]
    public void the_probe_body_implies_no_work_and_is_one_line()
    {
        // A probe with content invites a long turn, which is the cost the 2026-07-23 removal of
        // the periodic pong probe complained about.
        var body = new AgentSessionSettings().BootProbeBody;

        body.ShouldNotContain("\n");
        body.Length.ShouldBeLessThan(140);
        body.ShouldContain("Do not do any other work");
        new AgentSessionSettings().BootProbeEnabled.ShouldBeTrue();
    }

    // ---- helpers ---------------------------------------------------------------------------------

    /// <summary>One fake adapter, handed to the single launch each test performs.</summary>
    private sealed class SingleAdapterFactory(IAgentProtocolAdapter adapter) : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) => adapter;
    }

    private sealed class ProbeFixture : IAsyncDisposable
    {
        private BridgeQueueHarness _harness = null!;
        private IServiceScope _scope = null!;

        public Guid SessionId { get; private init; }
        public Guid AgentId { get; private init; }
        public string Workspace { get; private init; } = "";

        public static async Task<ProbeFixture> CreateAsync(
            bool alwaysOn,
            AgentKind sessionKind = AgentKind.ClaudeCode,
            AgentSessionSettings? sessionSettings = null)
        {
            var adapter = new FakeAgentProtocolAdapter();
            var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
            {
                AlwaysOn = alwaysOn,
                ConfigureServices = s =>
                {
                    s.AddSingleton<IAgentProtocolAdapterFactory>(new SingleAdapterFactory(adapter));
                    s.AddSingleton<IOptions<AgentSessionSettings>>(Options.Create(
                        sessionSettings ?? new AgentSessionSettings
                        {
                            FirstDeltaTimeoutMs = 200,
                            KillGraceMs = 100,
                            RemoteControlArmTimeoutMs = 300,
                            RemoteControlSetupTimeoutMs = 2_000,
                            SessionLogPath = Path.Combine(
                                Path.GetTempPath(), $"antiphon-boot-probe-{Guid.NewGuid():N}"),
                        }));
                },
            });

            var workspace = Path.Combine(harness.TempRoot, "probe-workspace");
            Directory.CreateDirectory(workspace);
            var sessionId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            await using (var db = BridgeQueueHarness.CreateContext())
            {
                db.AgentSessions.Add(new AgentSession
                {
                    Id = sessionId,
                    DefinitionName = "fake",
                    AgentKind = sessionKind,
                    Status = SessionStatus.Starting,
                    Cwd = workspace,
                    Cols = 120,
                    Rows = 30,
                    CreatedAt = now,
                    StartedAt = now,
                    LastSeenAt = now,
                });
                await db.SaveChangesAsync();
                await db.Agents.Where(a => a.Id == harness.AgentId).ExecuteUpdateAsync(u => u
                    .SetProperty(a => a.Status, AgentStatus.Running)
                    .SetProperty(a => a.PersistentSessionId, sessionId.ToString("D")));
            }

            adapter.RegisterOnStart = harness.Runtime;
            return new ProbeFixture
            {
                SessionId = sessionId,
                AgentId = harness.AgentId,
                Workspace = workspace,
                _harness = harness,
                _scope = harness.Provider.CreateScope(),
            };
        }

        public Task LaunchAsync(string? initialPrompt = null) =>
            _scope.ServiceProvider.GetRequiredService<AgentSessionService>().LaunchInteractiveAsync(
                SessionId,
                AgentId,
                new AgentLaunchSpec(
                    "fake", AgentKind.Raw, "fake", [], new Dictionary<string, string>(),
                    Workspace, 120, 30),
                remoteControlName: null,
                resume: false,
                notes: null,
                CancellationToken.None,
                initialPrompt);

        public async Task QueueBriefAsync(string body)
        {
            await _scope.ServiceProvider.GetRequiredService<SessionMessageQueueService>()
                .EnqueueAsync(
                    SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None,
                    QueuedMessageOrigin.Delegation);
        }

        public async Task BindChannelAsync()
        {
            await using var db = BridgeQueueHarness.CreateContext();
            db.ChatChannels.Add(new ChatChannel
            {
                Id = Guid.NewGuid(),
                Provider = "telegram",
                ExternalId = $"probe-{Guid.NewGuid():N}",
                Title = "probe channel",
                AgentId = AgentId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        public async Task<List<string>> QueuedBodiesAsync()
        {
            await using var db = BridgeQueueHarness.CreateContext();
            return await db.SessionQueuedMessages
                .Where(m => m.AgentSessionId == SessionId)
                .OrderBy(m => m.Sequence)
                .Select(m => m.Body)
                .ToListAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await using (var db = BridgeQueueHarness.CreateContext())
            {
                await db.ChatChannels.Where(c => c.AgentId == AgentId).ExecuteDeleteAsync();
                await db.TranscriptEntries.Where(e => e.AgentSessionId == SessionId).ExecuteDeleteAsync();
                await db.SessionQueuedMessages.Where(m => m.AgentSessionId == SessionId).ExecuteDeleteAsync();
                await db.AgentIncidents.Where(i => i.SessionId == SessionId).ExecuteDeleteAsync();
                await db.AgentSessions.Where(s => s.Id == SessionId).ExecuteDeleteAsync();
            }

            _scope.Dispose();
            await _harness.DisposeAsync();
        }
    }
}
