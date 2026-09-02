using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
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
/// CARD-0056 slices 1-2: what a launch does when it fails, and what it must NOT treat as a failure.
///
/// Slice 1 — a failed launch has to kill what it started. <c>DisposeAsync</c> is not teardown: on
/// the production <c>RunnerClaudeAdapter</c> it is literally <c>=&gt; ValueTask.CompletedTask</c>,
/// because the agent lives in a detached pty-host that outlives the server. Every catch that only
/// disposed therefore left a live agent behind a row that read Failed — two such sessions were
/// found running on 2026-08-16, one of them three days old.
///
/// Slice 2 — <c>/remote-control</c> is monitoring, not the session's purpose, so its delivery
/// failure must degrade (incident) rather than fail a healthy session. That is the whole incident:
/// a 15-character slash command never reached the composer of a live orchestrator, the launch was
/// declared failed, and everything after it was a correct response to a false signal.
///
/// Every assertion is scoped to the rows this test made (CLAUDE.md: the integration suite shares
/// one Postgres, so an unscoped count also asserts "no other test has data right now").
/// </summary>
[Category("Integration")]
[NotInParallel("AgentSessionLaunchFailure")]
public class AgentSessionLaunchFailureTests
{
    // ---- Slice 1: kill before dispose -----------------------------------------------------------

    /// <summary>
    /// The interactive path's catch (the one that leaked cefed08a's replacement). Anything thrown
    /// between StartAsync and the end of the boot sequence must tear the process down, in that
    /// order: kill, then dispose.
    /// </summary>
    [Test]
    public async Task Interactive_launch_failure_kills_the_process_before_disposing_it()
    {
        var adapter = new FakeAgentProtocolAdapter { ReadyResult = false };
        await using var fixture = await LaunchFixture.CreateAsync(adapter);

        var launch = fixture.LaunchInteractiveAsync();

        await Should.ThrowAsync<InvalidOperationException>(launch);
        adapter.Lifecycle.ShouldBe(["Kill", "Dispose"]);

        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.Status.ShouldBe(SessionStatus.Failed);
        session.TerminationSource.ShouldBe(SessionTerminationSource.SystemRequest);
        var agent = await db.Agents.SingleAsync(a => a.Id == fixture.AgentId);
        agent.Status.ShouldBe(AgentStatus.Failed, "the Start API already flipped it to Running");
    }

    [Test]
    public async Task Herdr_pairing_refusal_records_SystemRequest()
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var fixture = await LaunchFixture.CreateAsync(adapter);

        await using (var db = LaunchFixture.CreateContext())
        {
            var sessionRow = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
            sessionRow.SessionBackend = SessionBackend.Herdr;
            var agent = await db.Agents.SingleAsync(a => a.Id == fixture.AgentId);
            agent.Kind = AgentKind.Raw;
            await db.SaveChangesAsync();
        }

        var spec = new AgentLaunchSpec(
            "fake",
            AgentKind.Raw,
            "fake",
            [],
            new Dictionary<string, string>(),
            fixture.Workspace,
            120,
            30,
            Backend: SessionBackend.Herdr);

        var ex = await Should.ThrowAsync<ConflictException>(
            fixture.LaunchInteractiveAsync(spec: spec));
        ex.Code.ShouldBe("herdr_refused");
        adapter.Started.ShouldBeFalse();

        await using var verify = LaunchFixture.CreateContext();
        var session = await verify.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.Status.ShouldBe(SessionStatus.Failed);
        session.FailureReason.ShouldContain("Raw");
        session.TerminationSource.ShouldBe(SessionTerminationSource.SystemRequest);
    }

    // ---- CARD-0106 S2: the API key tripwire, in the method every launch passes through ----------

    /// <summary>
    /// The tripwire's whole reason to exist, exercised where it lives rather than as a static
    /// helper: a spec that never went through <c>ApiKeyEnvResolver</c> reaches
    /// <c>AgentSessionService.BuildRuntimeLaunchSpec</c>, which is the one method all three
    /// <c>adapter.StartAsync</c> sites go through. It must refuse, and it must refuse BEFORE the
    /// process starts — a child already holding a literal {{key:...}} in its environment would
    /// authenticate as nobody, with no error anywhere to say why.
    /// </summary>
    [Test]
    public async Task An_unresolved_api_key_placeholder_refuses_the_launch_before_the_process_starts()
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        var unresolved = new AgentLaunchSpec(
            "fake",
            AgentKind.Raw,
            "fake",
            [],
            new Dictionary<string, string> { ["ANTHROPIC_API_KEY"] = "{{key:anthropic-maven}}" },
            fixture.Workspace,
            120,
            30);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            fixture.LaunchInteractiveAsync(spec: unresolved));

        ex.Message.ShouldContain("ANTHROPIC_API_KEY");
        ex.Message.ShouldContain("{{key:anthropic-maven}}");
        adapter.Started.ShouldBeFalse("nothing may launch holding an unresolved placeholder");

        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.Status.ShouldBe(SessionStatus.Failed);
        session.FailureReason.ShouldContain(
            "anthropic-maven", customMessage: "the failure reason names the key to add or fix");
    }

    /// <summary>
    /// The same choke point, for the leak surface the placeholder is banned from: an ARGUMENT. This
    /// is the rule being ENFORCED rather than documented — argv is process-listing-visible and is
    /// quoted into failure reasons, and --append-system-prompt text also lands in the transcript.
    /// </summary>
    [Test]
    public async Task A_placeholder_in_a_launch_argument_refuses_the_launch_too()
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        var inArgs = new AgentLaunchSpec(
            "fake",
            AgentKind.Raw,
            "fake",
            ["--append-system-prompt", "Authenticate with {{key:anthropic-maven}}."],
            new Dictionary<string, string>(),
            fixture.Workspace,
            120,
            30);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            fixture.LaunchInteractiveAsync(spec: inArgs));

        ex.Message.ShouldContain("environment VALUES only");
        adapter.Started.ShouldBeFalse();
    }

    /// <summary>
    /// The card path's OUTER catch — the finding beyond the original triage. Its two inner timeout
    /// branches already killed; everything else (ready check, remote-control commands, a failing
    /// save) fell through to a dispose-only cleanup and leaked exactly the same way.
    /// </summary>
    [Test]
    public async Task Card_launch_failure_outside_the_timeout_branches_kills_before_disposing()
    {
        var adapter = new FakeAgentProtocolAdapter
        {
            PromptFailure = _ => new PromptDeliveryException("No composer evidence for the work prompt."),
        };
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        var card = await fixture.CreateCardAsync();

        var start = fixture.StartCardSessionAsync(card, "do the work");

        await Should.ThrowAsync<PromptDeliveryException>(start);
        adapter.Lifecycle.ShouldBe(["Kill", "Dispose"]);

        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.CardId == card);
        session.Status.ShouldBe(SessionStatus.Failed);
        session.TerminationSource.ShouldBe(SessionTerminationSource.SystemRequest);
        var attempt = await db.RunAttempts.SingleAsync(a => a.CardId == card);
        attempt.Phase.ShouldBe(RunPhase.Failed);
    }

    /// <summary>
    /// The resume-not-found fallback relaunches under the SAME session id. Until the kill landed
    /// first, that only worked when the first process happened to have died on its own; now it is
    /// correct by construction.
    /// </summary>
    [Test]
    public async Task Resume_not_found_kills_the_first_process_before_the_fallback_relaunch()
    {
        // The needle Claude prints when the conversation it was asked to resume is gone.
        var resumeAdapter = new FakeAgentProtocolAdapter
        {
            StartupOutput = "No conversation found with session ID: 0b5f2f7e",
            ReadyResult = false,
        };
        var freshAdapter = new FakeAgentProtocolAdapter();
        await using var fixture = await LaunchFixture.CreateAsync(resumeAdapter, freshAdapter);
        freshAdapter.RegisterOnStart = fixture.Runtime;

        await fixture.LaunchInteractiveAsync(resume: true);

        resumeAdapter.Lifecycle.ShouldBe(
            ["Kill", "Dispose"],
            "the abandoned --resume process must be killed before its id is reused");
        freshAdapter.Started.ShouldBeTrue();
        freshAdapter.StartedArgs.ShouldNotContain("--resume", "the fallback starts a fresh conversation");
        freshAdapter.Killed.ShouldBeFalse();

        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.Status.ShouldBe(SessionStatus.Running);
    }

    /// <summary>
    /// Killing an already-killed process is a no-op, not an error — the runner answers false for a
    /// session it no longer knows. Driven through the real double-teardown path: a --resume launch
    /// is killed as not-found, and the fallback relaunch of the SAME process fails too.
    /// </summary>
    [Test]
    public async Task A_second_kill_of_an_already_killed_process_is_harmless()
    {
        var adapter = new FakeAgentProtocolAdapter
        {
            StartupOutput = "No conversation found with session ID: 0b5f2f7e",
            ReadyResult = false,
            // What the runner really answers once the process is gone.
            KillResult = false,
        };
        // One adapter instance for BOTH launches: the fallback re-enters teardown on a handle that
        // has already been killed once.
        await using var fixture = await LaunchFixture.CreateAsync(new SharedAdapterFactory(adapter));

        var launch = fixture.LaunchInteractiveAsync(resume: true);

        await Should.ThrowAsync<InvalidOperationException>(
            launch,
            "the fallback's own failure surfaces, not the not-found marker");
        adapter.KillCount.ShouldBe(2);
        adapter.Lifecycle.ShouldBe(["Kill", "Dispose", "Kill", "Dispose"]);

        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.Status.ShouldBe(SessionStatus.Failed);
        session.FailureReason.ShouldBe("Agent process did not become ready.");
    }

    // ---- Slice 2: /remote-control is best-effort ------------------------------------------------

    /// <summary>
    /// The incident itself, inverted. A healthy resuming orchestrator swallowed a 15-character
    /// "/remote-control" while re-rendering its history; the submitter correctly found no composer
    /// evidence, and that verdict — about a MONITORING command — failed the whole launch. Now the
    /// session stays Running, the rest of the boot sequence still happens, and the degradation is
    /// an incident instead of a dead session.
    /// </summary>
    [Test]
    public async Task Remote_control_delivery_failure_leaves_the_session_running_and_records_an_incident()
    {
        var adapter = new FakeAgentProtocolAdapter
        {
            PromptOutput = "remote-control is active",
            PromptFailure = prompt => prompt.StartsWith("/remote-control", StringComparison.Ordinal)
                ? new PromptDeliveryException("No composer evidence appeared for the typed body.")
                : null,
        };
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        fixture.WireDelivery(adapter);
        // Already Pending when the launch starts — the enqueue path refuses to type into a booting
        // TUI, so this is what the boot's closing FlushSessionAsync exists to release.
        var queued = await fixture.SeedPendingMessageAsync("queued while the session was Starting");

        await fixture.LaunchInteractiveAsync(
            remoteControlName: "Antiphon-Orchestrator",
            notes: new LaunchNotes("launch note body", null));

        adapter.Prompts.ShouldBe(
            ["/remote-control", "/remote-control", "/remote-control"],
            "slice 3 retypes the whole verified submit before giving up on it");
        adapter.Prompts.ShouldNotContain(
            p => p.StartsWith("/rename", StringComparison.Ordinal),
            "never append to a composer that may still be holding the first body");

        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.Status.ShouldBe(SessionStatus.Running, "a monitoring command must not fail a healthy session");
        var agent = await db.Agents.SingleAsync(a => a.Id == fixture.AgentId);
        agent.Status.ShouldBe(AgentStatus.Running);

        var incident = await db.AgentIncidents.SingleAsync(
            i => i.AgentId == fixture.AgentId && i.Kind == AgentIncidentKind.RcDegraded);
        incident.Severity.ShouldBe(AlertSeverity.Warning);
        incident.SessionId.ShouldBe(fixture.SessionId);
        incident.FailureReason.ShouldBe("RemoteControlNotDelivered");
        // Incidents are the supervisor's alerts 1:1, deduped per agent+kind.
        var alert = await db.Alerts.SingleAsync(
            a => a.AgentId == fixture.AgentId
                && a.DedupKey == $"supervisor:{AgentIncidentKind.RcDegraded}:{fixture.AgentId}");
        alert.Severity.ShouldBe(AlertSeverity.Warning);

        // The launch ran to completion: the note went out and the boot's flush released the queue.
        adapter.SubmittedBodies.ShouldContain("launch note body");
        var released = await db.SessionQueuedMessages.SingleAsync(m => m.Id == queued);
        released.Status.ShouldBe(QueuedMessageStatus.Sent);
    }

    /// <summary>
    /// The other half: the command lands but the bridge never reports itself armed. That was
    /// log-only, so a session that is silently unreachable from claude.ai looked identical to a
    /// healthy one. It is an incident now — and /rename is NOT sent: Claude only syncs a title
    /// while the bridge is armed, so typing it into a connecting TUI cannot help and can only
    /// jam another slash command into the composer (CARD-0240).
    /// </summary>
    [Test]
    public async Task Remote_control_that_never_arms_records_an_incident_and_does_not_rename()
    {
        var adapter = new FakeAgentProtocolAdapter { PromptOutput = "some output, but never the marker" };
        await using var fixture = await LaunchFixture.CreateAsync(adapter);

        await fixture.LaunchInteractiveAsync(remoteControlName: "Antiphon-Orchestrator");

        adapter.Prompts.ShouldBe(["/remote-control"]);
        adapter.Prompts.ShouldNotContain(p => p.StartsWith("/rename", StringComparison.Ordinal));

        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.Status.ShouldBe(SessionStatus.Running);
        var incident = await db.AgentIncidents.SingleAsync(
            i => i.AgentId == fixture.AgentId && i.Kind == AgentIncidentKind.RcDegraded);
        incident.FailureReason.ShouldBe("RemoteControlNotArmed");
        incident.Severity.ShouldBe(AlertSeverity.Warning);
        incident.Message.ShouldContain("armed");
    }

    /// <summary>
    /// Card path: RC accepts, never arms, and the actual work body still goes out after setup
    /// gives up. A failed monitor bootstrap must not convert an otherwise-deliverable work
    /// prompt into a hung or silently-healthy-looking launch.
    /// </summary>
    [Test]
    public async Task Card_launch_unarmed_remote_control_still_delivers_the_work_prompt()
    {
        var adapter = new FakeAgentProtocolAdapter { PromptOutput = "working, but never the armed marker" };
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        var card = await fixture.CreateCardAsync();
        await fixture.AssignAgentAsync(card);

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var started = await fixture.StartCardSessionAsync(
            card, "do the work", remoteControlName: "Card Agent", kind: AgentKind.ClaudeCode,
            ct: deadline.Token);

        adapter.Prompts.ShouldBe(["/remote-control", "do the work"]);
        adapter.Prompts.ShouldNotContain(p => p.StartsWith("/rename", StringComparison.Ordinal));

        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == started.SessionId);
        session.Status.ShouldBe(SessionStatus.Running);
        var attempt = await db.RunAttempts.SingleAsync(a => a.Id == started.RunAttemptId);
        attempt.Phase.ShouldBe(RunPhase.Succeeded);
        var incident = await db.AgentIncidents.SingleAsync(
            i => i.AgentId == fixture.AgentId && i.Kind == AgentIncidentKind.RcDegraded);
        incident.Severity.ShouldBe(AlertSeverity.Warning);
        incident.FailureReason.ShouldBe("RemoteControlNotArmed");
        incident.SessionId.ShouldBe(started.SessionId);
    }

    /// <summary>
    /// The actual CARD-0240 deadline leak: a snapshot call that never returns. The polling loop
    /// could only check its deadline BETWEEN snapshots; a hung HTTP GetResult() held the launch
    /// forever. The async snapshot must honor the supplied token so the outer RC budget releases
    /// the launch, records the degradation, skips /rename, and still reaches the work prompt.
    /// </summary>
    [Test]
    public async Task Hung_raw_snapshot_during_rc_arm_wait_is_bounded_and_reaches_the_work_prompt()
    {
        var adapter = new FakeAgentProtocolAdapter
        {
            PromptOutput = "some output, but never the marker",
            HangRawSnapshotUntilCanceled = true,
        };
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        var card = await fixture.CreateCardAsync();
        await fixture.AssignAgentAsync(card);

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var started = await fixture.StartCardSessionAsync(
            card, "do the work", remoteControlName: "Card Agent", kind: AgentKind.ClaudeCode,
            ct: deadline.Token);

        adapter.Prompts.ShouldBe(["/remote-control", "do the work"]);
        adapter.Prompts.ShouldNotContain(p => p.StartsWith("/rename", StringComparison.Ordinal));

        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == started.SessionId);
        session.Status.ShouldBe(SessionStatus.Running);
        var attempt = await db.RunAttempts.SingleAsync(a => a.Id == started.RunAttemptId);
        attempt.Phase.ShouldBe(RunPhase.Succeeded);
        var incident = await db.AgentIncidents.SingleAsync(
            i => i.AgentId == fixture.AgentId && i.Kind == AgentIncidentKind.RcDegraded);
        incident.Severity.ShouldBe(AlertSeverity.Warning);
        incident.FailureReason.ShouldBe("RemoteControlNotArmed");
    }

    /// <summary>
    /// Cancelling the launch itself is not an RC timeout. The local setup CTS is linked to the
    /// caller token; expiry of that caller token must propagate, never be rewritten as RcDegraded.
    /// </summary>
    [Test]
    public async Task External_cancellation_during_rc_setup_is_not_reported_as_degraded()
    {
        var adapter = new FakeAgentProtocolAdapter
        {
            PromptOutput = "some output, but never the marker",
            HangRawSnapshotUntilCanceled = true,
        };
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        using var cts = new CancellationTokenSource();

        var launch = fixture.LaunchInteractiveAsync(
            remoteControlName: "Antiphon-Orchestrator", ct: cts.Token);

        var waitUntil = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (adapter.Prompts.Count == 0 && !launch.IsCompleted && DateTime.UtcNow < waitUntil)
            await Task.Delay(10);
        adapter.Prompts.Count.ShouldBeGreaterThan(0, "the RC command should have been typed before we cancel");
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(launch);

        await using var db = LaunchFixture.CreateContext();
        (await db.AgentIncidents.CountAsync(
            i => i.AgentId == fixture.AgentId && i.Kind == AgentIncidentKind.RcDegraded))
            .ShouldBe(0);
    }

    // ---- CARD-0292 S1/S2: skip /remote-control on a live bridge; Esc a standing menu ----------

    [Test]
    public async Task Resume_with_an_already_armed_bridge_skips_remote_control_and_still_renames()
    {
        var adapter = new FakeAgentProtocolAdapter { PromptOutput = "remote-control is active" };
        var probe = new CountingRcProbe { Armed = true };
        await using var fixture = await LaunchFixture.CreateAsync(
            adapter, s => s.AddSingleton<IRcBridgeProbe>(probe));

        await fixture.LaunchInteractiveAsync(remoteControlName: "Antiphon-Orchestrator", resume: true);

        probe.Calls.ShouldBeGreaterThan(0, "a resume must poll the child pid's own state file");
        probe.LastPid.ShouldBe(adapter.Pid);
        adapter.Prompts.ShouldBe(["/rename Antiphon-Orchestrator"]);
        adapter.Prompts.ShouldNotContain("/remote-control");

        await using var db = LaunchFixture.CreateContext();
        (await db.AgentIncidents.CountAsync(
            i => i.AgentId == fixture.AgentId && i.Kind == AgentIncidentKind.RcDegraded))
            .ShouldBe(0);
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.Status.ShouldBe(SessionStatus.Running);
    }

    [Test]
    public async Task Resume_unarmed_falls_through_and_sends_remote_control_as_today()
    {
        var adapter = new FakeAgentProtocolAdapter { PromptOutput = "remote-control is active" };
        var probe = new CountingRcProbe { Armed = false };
        await using var fixture = await LaunchFixture.CreateAsync(
            adapter, s => s.AddSingleton<IRcBridgeProbe>(probe));

        await fixture.LaunchInteractiveAsync(remoteControlName: "Antiphon-Orchestrator", resume: true);

        probe.Calls.ShouldBeGreaterThan(0);
        adapter.Prompts.ShouldBe(["/remote-control", "/rename Antiphon-Orchestrator"]);
    }

    [Test]
    public async Task Fresh_launch_never_probes_the_bridge()
    {
        var adapter = new FakeAgentProtocolAdapter { PromptOutput = "remote-control is active" };
        var probe = new CountingRcProbe { Armed = true };
        await using var fixture = await LaunchFixture.CreateAsync(
            adapter, s => s.AddSingleton<IRcBridgeProbe>(probe));

        await fixture.LaunchInteractiveAsync(remoteControlName: "Antiphon-Orchestrator");

        probe.Calls.ShouldBe(0, "a fresh launch (resumeMode null) must not skip the send");
        adapter.Prompts.ShouldBe(["/remote-control", "/rename Antiphon-Orchestrator"]);
    }

    [Test]
    public async Task Resume_probe_throw_degrades_to_the_send_path_and_the_launch_survives()
    {
        var adapter = new FakeAgentProtocolAdapter { PromptOutput = "remote-control is active" };
        await using var fixture = await LaunchFixture.CreateAsync(
            adapter, s => s.AddSingleton<IRcBridgeProbe>(new ThrowingRcProbe()));

        await fixture.LaunchInteractiveAsync(remoteControlName: "Antiphon-Orchestrator", resume: true);

        adapter.Prompts.ShouldBe(["/remote-control", "/rename Antiphon-Orchestrator"]);
        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.Status.ShouldBe(SessionStatus.Running);
    }

    [Test]
    public async Task Unarmed_branch_with_the_management_menu_on_screen_escapes_and_renames_without_degrading()
    {
        var adapter = new FakeAgentProtocolAdapter
        {
            PromptOutput = "some output, but never the marker",
            RemoteControlMenuOpen = true,
        };
        await using var fixture = await LaunchFixture.CreateAsync(adapter);

        await fixture.LaunchInteractiveAsync(remoteControlName: "Antiphon-Orchestrator");

        adapter.Prompts.ShouldBe(["/remote-control", "/rename Antiphon-Orchestrator"]);
        adapter.Inputs.ShouldContain("\u001b");
        adapter.RemoteControlMenuOpen.ShouldBeFalse();

        await using var db = LaunchFixture.CreateContext();
        (await db.AgentIncidents.CountAsync(
            i => i.AgentId == fixture.AgentId && i.Kind == AgentIncidentKind.RcDegraded))
            .ShouldBe(0, "the menu is positive evidence the bridge is armed — not a degradation");
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.Status.ShouldBe(SessionStatus.Running);
    }

    [Test]
    public async Task Catch_path_with_the_management_menu_escapes_names_it_and_skips_rename()
    {
        var adapter = new FakeAgentProtocolAdapter
        {
            RemoteControlMenuOpen = true,
            PromptFailure = prompt => prompt.StartsWith("/remote-control", StringComparison.Ordinal)
                ? new PromptDeliveryException("No composer evidence appeared for the typed body.")
                : null,
        };
        await using var fixture = await LaunchFixture.CreateAsync(adapter);

        await fixture.LaunchInteractiveAsync(remoteControlName: "Antiphon-Orchestrator");

        adapter.Prompts.ShouldBe(
            ["/remote-control", "/remote-control", "/remote-control"],
            "slice 3 retypes the verified submit before giving up");
        adapter.Prompts.ShouldNotContain(p => p.StartsWith("/rename", StringComparison.Ordinal));
        adapter.Inputs.ShouldContain("\u001b");
        adapter.RemoteControlMenuOpen.ShouldBeFalse();

        await using var db = LaunchFixture.CreateContext();
        var incident = await db.AgentIncidents.SingleAsync(
            i => i.AgentId == fixture.AgentId && i.Kind == AgentIncidentKind.RcDegraded);
        incident.FailureReason.ShouldBe("RemoteControlNotDelivered");
        incident.Message.ShouldContain("management menu");
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.Status.ShouldBe(SessionStatus.Running);
    }

    private sealed class CountingRcProbe : IRcBridgeProbe
    {
        public bool Armed { get; set; }
        public int Calls { get; private set; }
        public int? LastPid { get; private set; }

        public RcProbeResult Probe(int pid)
        {
            Calls++;
            LastPid = pid;
            return new RcProbeResult(Armed, BridgeConnections: Armed ? 2 : 0, StateFileFound: true);
        }
    }

    private sealed class ThrowingRcProbe : IRcBridgeProbe
    {
        public RcProbeResult Probe(int pid) =>
            throw new InvalidOperationException("bridge probe exploded");
    }

    /// <summary>
    /// The line slice 2 must not cross: the card WORK prompt is the session's purpose, so its
    /// delivery failure still fails the launch — now with the process killed on the way out.
    /// </summary>
    [Test]
    public async Task Card_work_prompt_delivery_failure_still_fails_the_launch()
    {
        var adapter = new FakeAgentProtocolAdapter
        {
            PromptOutput = "remote-control is active",
            PromptFailure = prompt => prompt.StartsWith('/')
                ? null
                : new PromptDeliveryException("No composer evidence appeared for the work prompt."),
        };
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        var card = await fixture.CreateCardAsync();

        var start = fixture.StartCardSessionAsync(
            card, "do the work", remoteControlName: "Card Agent", kind: AgentKind.ClaudeCode);

        await Should.ThrowAsync<PromptDeliveryException>(start);
        // The work prompt gets slice 3's retries too, and this session is brand new — no transcript
        // file exists yet, so there is no ground truth to late-confirm against and the launch fails.
        adapter.Prompts.ShouldBe(
            ["/remote-control", "/rename Card Agent", "do the work", "do the work", "do the work"]);
        adapter.Lifecycle.ShouldBe(["Kill", "Dispose"]);

        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.CardId == card);
        session.Status.ShouldBe(SessionStatus.Failed);
    }

    // ---- Slice 3: boot retry + transcript late-confirm ------------------------------------------

    /// <summary>
    /// The fix the capture actually supports. cefed08a's replacement, resuming the SAME
    /// conversation 60 seconds later, armed on its first try — so the swallow is transient and
    /// re-typing is what beats it. Re-typing is safe because the exception means no composer
    /// evidence appeared: the composer is not holding the body, so this cannot double-submit.
    /// </summary>
    [Test]
    public async Task A_swallowed_boot_prompt_is_retyped_and_the_second_attempt_lands()
    {
        var adapter = new FakeAgentProtocolAdapter { PromptOutput = "remote-control is active" };
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        var typed = 0;
        adapter.PromptFailure = prompt =>
            prompt.StartsWith("/remote-control", StringComparison.Ordinal) && ++typed == 1
                ? new PromptDeliveryException("No composer evidence appeared for the typed body.")
                : null;

        await fixture.LaunchInteractiveAsync(remoteControlName: "Antiphon-Orchestrator");

        adapter.Prompts.ShouldBe(
            ["/remote-control", "/remote-control", "/rename Antiphon-Orchestrator"]);

        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.Status.ShouldBe(SessionStatus.Running);
        (await db.AgentIncidents.Where(i => i.AgentId == fixture.AgentId).ToListAsync())
            .ShouldBeEmpty("a retry that succeeded is not a degradation");
    }

    /// <summary>
    /// The residual case the retries cannot see: the command really did submit, and only the screen
    /// reads were blind. Claude records a local slash command as a
    /// <c>&lt;command-name&gt;</c> wrapper, and that record past the pre-attempt baseline is ground
    /// truth — so the launch proceeds as delivered (the <c>/rename</c> goes out) instead of
    /// degrading a session that is, in fact, fully monitored.
    /// </summary>
    [Test]
    public async Task Exhausted_boot_retries_are_late_confirmed_by_the_transcript_record()
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        // A resumed session with a day of ingestion behind it — the observability gate CARD-0055
        // scoped fresh boots out of. Complete turn, so the launch writes no restart boundary.
        await fixture.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, "yesterday's work");
        await fixture.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");

        var typed = 0;
        adapter.PromptFailure = prompt =>
        {
            if (!prompt.StartsWith("/remote-control", StringComparison.Ordinal))
                return null;
            if (++typed == LaunchFixture.BootPromptAttempts)
            {
                // The command DID run — which is why the TUI prints the armed marker and Claude
                // writes the wrapper. Only the composer echo was never seen. Blocking on purpose:
                // the row must be committed before the retry loop's confirm poll reads it.
                adapter.Emit("remote-control is active");
                fixture.InsertTranscriptEntryAsync(
                        TranscriptKinds.UserPrompt, RemoteControlWrapper, timestamp: DateTime.UtcNow)
                    .GetAwaiter().GetResult();
            }

            return new PromptDeliveryException("No composer evidence appeared for the typed body.");
        };

        await fixture.LaunchInteractiveAsync(remoteControlName: "Antiphon-Orchestrator");

        adapter.Prompts.ShouldBe([
            "/remote-control", "/remote-control", "/remote-control", "/rename Antiphon-Orchestrator"
        ], "the confirmed command lets the launch continue to the rename it used to skip");

        await using var db = LaunchFixture.CreateContext();
        (await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId)).Status
            .ShouldBe(SessionStatus.Running);
        (await db.AgentIncidents.Where(i => i.AgentId == fixture.AgentId).ToListAsync())
            .ShouldBeEmpty("the command was delivered; only our screen reads were blind");
    }

    /// <summary>
    /// The false positive the late-confirm must never produce. A <c>--resume</c> re-ingests the
    /// conversation's copied history — which contains the PREVIOUS boot's own
    /// <c>/remote-control</c> wrapper, arriving with a sequence past our baseline because backfill
    /// rebases unseen entries onto the end. The record's own timestamp is what tells the two apart.
    /// </summary>
    [Test]
    public async Task A_previous_boots_own_command_record_never_confirms_this_one()
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        await fixture.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, "yesterday's work");
        await fixture.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");

        var typed = 0;
        adapter.PromptFailure = prompt =>
        {
            if (!prompt.StartsWith("/remote-control", StringComparison.Ordinal))
                return null;
            if (++typed == LaunchFixture.BootPromptAttempts)
            {
                fixture.InsertTranscriptEntryAsync(
                        TranscriptKinds.UserPrompt,
                        RemoteControlWrapper,
                        timestamp: DateTime.UtcNow.AddDays(-3))
                    .GetAwaiter().GetResult();
            }

            return new PromptDeliveryException("No composer evidence appeared for the typed body.");
        };

        await fixture.LaunchInteractiveAsync(remoteControlName: "Antiphon-Orchestrator");

        adapter.Prompts.ShouldNotContain(p => p.StartsWith("/rename", StringComparison.Ordinal));

        await using var db = LaunchFixture.CreateContext();
        (await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId)).Status
            .ShouldBe(SessionStatus.Running, "degrading still never fails a healthy session");
        var incident = await db.AgentIncidents.SingleAsync(
            i => i.AgentId == fixture.AgentId && i.Kind == AgentIncidentKind.RcDegraded);
        incident.FailureReason.ShouldBe("RemoteControlNotDelivered");
    }

    /// <summary>
    /// The observability gate, and why CARD-0055's boot scope-out still stands for fresh sessions:
    /// with no transcript at baseline time there is no floor, so a record appearing during the
    /// attempts proves nothing about which write produced it. Same seeded record as the confirming
    /// test above — the ONLY difference is that this session had no transcript to measure against.
    /// </summary>
    [Test]
    public async Task A_session_with_no_transcript_at_baseline_never_late_confirms()
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var fixture = await LaunchFixture.CreateAsync(adapter);

        var typed = 0;
        adapter.PromptFailure = prompt =>
        {
            if (!prompt.StartsWith("/remote-control", StringComparison.Ordinal))
                return null;
            if (++typed == LaunchFixture.BootPromptAttempts)
            {
                fixture.InsertTranscriptEntryAsync(
                        TranscriptKinds.UserPrompt, RemoteControlWrapper, timestamp: DateTime.UtcNow)
                    .GetAwaiter().GetResult();
            }

            return new PromptDeliveryException("No composer evidence appeared for the typed body.");
        };

        await fixture.LaunchInteractiveAsync(remoteControlName: "Antiphon-Orchestrator");

        adapter.Prompts.ShouldBe(["/remote-control", "/remote-control", "/remote-control"]);

        await using var db = LaunchFixture.CreateContext();
        (await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId)).Status
            .ShouldBe(SessionStatus.Running);
        var incident = await db.AgentIncidents.SingleAsync(
            i => i.AgentId == fixture.AgentId && i.Kind == AgentIncidentKind.RcDegraded);
        incident.FailureReason.ShouldBe("RemoteControlNotDelivered");
    }

    /// <summary>What Claude really writes to the JSONL when a local slash command runs.</summary>
    private const string RemoteControlWrapper =
        "<command-name>/remote-control</command-name>\n"
        + "            <command-message>remote-control</command-message>\n"
        + "            <command-args></command-args>";

    // ---- CARD-0025: spawn prompt spill ----------------------------------------------------------

    private static string OversizedSpawnPrompt() =>
        "Work on card CARD-0025: Leaky launch\n\nDescription:\n" + new string('a', 2_000);

    [Test]
    public async Task An_oversized_spawn_prompt_is_typed_as_a_pointer_and_the_file_holds_the_original()
    {
        var adapter = new FakeAgentProtocolAdapter { PromptOutput = "working" };
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        var card = await fixture.CreateCardAsync();
        var prompt = OversizedSpawnPrompt();
        System.Text.Encoding.UTF8.GetByteCount(prompt).ShouldBeGreaterThan(900);

        var started = await fixture.StartCardSessionAsync(card, prompt);

        var typed = adapter.Prompts.ShouldHaveSingleItem();
        typed.ShouldContain(TypedBodySpill.PointerHeadline);
        typed.ShouldNotBe(prompt);

        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == started.SessionId);
        session.Status.ShouldBe(SessionStatus.Running);
        var attempt = await db.RunAttempts.SingleAsync(a => a.Id == started.RunAttemptId);
        attempt.Prompt.ShouldBe(prompt, "RunAttempt.Prompt is the work record, not the typed pointer");

        var stem = $"spawn-{started.SessionId.ToString("N")[..8]}";
        var spill = TypedBodySpill.InboxAbsolutePath(session.Cwd, stem);
        File.Exists(spill).ShouldBeTrue();
        File.ReadAllText(spill).ShouldBe(prompt);
        typed.ShouldContain(TypedBodySpill.InboxRelativePath(stem));
    }

    [Test]
    public async Task A_small_spawn_prompt_is_typed_whole_with_no_spill_file()
    {
        var adapter = new FakeAgentProtocolAdapter { PromptOutput = "working" };
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        var card = await fixture.CreateCardAsync();
        const string prompt = "do the work";

        var started = await fixture.StartCardSessionAsync(card, prompt);

        adapter.Prompts.ShouldBe([prompt]);
        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == started.SessionId);
        Directory.Exists(Path.Combine(session.Cwd, ".antiphon", "inbox")).ShouldBeFalse();
        (await db.RunAttempts.SingleAsync(a => a.Id == started.RunAttemptId)).Prompt.ShouldBe(prompt);
        session.Status.ShouldBe(SessionStatus.Running);
    }

    [Test]
    public async Task A_spill_file_write_failure_on_boot_types_the_original_and_does_not_block_launch()
    {
        var adapter = new FakeAgentProtocolAdapter { PromptOutput = "working" };
        await using var fixture = await LaunchFixture.CreateAsync(
            adapter,
            s => s.AddSingleton<IWorktreeManager>(new BlockAntiphonDirWorktreeManager()));
        var card = await fixture.CreateCardAsync();
        var prompt = OversizedSpawnPrompt();

        var started = await fixture.StartCardSessionAsync(card, prompt);

        adapter.Prompts.ShouldBe([prompt], "filesystem failure types the body inline rather than blocking");
        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == started.SessionId);
        session.Status.ShouldBe(SessionStatus.Running, "a spill-file failure must not fail the launch");
        (await db.RunAttempts.SingleAsync(a => a.Id == started.RunAttemptId)).Prompt.ShouldBe(prompt);
    }

    [Test]
    public async Task A_remote_control_name_on_a_non_capable_kind_types_nothing()
    {
        var adapter = new FakeAgentProtocolAdapter { PromptOutput = "working" };
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        var card = await fixture.CreateCardAsync();

        var started = await fixture.StartCardSessionAsync(
            card, "do the work", remoteControlName: "Card Agent", kind: AgentKind.Grok);

        adapter.Prompts.ShouldBe(["do the work"]);

        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == started.SessionId);
        session.AgentKind.ShouldBe(AgentKind.Grok);
        (await db.AgentIncidents.CountAsync(i =>
            i.Kind == AgentIncidentKind.RcDegraded
            && (i.SessionId == started.SessionId || i.AgentId == fixture.AgentId))).ShouldBe(0);
    }

    /// <summary>
    /// Creates the worktree, then plants a FILE at <c>.antiphon</c> so the spill helper cannot
    /// create <c>.antiphon/inbox/</c>.
    /// </summary>
    private sealed class BlockAntiphonDirWorktreeManager : IWorktreeManager
    {
        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct)
        {
            var worktreePath = Path.Combine(repoPath, $"card-{cardId}");
            Directory.CreateDirectory(worktreePath);
            File.WriteAllText(Path.Combine(worktreePath, ".antiphon"), "blocker");
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(
                new WorktreeInfo(cardId, repoPath, worktreePath, $"feat/card-{cardId}", baseRef, now, now));
        }

        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorktreeInfo>>([]);

        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task TouchAsync(string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task<int> PruneStaleAsync(CancellationToken ct) => Task.FromResult(0);
    }

    // ---- Fixture --------------------------------------------------------------------------------

    /// <summary>
    /// One agent, one cardless session in Starting, and (on demand) one card graph, over the shared
    /// <see cref="BridgeQueueHarness"/> service graph — which already carries the supervisor, alert
    /// and queue services the degraded path records through. The harness's own session is left
    /// alone; these tests launch into a session of their own so the adapter can register itself in
    /// the runtime without colliding with the harness's.
    /// </summary>
    private sealed class LaunchFixture : IAsyncDisposable
    {
        private readonly List<Guid> _projectIds = [];
        public required BridgeQueueHarness Harness { private get; init; }

        /// <summary>
        /// A scope of its OWN, created after the seeding below. The harness built its agent through
        /// its own scope, so that scope's DbContext still tracks the pre-launch agent row — and the
        /// ExecuteUpdate that puts the agent into the state a real Start API leaves behind bypasses
        /// the change tracker. Launching through the tracked context would read a stale Status and
        /// silently skip the rollback this suite asserts on.
        /// </summary>
        public required IServiceScope LaunchScope { private get; init; }
        public required Guid SessionId { get; init; }
        public required Guid AgentId { get; init; }
        public required string Workspace { get; init; }
        public AgentSessionRuntime Runtime => Harness.Runtime;
        public IServiceProvider Services => LaunchScope.ServiceProvider;

        public static Task<LaunchFixture> CreateAsync(params FakeAgentProtocolAdapter[] adapters) =>
            CreateAsync(new QueueAdapterFactory(adapters));

        public static Task<LaunchFixture> CreateAsync(
            IAgentProtocolAdapterFactory adapterFactory,
            Action<IServiceCollection>? extraServices = null)
        {
            return CreateCoreAsync(adapterFactory, extraServices);
        }

        public static Task<LaunchFixture> CreateAsync(
            FakeAgentProtocolAdapter adapter,
            Action<IServiceCollection> extraServices) =>
            CreateCoreAsync(new QueueAdapterFactory([adapter]), extraServices);

        private static async Task<LaunchFixture> CreateCoreAsync(
            IAgentProtocolAdapterFactory adapterFactory,
            Action<IServiceCollection>? extraServices)
        {
            var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
            {
                AlwaysOn = false,
                ConfigureServices = s =>
                {
                    s.AddSingleton(adapterFactory);
                    s.AddSingleton<IWorktreeManager>(new StubWorktreeManager());
                    s.AddSingleton<IOptions<AgentSessionSettings>>(Options.Create(new AgentSessionSettings
                    {
                        // Everything the boot sequence waits on, compressed: these suites are about
                        // what happens when a wait FAILS, so no test should pay a real timeout.
                        FirstDeltaTimeoutMs = 200,
                        KillGraceMs = 100,
                        RemoteControlArmTimeoutMs = 300,
                        // CARD-0292 S1: resume-bridge poll. Keep this well under the arm wait so
                        // an unarmed resume falls through without a 5s production window.
                        RemoteControlResumeProbeTimeoutMs = 50,
                        // Outer budget must still fit the production 3s first-output wait plus
                        // the compressed arm wait; 800ms was clipping late-confirm tests that
                        // emit no PromptOutput and therefore sit in WaitForFirstPromptOutputAsync.
                        RemoteControlSetupTimeoutMs = 8_000,
                        SessionLogPath = Path.Combine(Path.GetTempPath(), $"antiphon-launch-fail-{Guid.NewGuid():N}"),
                    }));
                    extraServices?.Invoke(s);
                },
            });

            var workspace = Path.Combine(harness.TempRoot, "launch-workspace");
            Directory.CreateDirectory(workspace);
            var sessionId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            await using (var db = CreateContext())
            {
                db.AgentSessions.Add(new AgentSession
                {
                    Id = sessionId,
                    CardId = null,
                    DefinitionName = "fake",
                    AgentKind = AgentKind.ClaudeCode,
                    Status = SessionStatus.Starting,
                    Cwd = workspace,
                    Cols = 120,
                    Rows = 30,
                    CreatedAt = now,
                    StartedAt = now,
                    LastSeenAt = now,
                });
                await db.SaveChangesAsync();
                // The state the Start API leaves behind before the background launch runs: the
                // agent is already Running and already points at the session being launched.
                await db.Agents.Where(a => a.Id == harness.AgentId).ExecuteUpdateAsync(u => u
                    .SetProperty(a => a.Status, AgentStatus.Running)
                    .SetProperty(a => a.PersistentSessionId, sessionId.ToString("D")));
            }

            return new LaunchFixture
            {
                Harness = harness,
                LaunchScope = harness.Provider.CreateScope(),
                SessionId = sessionId,
                AgentId = harness.AgentId,
                Workspace = workspace,
            };
        }

        public static AppDbContext CreateContext() => BridgeQueueHarness.CreateContext();

        public Task LaunchInteractiveAsync(
            string? remoteControlName = null,
            bool resume = false,
            LaunchNotes? notes = null,
            // CARD-0106 S2: lets a test hand the launch a spec the resolvers never saw, which is
            // exactly the "future forgotten path" the tripwire exists for.
            AgentLaunchSpec? spec = null,
            string? initialPrompt = null,
            CancellationToken ct = default) =>
            Services.GetRequiredService<AgentSessionService>().LaunchInteractiveAsync(
                SessionId,
                AgentId,
                spec ?? LaunchSpec(Workspace),
                remoteControlName,
                resume,
                notes,
                ct,
                initialPrompt);

        /// <summary>A project/board/card graph for the card-launch path. Returns the card id.</summary>
        public async Task<Guid> CreateCardAsync(string? description = null)
        {
            var repoPath = Path.Combine(Harness.TempRoot, "repo");
            Directory.CreateDirectory(repoPath);
            var now = DateTime.UtcNow;
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = $"Launch failure {Guid.NewGuid():N}",
                GitRepositoryUrl = "https://example.test/repo.git",
                LocalRepositoryPath = repoPath,
                BaseBranch = "main",
                CreatedAt = now,
                UpdatedAt = now,
            };
            await using (var db = CreateContext())
            {
                db.Projects.Add(project);
                await db.SaveChangesAsync();
            }
            _projectIds.Add(project.Id);

            var board = await Services.GetRequiredService<BoardService>()
                .CreateAsync(new CreateBoardRequest(project.Id, "Launch failures"), CancellationToken.None);
            var card = await Services.GetRequiredService<CardService>()
                .CreateAsync(board.Id, new CreateCardRequest(null, "Leaky launch", description), CancellationToken.None);
            return card.Id;
        }

        public async Task AssignAgentAsync(Guid cardId)
        {
            // Must mutate the launch-scope tracked card: CreateCardAsync loaded it on this
            // context, and StartAsync's LoadCardAsync would otherwise return the stale
            // AssignedAgentId=null instance (ExecuteUpdate on a second context does not
            // refresh tracked entities).
            var db = Services.GetRequiredService<AppDbContext>();
            var card = await db.Cards.FirstAsync(c => c.Id == cardId);
            card.AssignedAgentId = AgentId;
            await db.SaveChangesAsync();
        }

        public Task<AgentSessionStartResult> StartCardSessionAsync(
            Guid cardId,
            string prompt,
            string? remoteControlName = null,
            AgentKind kind = AgentKind.Raw,
            CancellationToken ct = default) =>
            Services.GetRequiredService<AgentSessionService>().StartAsync(
                new StartAgentSessionRequest(
                    cardId, "fake", kind, prompt, RemoteControlName: remoteControlName),
                LaunchSpec(Path.Combine(Harness.TempRoot, "repo"), kind),
                ct);

        private static AgentLaunchSpec LaunchSpec(string cwd, AgentKind kind = AgentKind.Raw) =>
            new("fake", kind, "fake", [], new Dictionary<string, string>(), cwd, 120, 30);

        /// <summary>
        /// Makes the adapter reachable as this session's live terminal and models a whole delivery
        /// round trip, not just the composer half: a real Claude records the submitted prompt in its
        /// JSONL and the tailer turns it into the UserPrompt row that CARD-0055 confirms against.
        /// Needed by any test that asserts the boot's launch note / queue flush actually landed.
        /// </summary>
        public void WireDelivery(FakeAgentProtocolAdapter adapter)
        {
            adapter.RegisterOnStart = Runtime;
            adapter.OnSubmitted = async submitted =>
            {
                await InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, submitted);
                await InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
            };
        }

        /// <summary>
        /// The production attempt count these tests run at — read from the settings object rather
        /// than restated, so a change to the default cannot leave the tests asserting the old one.
        /// (The harness compresses the DELAY between attempts to zero; the count is production's.)
        /// </summary>
        public static int BootPromptAttempts => new DeliveryVerificationSettings().BootPromptAttempts;

        public Task<long> InsertTranscriptEntryAsync(
            string kind, string? text = null, string? stopReason = null, DateTime? timestamp = null) =>
            Harness.InsertTranscriptEntryAsync(kind, text, stopReason, SessionId, timestamp);

        public Task<Guid> SeedPendingMessageAsync(string body) =>
            Harness.SeedPendingMessageAsync(body, SessionId);

        public async ValueTask DisposeAsync()
        {
            foreach (var projectId in _projectIds)
                await CleanupCardGraphAsync(projectId);

            await using (var db = CreateContext())
            {
                await db.SessionQueuedMessages.Where(m => m.AgentSessionId == SessionId).ExecuteDeleteAsync();
                await db.TranscriptEntries.Where(t => t.AgentSessionId == SessionId).ExecuteDeleteAsync();
                await db.AgentIncidents.Where(i => i.SessionId == SessionId).ExecuteDeleteAsync();
                await db.Alerts.Where(a => a.SessionId == SessionId).ExecuteDeleteAsync();
                await db.AgentSessions.Where(s => s.Id == SessionId).ExecuteDeleteAsync();
            }

            LaunchScope.Dispose();
            await Harness.DisposeAsync();
        }

        private static async Task CleanupCardGraphAsync(Guid projectId)
        {
            await using var db = CreateContext();
            var boardIds = await db.Boards.Where(b => b.ProjectId == projectId).Select(b => b.Id).ToListAsync();
            var cardIds = await db.Cards.Where(c => boardIds.Contains(c.BoardId)).Select(c => c.Id).ToListAsync();
            var sessionIds = await db.AgentSessions
                .Where(s => s.CardId != null && cardIds.Contains(s.CardId.Value))
                .Select(s => s.Id)
                .ToListAsync();

            // Null the cross-links first so the deletes below aren't blocked by FK constraints.
            await db.Cards.Where(c => cardIds.Contains(c.Id)).ExecuteUpdateAsync(u => u
                .SetProperty(c => c.OwnerSessionId, (Guid?)null)
                .SetProperty(c => c.CurrentWorktreeId, (Guid?)null)
                .SetProperty(c => c.AssignedAgentId, (Guid?)null)
                .SetProperty(c => c.ActiveWorkflowRunId, (Guid?)null));
            await db.SessionQueuedMessages.Where(m => sessionIds.Contains(m.AgentSessionId)).ExecuteDeleteAsync();
            await db.TranscriptEntries.Where(t => sessionIds.Contains(t.AgentSessionId)).ExecuteDeleteAsync();
            await db.AgentIncidents.Where(i => i.SessionId != null && sessionIds.Contains(i.SessionId.Value))
                .ExecuteDeleteAsync();
            await db.Alerts.Where(a => a.SessionId != null && sessionIds.Contains(a.SessionId.Value))
                .ExecuteDeleteAsync();
            await db.RunAttempts.Where(a => cardIds.Contains(a.CardId)).ExecuteDeleteAsync();
            await db.AgentSessions.Where(s => sessionIds.Contains(s.Id)).ExecuteDeleteAsync();
            await db.Worktrees.Where(w => cardIds.Contains(w.CardId)).ExecuteDeleteAsync();
            await db.Cards.Where(c => cardIds.Contains(c.Id)).ExecuteDeleteAsync();
            await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
            await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
            await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
            await db.Projects.Where(p => p.Id == projectId).ExecuteDeleteAsync();
        }
    }

    private sealed class QueueAdapterFactory(IEnumerable<IAgentProtocolAdapter> adapters) : IAgentProtocolAdapterFactory
    {
        private readonly Queue<IAgentProtocolAdapter> _adapters = new(adapters);

        public IAgentProtocolAdapter Create(AgentKind kind) =>
            _adapters.TryDequeue(out var adapter)
                ? adapter
                : throw new InvalidOperationException("No fake adapter was queued for this launch.");
    }

    /// <summary>Hands the SAME adapter to every launch — how a relaunch re-touches one process.</summary>
    private sealed class SharedAdapterFactory(IAgentProtocolAdapter adapter) : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) => adapter;
    }

    /// <summary>Creates the worktree directory and nothing else — no git in these tests.</summary>
    private sealed class StubWorktreeManager : IWorktreeManager
    {
        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct)
        {
            var worktreePath = Path.Combine(repoPath, $"card-{cardId}");
            Directory.CreateDirectory(worktreePath);
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(
                new WorktreeInfo(cardId, repoPath, worktreePath, $"feat/card-{cardId}", baseRef, now, now));
        }

        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorktreeInfo>>([]);

        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task TouchAsync(string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task<int> PruneStaleAsync(CancellationToken ct) => Task.FromResult(0);
    }
}
