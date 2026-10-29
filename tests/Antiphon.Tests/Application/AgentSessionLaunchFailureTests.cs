using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Infrastructure.Resilience;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
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
[Category("Slow")]
public class AgentSessionLaunchFailureTests
{
    [Test]
    [Arguments(AgentKind.Grok)]
    [Arguments(AgentKind.ClaudeCode)]
    public async Task Sign_in_failure_records_incident_without_waiting_on_its_own_agent_lock(AgentKind kind)
    {
        var home = Path.Combine(Path.GetTempPath(), $"signin-lock-{Guid.NewGuid():N}");
        const string reason = "Provider sign-in required; run login";
        var adapter = new FakeAgentProtocolAdapter
        {
            ReadyResult = false,
            LaunchBlock = new(AgentLaunchBlockKind.ProviderSignInRequired, reason, home),
        };
        var lockWitness = new SignInAgentLockWitness();
        // Bound the old code's cross-connection FK wait. Its incident writer times out,
        // leaving no incident even though the caller owns the blocking agent lock.
        await using var fixture = await LaunchFixture.CreateAsync(adapter,
            configureDb: options => options.AddInterceptors(lockWitness)
                .UseNpgsql(npgsql => npgsql.CommandTimeout(2)));
        await using var db = LaunchFixture.CreateContext();
        await db.AgentSessions.Where(s => s.Id == fixture.SessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.AgentKind, kind));

        await Should.ThrowAsync<AgentLaunchBlockedException>(fixture.LaunchInteractiveAsync());

        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.Status.ShouldBe(SessionStatus.Failed);
        session.LaunchBlock.ShouldBe(SessionLaunchBlock.ProviderSignInRequired);
        session.FailureReason.ShouldBe(reason);
        (await db.Agents.SingleAsync(a => a.Id == fixture.AgentId)).Status.ShouldBe(AgentStatus.Failed);
        adapter.Prompts.ShouldBeEmpty();
        adapter.Lifecycle.ShouldBe(["KillGeneration", "Dispose"]);
        lockWitness.Observed.ShouldBeTrue("failure handling must still exclude agent writers and deletion");
        var incident = await db.AgentIncidents.SingleOrDefaultAsync(i =>
            i.AgentId == fixture.AgentId && i.SessionId == fixture.SessionId
            && i.Kind == AgentIncidentKind.ProviderSignInRequired);
        incident.ShouldNotBeNull("the sign-in incident must persist before failure handling returns");
        incident.FailureReason.ShouldBe(GrokSignInIncident.EpisodeKey(home));
        (await db.Alerts.CountAsync(a => a.AgentId == fixture.AgentId
            && a.SessionId == fixture.SessionId && a.Severity == AlertSeverity.Critical)).ShouldBe(1);
    }

    private sealed class SignInAgentLockWitness : DbCommandInterceptor
    {
        public bool Observed { get; private set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.Contains("SELECT * FROM \"Agents\"", StringComparison.Ordinal)
                || !command.CommandText.Contains("FOR ", StringComparison.Ordinal))
                return result;

            var owner = (NpgsqlConnection)command.Connection!;
            await using var competing = owner.CloneWith(owner.ConnectionString);
            await competing.OpenAsync(cancellationToken);
            await using (var timeout = new NpgsqlCommand("SET lock_timeout = '100ms'", competing))
                await timeout.ExecuteNonQueryAsync(cancellationToken);
            foreach (var sql in new[]
            {
                "UPDATE \"Agents\" SET \"UpdatedAt\" = \"UpdatedAt\" WHERE \"Id\" = @id",
                "DELETE FROM \"Agents\" WHERE \"Id\" = @id",
            })
            {
                await using var writer = new NpgsqlCommand(sql, competing);
                writer.Parameters.AddWithValue("id", command.Parameters[0].Value!);
                var blocked = await Should.ThrowAsync<PostgresException>(
                    () => writer.ExecuteNonQueryAsync(cancellationToken));
                blocked.SqlState.ShouldBe(PostgresErrorCodes.LockNotAvailable);
            }
            Observed = true;
            return result;
        }
    }

    [Test]
    public Task Interactive_effort_dialog_block_persists_reason_and_cleanup() => VerifyEffortBlockAsync(false);

    [Test]
    public Task Card_effort_dialog_block_withholds_boot_and_cleans_up() => VerifyEffortBlockAsync(true);

    private static async Task VerifyEffortBlockAsync(bool cardLaunch)
    {
        const string reason = "requested=xhigh; current=xhigh; suggested=high; selected=Keep; Enter=3; settle deadline exhausted";
        var adapter = new FakeAgentProtocolAdapter { ReadyResult = false,
            LaunchBlock = new(AgentLaunchBlockKind.EffortDialogNotCleared, reason) };
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        Guid? card = cardLaunch ? await fixture.CreateCardAsync() : null;
        if (card is { } cardId)
            await Should.ThrowAsync<AgentLaunchBlockedException>(fixture.StartCardSessionAsync(cardId, "boot-must-not-be-sent", kind: AgentKind.ClaudeCode));
        else await Should.ThrowAsync<AgentLaunchBlockedException>(fixture.LaunchInteractiveAsync());
        adapter.Lifecycle.ShouldBe(cardLaunch ? ["Kill", "Dispose"] : ["KillGeneration", "Dispose"]);
        adapter.Prompts.ShouldBeEmpty();
        await using var db = LaunchFixture.CreateContext();
        var session = card is { } id ? await db.AgentSessions.SingleAsync(s => s.CardId == id)
            : await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.Status.ShouldBe(SessionStatus.Failed);
        session.LaunchBlock.ShouldBe(SessionLaunchBlock.EffortDialogNotCleared);
        session.FailureReason.ShouldBe(reason);
        session.TerminationSource.ShouldBe(SessionTerminationSource.SystemRequest);
        if (card is { } c) (await db.RunAttempts.SingleAsync(a => a.CardId == c)).Phase.ShouldBe(RunPhase.Failed);
    }

    [Test]
    public async Task Grok_failed_startup_stays_owned_and_recoverable_until_kill_is_confirmed()
    {
        var adapter = new FakeAgentProtocolAdapter { KillResult = false };
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        adapter.RegisterOnStart = fixture.Runtime;
        await fixture.LaunchInteractiveAsync();
        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.AgentKind = AgentKind.Grok;
        session.GrokRulesState = GrokRulesState.Failed;
        session.GrokRulesFailure = "grok_rules_initialization_failed: timeout";
        await db.SaveChangesAsync();
        var service = fixture.Services.GetRequiredService<AgentSessionService>();
        await service.FailRecoveredRulesStartupAsync(session.Id, CancellationToken.None);
        await db.Entry(session).ReloadAsync();
        session.Status.ShouldBe(SessionStatus.Running, "unconfirmed cleanup must stay visible to periodic recovery");
        session.EndedAt.ShouldBeNull();
        adapter.Exited.IsCompleted.ShouldBeFalse();
        adapter.KillCount.ShouldBe(1);
        adapter.KillResult = true;
        await service.FailRecoveredRulesStartupAsync(session.Id, CancellationToken.None);
        await db.Entry(session).ReloadAsync();
        session.Status.ShouldBe(SessionStatus.Failed);
        adapter.KillCount.ShouldBe(2);
        session.TerminationSource.ShouldBe(SessionTerminationSource.SystemRequest);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Grok_recovered_failed_barrier_reaps_only_uninitialized_startup(bool previouslyReady)
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        adapter.RegisterOnStart = fixture.Runtime;
        await fixture.LaunchInteractiveAsync();
        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.AgentKind = AgentKind.Grok;
        session.GrokRulesState = GrokRulesState.Failed;
        session.GrokRulesReadyAt = previouslyReady ? DateTime.UtcNow.AddMinutes(-1) : null;
        session.GrokRulesFailure = "grok_rules_initialization_failed: timeout";
        await db.SaveChangesAsync();
        await fixture.Services.GetRequiredService<AgentSessionService>()
            .FailRecoveredRulesStartupAsync(session.Id, CancellationToken.None);
        await db.Entry(session).ReloadAsync();
        adapter.Killed.ShouldBe(!previouslyReady);
        session.Status.ShouldBe(previouslyReady ? SessionStatus.Running : SessionStatus.Failed);
        if (!previouslyReady)
        {
            session.TerminationSource.ShouldBe(SessionTerminationSource.SystemRequest);
            session.FailureReason.ShouldBe("grok_rules_initialization_failed: timeout");
            session.EndedAt.ShouldNotBeNull();
        }
    }

    [Test]
    public async Task A_runner_detect_timeout_409_stamps_DetectTimeout_and_keeps_the_runner_prose()
    {
        await using var fixture = await LaunchFixture.CreateAsync(new FakeAgentProtocolAdapter
            { ThrowOnStart = HerdrSupervisionBackoffTests.Timeout() });
        await Should.ThrowAsync<ConflictException>(fixture.LaunchInteractiveAsync());
        await using var db = LaunchFixture.CreateContext();
        var row = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        row.Status.ShouldBe(SessionStatus.Failed);
        row.TerminationSource.ShouldBe(SessionTerminationSource.SystemRequest);
        row.HerdrSupervisionFailureKind.ShouldBe(HerdrSupervisionFailureKind.DetectTimeout);
        row.FailureReason.ShouldContain("did not detect agent kind");
    }

    [Test]
    public async Task A_runner_pane_occupied_409_on_a_row_stamps_NonQualifying()
    {
        await using var fixture = await LaunchFixture.CreateAsync(new FakeAgentProtocolAdapter
            { ThrowOnStart = new ConflictException("occupied", "pane_occupied") });
        await Should.ThrowAsync<ConflictException>(fixture.LaunchInteractiveAsync());
        await using var db = LaunchFixture.CreateContext();
        (await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId)).HerdrSupervisionFailureKind
            .ShouldBe(HerdrSupervisionFailureKind.NonQualifying);
    }

    [Test]
    public async Task Codex_command_line_refusal_409_persists_the_measured_length_and_limit_without_the_instructions()
    {
        const string sentinel = "C0497-LAUNCH-SENTINEL";
        var detail =
            "Session 00000000-0000-0000-0000-000000000001: codex_command_line_too_long: launcher node.exe codex.js measured 30,001 UTF-16 units against an effective budget of 30,000. Shrink the developer instructions or standing prompt; the runner refuses before creating a child.";
        var adapter = new FakeAgentProtocolAdapter
        {
            ThrowOnStart = new ConflictException(detail, CodexLaunchProblemTypes.CommandLineTooLong),
        };
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        var ex = await Should.ThrowAsync<ConflictException>(fixture.LaunchInteractiveAsync());
        ex.Code.ShouldBe(CodexLaunchProblemTypes.CommandLineTooLong);
        adapter.Started.ShouldBeFalse();
        adapter.Prompts.ShouldBeEmpty();
        await using var db = LaunchFixture.CreateContext();
        var row = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        row.Status.ShouldBe(SessionStatus.Failed);
        row.FailureReason.ShouldContain("codex_command_line_too_long");
        row.FailureReason.ShouldContain("30,001");
        row.FailureReason.ShouldContain("30,000");
        row.FailureReason.ShouldNotContain(sentinel);
        row.TerminationSource.ShouldBe(SessionTerminationSource.SystemRequest);
        row.HerdrSupervisionFailureKind.ShouldBe(HerdrSupervisionFailureKind.NonQualifying);
    }

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
        adapter.Lifecycle.ShouldBe(["KillGeneration", "Dispose"]);
        adapter.KillGenerationCalls.ShouldHaveSingleItem();

        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.Status.ShouldBe(SessionStatus.Failed);
        session.TerminationSource.ShouldBe(SessionTerminationSource.SystemRequest);
        var agent = await db.Agents.SingleAsync(a => a.Id == fixture.AgentId);
        agent.Status.ShouldBe(AgentStatus.Failed, "the Start API already flipped it to Running");
    }

    [Test]
    public async Task A_verification_binding_whose_generation_disagrees_with_the_launch_field_is_rejected_before_the_process_starts()
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        var now = DateTime.UtcNow;
        var binding = new VerificationExecutionBinding(
            Guid.NewGuid(),
            new(Guid.NewGuid(), Guid.NewGuid(), new string('a', 40)),
            new(fixture.SessionId, now.AddMinutes(1)),
            new(@"C:\repo", @"C:\repo\.git", @"C:\trees\snapshot", @"C:\repo\.git\worktrees\snapshot",
                "feat/card-task-12345678", Guid.NewGuid()),
            VerificationCustodyBackends.WindowsJob, RunnerStoreId: Guid.NewGuid());
        var spec = new AgentLaunchSpec(
            "fake", AgentKind.ClaudeCode, "fake", [], new Dictionary<string, string>(),
            fixture.Workspace, 120, 30, VerificationBinding: binding);

        var error = await Should.ThrowAsync<ConflictException>(() => fixture.LaunchInteractiveAsync(spec: spec));
        error.Code.ShouldBe(SessionGeneration.BindingMismatch);
        adapter.Started.ShouldBeFalse();

        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.Status.ShouldBe(SessionStatus.Failed);
        session.FailureReason.ShouldContain(SessionGeneration.BindingMismatch);
    }

    [Test]
    public async Task Interactive_sign_in_block_persists_LaunchBlock_and_the_named_reason()
    {
        var grokHome = Path.Combine(Path.GetTempPath(), $"antiphon-grok-home-{Guid.NewGuid():N}");
        var reason = GrokSignInPromptDetector.BlockReason(grokHome);
        var adapter = new FakeAgentProtocolAdapter
        {
            ReadyResult = false,
            LaunchBlock = new AgentLaunchBlock(
                AgentLaunchBlockKind.ProviderSignInRequired, reason, grokHome),
        };
        await using var fixture = await LaunchFixture.CreateAsync(adapter);

        var launch = fixture.LaunchInteractiveAsync();

        var ex = await Should.ThrowAsync<AgentLaunchBlockedException>(launch);
        ex.Block.Kind.ShouldBe(AgentLaunchBlockKind.ProviderSignInRequired);
        adapter.Lifecycle.ShouldBe(["KillGeneration", "Dispose"]);

        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.Status.ShouldBe(SessionStatus.Failed);
        session.LaunchBlock.ShouldBe(SessionLaunchBlock.ProviderSignInRequired);
        session.FailureReason.ShouldBe(reason);
        session.TerminationSource.ShouldBe(SessionTerminationSource.SystemRequest);
    }

    [Test]
    public async Task Interactive_trust_dialog_block_persists_LaunchBlock_and_the_named_reason()
    {
        var cwd = @"C:\logs\antiphon\diagnose";
        var reason = ClaudeBlockingPromptDetector.FormatTrustDialogNotClearedReason(
            cwd,
            ClaudeTrustDialogLayout.HighlightedList,
            "highlight never reached 'Yes, I trust this folder' after j/Down/Ctrl+N; Enter withheld");
        var adapter = new FakeAgentProtocolAdapter
        {
            ReadyResult = false,
            LaunchBlock = new AgentLaunchBlock(
                AgentLaunchBlockKind.TrustDialogNotCleared, reason),
        };
        await using var fixture = await LaunchFixture.CreateAsync(adapter);

        var launch = fixture.LaunchInteractiveAsync();

        var ex = await Should.ThrowAsync<AgentLaunchBlockedException>(launch);
        ex.Block.Kind.ShouldBe(AgentLaunchBlockKind.TrustDialogNotCleared);
        adapter.Lifecycle.ShouldBe(["KillGeneration", "Dispose"]);

        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.Status.ShouldBe(SessionStatus.Failed);
        session.LaunchBlock.ShouldBe(SessionLaunchBlock.TrustDialogNotCleared);
        session.FailureReason.ShouldBe(reason);
        session.TerminationSource.ShouldBe(SessionTerminationSource.SystemRequest);
        (await db.AgentIncidents.CountAsync(
            i => i.AgentId == fixture.AgentId && i.Kind == AgentIncidentKind.ProviderSignInRequired))
            .ShouldBe(0);
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
    /// Missing native history ends the one resume attempt and leaves an explicit durable decision.
    /// </summary>
    [Test]
    public async Task Resume_not_found_kills_the_process_and_holds_without_fallback()
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

        await using (var seed = LaunchFixture.CreateContext())
            await seed.AgentSessions.Where(s => s.Id == fixture.SessionId).ExecuteUpdateAsync(u => u
                .SetProperty(s => s.HerdrSupervisionFailureKind, HerdrSupervisionFailureKind.PaneClosed));

        await Should.ThrowAsync<AgentSessionService.ResumeTargetMissingException>(fixture.LaunchInteractiveAsync(resume: true));
        resumeAdapter.Lifecycle.ShouldBe(["KillGeneration", "Dispose"]);
        freshAdapter.Started.ShouldBeFalse();
        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.Status.ShouldBe(SessionStatus.Failed);
        session.RestartFailureKind.ShouldBe(RestartFailureKind.ContinuityUnavailable);
        session.InteractiveLaunchCompletedAt.ShouldBeNull();
        (await db.AgentSupervisionStates.SingleAsync(s => s.AgentId == fixture.AgentId)).ContinuityReason
            .ShouldBe(StandingContinuityReason.NativeSessionMissing);
    }

    [Test]
    public async Task Missing_resume_does_not_launch_a_second_process_or_invent_its_Herdr_timeout()
    {
        var resume = new FakeAgentProtocolAdapter { StartupOutput = "No conversation found with session ID: 0b5f2f7e", ReadyResult = false };
        var fresh = new FakeAgentProtocolAdapter { ThrowOnStart = HerdrSupervisionBackoffTests.Timeout() };
        await using var fixture = await LaunchFixture.CreateAsync(resume, fresh);
        await Should.ThrowAsync<AgentSessionService.ResumeTargetMissingException>(fixture.LaunchInteractiveAsync(resume: true));
        fresh.Started.ShouldBeFalse();
        await using var db = LaunchFixture.CreateContext();
        var row = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        row.Status.ShouldBe(SessionStatus.Failed);
        row.HerdrSupervisionFailureKind.ShouldBeNull();
        row.RestartFailureKind.ShouldBe(RestartFailureKind.ContinuityUnavailable);
    }

    /// <summary>
    /// Standing Grok passes strict resume to the runner even when the server sees no native directory.
    /// </summary>
    [Test]
    public async Task Standing_Grok_missing_local_directory_keeps_resume_for_the_authoritative_runner()
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        await SetSessionKindAsync(fixture.SessionId, AgentKind.Grok);

        var grokHome = Path.Combine(Path.GetTempPath(), $"antiphon-grok-home-{Guid.NewGuid():N}");
        Directory.CreateDirectory(grokHome);
        try
        {
            await fixture.LaunchInteractiveAsync(
                resume: true,
                spec: GrokSpec(fixture.Workspace, grokHome));

            adapter.Started.ShouldBeTrue();
            adapter.StartedArgs.ShouldContain("--resume");
            adapter.StartedArgs.ShouldContain(fixture.SessionId.ToString("D"));
            adapter.StartedArgs.ShouldNotContain("--session-id");
        }
        finally
        {
            try { Directory.Delete(grokHome, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// CARD-0383: runner 409 <c>herdr_grok_native_session_missing</c> on a Resume launch is the
    /// same continuity decision as Claude's missing conversation. Never create using the same id.
    /// </summary>
    [Test]
    public async Task Grok_runner_native_session_missing_holds_without_create()
    {
        var resumeAdapter = new FakeAgentProtocolAdapter
        {
            ThrowOnStart = new ConflictException(
                "refusing to type `--resume` : no grok session directory",
                HerdrProblemTypes.GrokNativeSessionMissing),
        };
        var freshAdapter = new FakeAgentProtocolAdapter();
        await using var fixture = await LaunchFixture.CreateAsync(resumeAdapter, freshAdapter);
        freshAdapter.RegisterOnStart = fixture.Runtime;
        await SetSessionKindAsync(fixture.SessionId, AgentKind.Grok);

        var grokHome = Path.Combine(Path.GetTempPath(), $"antiphon-grok-home-{Guid.NewGuid():N}");
        var encoded = Uri.EscapeDataString(Path.GetFullPath(fixture.Workspace));
        Directory.CreateDirectory(Path.Combine(grokHome, "sessions", encoded, fixture.SessionId.ToString("D")));
        try
        {
            await Should.ThrowAsync<AgentSessionService.ResumeTargetMissingException>(() => fixture.LaunchInteractiveAsync(
                resume: true, spec: GrokSpec(fixture.Workspace, grokHome)));
            resumeAdapter.Lifecycle.ShouldBe(["KillGeneration", "Dispose"]);
            freshAdapter.Started.ShouldBeFalse();
            await using var db = LaunchFixture.CreateContext();
            var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
            session.Status.ShouldBe(SessionStatus.Failed);
            session.RestartFailureKind.ShouldBe(RestartFailureKind.ContinuityUnavailable);
            (await db.AgentSupervisionStates.SingleAsync(s => s.AgentId == fixture.AgentId)).ContinuityReason
                .ShouldBe(StandingContinuityReason.NativeSessionMissing);
        }
        finally
        {
            try { Directory.Delete(grokHome, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// CARD-0383: a Grok row whose native directory exists (any cwd encoding) keeps <c>--resume</c>.
    /// </summary>
    [Test]
    public async Task Grok_resume_of_a_row_with_a_native_session_keeps_resume()
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        await SetSessionKindAsync(fixture.SessionId, AgentKind.Grok);

        var grokHome = Path.Combine(Path.GetTempPath(), $"antiphon-grok-home-{Guid.NewGuid():N}");
        var encoded = Uri.EscapeDataString(@"D:\src\OTHER-machine\repo");
        Directory.CreateDirectory(Path.Combine(grokHome, "sessions", encoded, fixture.SessionId.ToString("D")));
        try
        {
            await fixture.LaunchInteractiveAsync(
                resume: true,
                spec: GrokSpec(fixture.Workspace, grokHome));

            adapter.Started.ShouldBeTrue();
            adapter.StartedArgs.ShouldContain("--resume");
            adapter.StartedArgs.ShouldContain(fixture.SessionId.ToString("D"));
            adapter.StartedArgs.ShouldNotContain("--session-id");
        }
        finally
        {
            try { Directory.Delete(grokHome, recursive: true); } catch { /* best effort */ }
        }
    }

    [Test]
    public async Task Grok_raw_multiline_rules_in_a_launch_argument_refuses_the_launch_before_the_process_starts()
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        await SetSessionKindAsync(fixture.SessionId, AgentKind.Grok);
        var sentinel = "card0382-sentinel-" + Guid.NewGuid().ToString("N");
        var grokHome = Path.Combine(Path.GetTempPath(), $"antiphon-grok-home-{Guid.NewGuid():N}");
        Directory.CreateDirectory(grokHome);
        try
        {
            var spec = new AgentLaunchSpec(
                "fake", AgentKind.Grok, "fake",
                ["--always-approve", "--rules", "line one\nline two " + sentinel],
                new Dictionary<string, string> { ["GROK_HOME"] = grokHome },
                fixture.Workspace, 120, 30);

            // CARD-0382 is a Windows argv policy. Off Windows the same payload is delivered
            // verbatim (GrokRulesArgvPolicy returns null) and the process is allowed to start.
            if (OperatingSystem.IsWindows())
            {
                var ex = await Should.ThrowAsync<ConflictException>(
                    () => fixture.LaunchInteractiveAsync(spec: spec));
                ex.Code.ShouldBe(GrokRulesArgvPolicy.ProblemCode);
                adapter.Started.ShouldBeFalse();

                await using var db = LaunchFixture.CreateContext();
                var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
                session.Status.ShouldBe(SessionStatus.Failed);
                session.HerdrSupervisionFailureKind.ShouldBe(HerdrSupervisionFailureKind.NonQualifying);
                session.FailureReason.ShouldNotBeNull();
                session.FailureReason.ShouldContain(GrokRulesArgvPolicy.ProblemCode);
                session.FailureReason.ShouldContain("--rules");
                session.FailureReason.ShouldNotContain(sentinel);
                session.TerminationSource.ShouldBe(SessionTerminationSource.SystemRequest);
                session.EndedAt.ShouldNotBeNull();
            }
            else
            {
                await fixture.LaunchInteractiveAsync(spec: spec);
                adapter.Started.ShouldBeTrue();
                adapter.StartedArgs.ShouldContain("line one\nline two " + sentinel);

                await using var db = LaunchFixture.CreateContext();
                var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
                session.Status.ShouldBe(SessionStatus.Running);
                session.HerdrSupervisionFailureKind.ShouldBeNull();
                (session.FailureReason ?? "").ShouldNotContain(sentinel);
                session.FailureReason.ShouldBeNull();
                session.TerminationSource.ShouldBe(SessionTerminationSource.Unknown);
                session.EndedAt.ShouldBeNull();
            }
        }
        finally
        {
            try { Directory.Delete(grokHome, recursive: true); } catch { /* best effort */ }
        }
    }

    [Test]
    public async Task Grok_raw_multiline_rules_in_a_launch_argument_refuses_the_launch_before_the_process_starts_on_resume_too()
    {
        var resumeAdapter = new FakeAgentProtocolAdapter();
        var freshAdapter = new FakeAgentProtocolAdapter();
        await using var fixture = await LaunchFixture.CreateAsync(resumeAdapter, freshAdapter);
        await SetSessionKindAsync(fixture.SessionId, AgentKind.Grok);
        var sentinel = "card0382-sentinel-" + Guid.NewGuid().ToString("N");
        var grokHome = Path.Combine(Path.GetTempPath(), $"antiphon-grok-home-{Guid.NewGuid():N}");
        var encoded = Uri.EscapeDataString(Path.GetFullPath(fixture.Workspace));
        Directory.CreateDirectory(Path.Combine(grokHome, "sessions", encoded, fixture.SessionId.ToString("D")));
        try
        {
            var spec = new AgentLaunchSpec(
                "fake", AgentKind.Grok, "fake",
                ["--always-approve", "--rules", "line one\nline two " + sentinel],
                new Dictionary<string, string> { ["GROK_HOME"] = grokHome },
                fixture.Workspace, 120, 30);

            if (OperatingSystem.IsWindows())
            {
                var ex = await Should.ThrowAsync<ConflictException>(
                    () => fixture.LaunchInteractiveAsync(resume: true, spec: spec));
                ex.Code.ShouldBe(GrokRulesArgvPolicy.ProblemCode);
                resumeAdapter.Started.ShouldBeFalse();
                freshAdapter.Started.ShouldBeFalse();
            }
            else
            {
                await fixture.LaunchInteractiveAsync(resume: true, spec: spec);
                resumeAdapter.Started.ShouldBeTrue();
                freshAdapter.Started.ShouldBeFalse();
                resumeAdapter.StartedArgs.ShouldContain("line one\nline two " + sentinel);
                resumeAdapter.StartedArgs.ShouldContain("--resume");
            }
        }
        finally
        {
            try { Directory.Delete(grokHome, recursive: true); } catch { /* best effort */ }
        }
    }

    [Test]
    public async Task Grok_herdr_launch_whose_rules_come_from_an_env_token_is_refused_server_side()
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        await SetSessionKindAsync(fixture.SessionId, AgentKind.Grok);
        await SetSessionBackendAsync(fixture.SessionId, SessionBackend.Herdr);
        var sentinel = "card0382-sentinel-" + Guid.NewGuid().ToString("N");
        var grokHome = Path.Combine(Path.GetTempPath(), $"antiphon-grok-home-{Guid.NewGuid():N}");
        Directory.CreateDirectory(grokHome);
        try
        {
            var spec = new AgentLaunchSpec(
                "fake", AgentKind.Grok, "fake",
                ["--rules", "$env:GROK_RULES"],
                new Dictionary<string, string>
                {
                    ["GROK_HOME"] = grokHome,
                    ["GROK_RULES"] = "a\nb " + sentinel,
                },
                fixture.Workspace, 120, 30,
                Backend: SessionBackend.Herdr);

            if (OperatingSystem.IsWindows())
            {
                var ex = await Should.ThrowAsync<ConflictException>(
                    () => fixture.LaunchInteractiveAsync(spec: spec));
                ex.Code.ShouldBe(GrokRulesArgvPolicy.ProblemCode);
                ex.Message.ShouldContain(GrokRulesArgvPolicy.ReasonLineBreak);
                ex.Message.ShouldContain("GROK_RULES");
                ex.Message.ShouldNotContain(sentinel);
                adapter.Started.ShouldBeFalse();
            }
            else
            {
                // Herdr expands $env:NAME for the scan only. Off Windows that scan allows the
                // line break, and the launched argv stays the token — the secret is not copied in.
                // ShouldNotContain compares whole arguments, so exclusion is an ordinal substring.
                await fixture.LaunchInteractiveAsync(spec: spec);
                adapter.Started.ShouldBeTrue();
                adapter.StartedArgs.ShouldContain("$env:GROK_RULES");
                var secretArgument = adapter.StartedArgs.FirstOrDefault(
                    argument => argument.Contains(sentinel, StringComparison.Ordinal));
                adapter.StartedArgs
                    .Any(argument => argument.Contains(sentinel, StringComparison.Ordinal))
                    .ShouldBeFalse("argv copied the rules secret into an argument: " + secretArgument);
                string.Join('\n', adapter.StartedArgs).ShouldNotContain(GrokRulesArgvPolicy.ProblemCode);
            }
        }
        finally
        {
            try { Directory.Delete(grokHome, recursive: true); } catch { /* best effort */ }
        }
    }

    [Test]
    public async Task Grok_pty_host_launch_with_an_env_token_value_is_not_expanded_and_passes_the_rules_guard()
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        await SetSessionKindAsync(fixture.SessionId, AgentKind.Grok);
        var grokHome = Path.Combine(Path.GetTempPath(), $"antiphon-grok-home-{Guid.NewGuid():N}");
        Directory.CreateDirectory(grokHome);
        try
        {
            var spec = new AgentLaunchSpec(
                "fake", AgentKind.Grok, "fake",
                ["--rules", "$env:GROK_RULES"],
                new Dictionary<string, string>
                {
                    ["GROK_HOME"] = grokHome,
                    ["GROK_RULES"] = "a\nb card0382-not-expanded",
                },
                fixture.Workspace, 120, 30);

            await fixture.LaunchInteractiveAsync(spec: spec);
            adapter.Started.ShouldBeTrue();
            adapter.StartedArgs.ShouldContain("$env:GROK_RULES");
        }
        finally
        {
            try { Directory.Delete(grokHome, recursive: true); } catch { /* best effort */ }
        }
    }

    [Test]
    public async Task Grok_rules_equals_form_and_append_system_prompt_alias_are_refused()
    {
        await AssertGrokRawArgsRefused(["--rules=a\nb"]);
        await AssertGrokRawArgsRefused(["--append-system-prompt", "a\r\nb"]);
    }

    /// <summary>
    /// A runner that already reaped the failed process returns false from Kill; disposal still runs.
    /// </summary>
    [Test]
    public async Task Killing_an_already_reaped_missing_resume_is_harmless()
    {
        var adapter = new FakeAgentProtocolAdapter
        {
            StartupOutput = "No conversation found with session ID: 0b5f2f7e",
            ReadyResult = false,
            KillResult = false,
        };
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        await Should.ThrowAsync<AgentSessionService.ResumeTargetMissingException>(fixture.LaunchInteractiveAsync(resume: true));
        adapter.KillGenerationCalls.ShouldNotBeEmpty();
        adapter.Lifecycle.ShouldBe(["KillGeneration", "Dispose"]);
        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.Status.ShouldBe(SessionStatus.Failed);
        session.RestartFailureKind.ShouldBe(RestartFailureKind.ContinuityUnavailable);
    }

    /// <summary>
    /// CARD-0312 S3 / CARD-0311 item 1. "Agent process did not become ready." was true and useless
    /// on the Antiphon-Orchestrator resume: the first /start failed the 60-second readiness budget,
    /// the second succeeded, and the 168% context that explained both was nowhere in the sentence a
    /// human read. Rung 1 measures a process painting a screen, so when the session's fullness is
    /// known the message names it.
    /// </summary>
    [Test]
    public async Task A_ready_failure_names_the_context_fullness_that_explains_it()
    {
        var adapter = new FakeAgentProtocolAdapter { ReadyResult = false };
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        await fixture.SeedContextUsageAsync(inputTokens: 336_000);

        await Should.ThrowAsync<InvalidOperationException>(fixture.LaunchInteractiveAsync());

        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.FailureReason.ShouldNotBeNull();
        session.FailureReason.ShouldStartWith("Agent process did not become ready (resuming a session at ");
        session.FailureReason.ShouldContain("168", customMessage:
            "336 000 tokens against the 200 000 default ceiling is 168% — the whole explanation");
    }

    /// <summary>
    /// CARD-1190: a ready-gate kill (exit code 0, KilledByRequest) and the launch-failure write
    /// both lock the session. The launch catch takes the session first, matching
    /// CloseSessionOnExitAsync. The exit observer can commit ProcessExit for that kill before
    /// the catch reloads. That stamp is cleared only when the kill result, generation, and stamp
    /// time all belong to this cleanup, so the not-ready error sticks.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    public async Task A_ready_gate_kill_records_the_not_ready_error_instead_of_ProcessExit(
        CancellationToken cancellationToken)
    {
        var adapter = new FakeAgentProtocolAdapter { ReadyResult = false };
        var barrier = new ConfirmedLockBarrier { HoldConfirmedSession = true };
        await using var fixture = await LaunchFixture.CreateAsync(adapter, barrier.Configure);
        Task<SessionExitDisposition>? exit = null;
        var sessionLockHeld = false;
        string? sessionLockDetail = null;
        adapter.OnKillGeneration = async () =>
        {
            exit = SessionExitObservation.ObserveMatchingAsync(
                fixture.Runtime, fixture.SessionId, exitCode: 0, AgentExitReason.KilledByRequest,
                LaunchFixture.CreateContext);
            // SessionLockConfirmed is set from ReaderExecutedAsync, after SELECT ... FOR UPDATE
            // has returned. HoldConfirmedSession keeps that transaction open for the probe.
            // A throw here is swallowed by launch cleanup, so the result is asserted below.
            await barrier.SessionLockConfirmed.WaitAsync(TimeSpan.FromSeconds(10));
            try
            {
                sessionLockHeld = await SessionTupleLockHeldAsync(fixture.SessionId);
                if (!sessionLockHeld)
                    sessionLockDetail = await DescribeSessionLocksAsync(fixture.SessionId);
            }
            catch (Exception ex)
            {
                sessionLockDetail = ex.ToString();
            }
            finally
            {
                barrier.ReleaseSessionHold();
            }

            await exit;
        };

        var thrown = await Should.ThrowAsync<Exception>(() => fixture.LaunchInteractiveAsync(ct: cancellationToken));
        var postgres = FindPostgres(thrown);
        postgres.ShouldBeNull();
        thrown.GetBaseException().Message.ShouldContain(AgentSessionService.NotReadyBase);

        exit.ShouldNotBeNull();
        (await exit!).ShouldBe(SessionExitDisposition.Applied);
        barrier.ConfirmedSessionAfterExecute.ShouldBeTrue();
        sessionLockDetail.ShouldBeNull(sessionLockDetail ?? "");
        sessionLockHeld.ShouldBeTrue(
            "the session row lock is confirmed only after SELECT ... FOR UPDATE has returned");

        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.Status.ShouldBe(SessionStatus.Failed);
        session.FailureReason.ShouldBe(AgentSessionService.NotReadyBase);
        session.TerminationSource.ShouldBe(SessionTerminationSource.SystemRequest);
        var verdict = AgentTaskLiveness.ClassifyFailure(
            fixture.SessionId,
            new AgentTaskLiveness.SessionSnapshot(
                session.Status, session.EndedAt, session.FailureReason,
                session.TerminationSource, session.ExitCode, session.LaunchBlock),
            hasTranscriptEntries: false);
        verdict.FailureCode.ShouldNotBe(AgentTaskFailureCode.StoppedBeforeFirstPrompt);
        verdict.Reason.ShouldContain(AgentSessionService.NotReadyBase);
        verdict.Reason.ShouldNotContain("ProcessExit");
        verdict.Reason.ShouldNotContain("exited on its own");
    }

    /// <summary>
    /// CARD-1190: a ProcessExit whose time predates this cleanup's kill is an earlier crash.
    /// The not-ready failure still throws, and the row keeps that stamp.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    public async Task A_ready_gate_kill_leaves_an_earlier_crash_stamp(CancellationToken cancellationToken)
    {
        var adapter = new FakeAgentProtocolAdapter { ReadyResult = false };
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        var crashedAt = DateTime.UtcNow.AddMinutes(-10);
        const string crash = "Process exited (ProcessExited, code 1).";
        await using (var seed = LaunchFixture.CreateContext())
        {
            var row = await seed.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
            row.TerminationSource = SessionTerminationSource.ProcessExit;
            row.FailureReason = crash;
            row.ExitCode = 1;
            row.EndedAt = crashedAt;
            await seed.SaveChangesAsync();
        }

        var thrown = await Should.ThrowAsync<Exception>(() => fixture.LaunchInteractiveAsync(ct: cancellationToken));
        thrown.GetBaseException().Message.ShouldContain(AgentSessionService.NotReadyBase);

        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.TerminationSource.ShouldBe(SessionTerminationSource.ProcessExit);
        session.FailureReason.ShouldBe(crash);
        session.ExitCode.ShouldBe(1);
        session.EndedAt.ShouldNotBeNull();
        session.EndedAt!.Value.ShouldBe(crashedAt, TimeSpan.FromSeconds(1));
        var agent = await db.Agents.SingleAsync(a => a.Id == fixture.AgentId);
        agent.Status.ShouldBe(AgentStatus.Failed);
    }

    /// <summary>
    /// CARD-1190: a kill-shaped ProcessExit stamped in the moment before this cleanup issues
    /// the kill is still an earlier exit. Dropping the issued-at comparison would rewrite it.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    public async Task A_ready_gate_kill_leaves_a_stamp_from_just_before_the_kill(CancellationToken cancellationToken)
    {
        var adapter = new FakeAgentProtocolAdapter
        {
            ReadyResult = false,
            ExitReason = AgentExitReason.KilledByRequest,
            ExitCode = 0,
        };
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        var stampedAt = DateTime.UtcNow.AddMilliseconds(-5);
        await using (var seed = LaunchFixture.CreateContext())
        {
            var row = await seed.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
            row.TerminationSource = SessionTerminationSource.ProcessExit;
            row.FailureReason = null;
            row.ExitCode = 0;
            row.EndedAt = stampedAt;
            await seed.SaveChangesAsync();
        }

        var thrown = await Should.ThrowAsync<Exception>(() => fixture.LaunchInteractiveAsync(ct: cancellationToken));
        thrown.GetBaseException().Message.ShouldContain(AgentSessionService.NotReadyBase);

        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.TerminationSource.ShouldBe(SessionTerminationSource.ProcessExit);
        session.FailureReason.ShouldBeNull();
        session.ExitCode.ShouldBe(0);
        session.EndedAt.ShouldNotBeNull();
        session.EndedAt!.Value.ShouldBe(stampedAt, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// CARD-1190: a natural crash observed while the ready-gate kill is in flight shares the
    /// kill's generation and is stamped at observation time, which is after the kill was issued.
    /// The exit code matches the kill result on purpose: ownership has to reject the
    /// ProcessExited cause, not a code mismatch.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    public async Task A_ready_gate_kill_leaves_an_in_window_natural_crash(CancellationToken cancellationToken)
    {
        var adapter = new FakeAgentProtocolAdapter { ReadyResult = false, ExitCode = 1 };
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        const string crash = "Process exited (ProcessExited, code 1).";
        adapter.OnKillGeneration = async () =>
        {
            adapter.ExitReason = AgentExitReason.ProcessExited;
            adapter.ExitCode = 1;
            await SessionExitObservation.ObserveMatchingAsync(
                fixture.Runtime, fixture.SessionId, exitCode: 1, AgentExitReason.ProcessExited,
                LaunchFixture.CreateContext);
        };

        var thrown = await Should.ThrowAsync<Exception>(() => fixture.LaunchInteractiveAsync(ct: cancellationToken));
        thrown.GetBaseException().Message.ShouldContain(AgentSessionService.NotReadyBase);

        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.TerminationSource.ShouldBe(SessionTerminationSource.ProcessExit);
        session.FailureReason.ShouldBe(crash);
        session.ExitCode.ShouldBe(1);
    }

    /// <summary>
    /// CARD-1190: a kill that does not report this generation dead must not clear a ProcessExit
    /// even when the stamp's time is after the kill was issued.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    public async Task A_ready_gate_kill_leaves_a_stamp_the_kill_did_not_produce(CancellationToken cancellationToken)
    {
        var adapter = new FakeAgentProtocolAdapter { ReadyResult = false, KillResult = false };
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        var stampedAt = DateTime.UtcNow.AddMinutes(30);
        const string crash = "Process exited (ProcessExited, code 7).";
        await using (var seed = LaunchFixture.CreateContext())
        {
            var row = await seed.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
            row.TerminationSource = SessionTerminationSource.ProcessExit;
            row.FailureReason = crash;
            row.ExitCode = 7;
            row.EndedAt = stampedAt;
            await seed.SaveChangesAsync();
        }

        var thrown = await Should.ThrowAsync<Exception>(() => fixture.LaunchInteractiveAsync(ct: cancellationToken));
        thrown.GetBaseException().Message.ShouldContain(AgentSessionService.NotReadyBase);

        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.TerminationSource.ShouldBe(SessionTerminationSource.ProcessExit);
        session.FailureReason.ShouldBe(crash);
        session.ExitCode.ShouldBe(7);
    }

    /// <summary>
    /// CARD-1190: a ProcessExit written for a different generation than the one this cleanup
    /// killed stays on the row.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    public async Task A_ready_gate_kill_leaves_a_stamp_from_another_generation(CancellationToken cancellationToken)
    {
        var adapter = new FakeAgentProtocolAdapter { ReadyResult = false };
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        const string crash = "earlier generation crashed";
        adapter.OnKillGeneration = async () =>
        {
            await using var db = LaunchFixture.CreateContext();
            var row = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
            row.StartedAt = SessionGeneration.Next(row.StartedAt, DateTime.UtcNow.AddHours(1));
            row.TerminationSource = SessionTerminationSource.ProcessExit;
            row.FailureReason = crash;
            row.ExitCode = 9;
            row.EndedAt = row.StartedAt.AddMinutes(-5);
            row.Status = SessionStatus.Failed;
            await db.SaveChangesAsync();
        };

        await fixture.LaunchInteractiveAsync(ct: cancellationToken);

        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.TerminationSource.ShouldBe(SessionTerminationSource.ProcessExit);
        session.FailureReason.ShouldBe(crash);
        session.ExitCode.ShouldBe(9);
        session.Status.ShouldBe(SessionStatus.Failed);
    }

    /// <summary>
    /// CARD-1190: a user stop locks the session and then the agent, the same order as the
    /// launch-failure catch. The barrier confirms each lock only after its statement returns,
    /// then holds the first lock until the other writer has confirmed its first lock. The old
    /// agent-then-session stop deadlocks (40P01). This order does not.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    public async Task A_ready_gate_stop_race_does_not_deadlock(CancellationToken cancellationToken)
    {
        var adapter = new FakeAgentProtocolAdapter { ReadyResult = false };
        var barrier = new ConfirmedLockBarrier { CrossWait = true };
        await using var fixture = await LaunchFixture.CreateAsync(adapter, barrier.Configure);
        adapter.RegisterOnStart = fixture.Runtime;
        Exception? stopFailure = null;
        Task? stop = null;
        adapter.OnKillGeneration = async () =>
        {
            stop = Task.Run(async () =>
            {
                try
                {
                    await using var scope = fixture.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<AgentControlService>()
                        .StopAsync(fixture.AgentId, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    stopFailure = ex;
                }
            });
            await barrier.FirstLockConfirmed.WaitAsync(TimeSpan.FromSeconds(10));
        };

        var thrown = await Should.ThrowAsync<Exception>(() => fixture.LaunchInteractiveAsync(ct: cancellationToken));
        FindPostgres(thrown).ShouldBeNull();
        stop.ShouldNotBeNull();
        await stop!;
        FindPostgres(stopFailure).ShouldBeNull();
        barrier.ConfirmedSessionAfterExecute.ShouldBeTrue();
        barrier.ConfirmedAgentAfterExecute.ShouldBeTrue();

        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.Status.ShouldBeOneOf(SessionStatus.Failed, SessionStatus.Stopped);
    }

    /// <summary>
    /// CARD-1190: a specialist delete whose session row is Failed but whose runtime is still
    /// live used to hold the agent row while KillAsync wrote the session. Stop holds the
    /// session and then waits for the agent. Start Stop, wait until its session lock has
    /// returned, then delete. The old order deadlocks (40P01).
    /// </summary>
    [Test]
    [Timeout(60_000)]
    public async Task A_ready_gate_stop_versus_delete_does_not_deadlock(CancellationToken cancellationToken)
    {
        var adapter = new FakeAgentProtocolAdapter();
        var barrier = new ConfirmedLockBarrier { CrossWait = true };
        // The harness registers AgentSessionService but not IDelegateSessionStopper, so Delete
        // would skip the kill and 409 while the registered adapter is still live. Production
        // wires the same factory (Program.cs).
        await using var fixture = await LaunchFixture.CreateAsync(adapter, barrier.Configure, services =>
            services.AddScoped<IDelegateSessionStopper>(sp => sp.GetRequiredService<AgentSessionService>()));
        fixture.Runtime.Register(fixture.SessionId, adapter);
        await using (var seed = LaunchFixture.CreateContext())
        {
            var agent = await seed.Agents.SingleAsync(a => a.Id == fixture.AgentId);
            agent.StandingSpecialistRole = AgentTaskRole.Check;
            agent.StandingSpecialistOwnerId = agent.Id;
            var session = await seed.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
            session.Status = SessionStatus.Failed;
            session.EndedAt = DateTime.UtcNow;
            session.TerminationSource = SessionTerminationSource.ProcessExit;
            session.ExitCode = 1;
            session.FailureReason = "Process exited (ProcessExited, code 1).";
            await seed.SaveChangesAsync();
        }

        Exception? stopFailure = null;
        var stop = Task.Run(async () =>
        {
            try
            {
                await using var scope = fixture.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AgentControlService>()
                    .StopAsync(fixture.AgentId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                stopFailure = ex;
            }
        }, cancellationToken);

        (await Task.WhenAny(barrier.SessionLockConfirmed, Task.Delay(TimeSpan.FromSeconds(15), cancellationToken)))
            .ShouldBe(barrier.SessionLockConfirmed);

        Exception? deleteFailure = null;
        var delete = Task.Run(async () =>
        {
            try
            {
                await using var scope = fixture.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AgentService>()
                    .DeleteAsync(fixture.AgentId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                deleteFailure = ex;
            }
        }, cancellationToken);

        await Task.WhenAll(stop, delete);
        // KillAsync swallows a 40P01 and Delete then throws agent_delete_session_live. Either
        // that conflict or a postgres exception on Stop is the cycle. A missing agent after a
        // successful delete is a NotFound from Stop's refetch, which is not the cycle.
        if (stopFailure is not null)
            stopFailure.ShouldBeOfType<NotFoundException>(stopFailure.ToString());
        deleteFailure.ShouldBeNull(deleteFailure?.ToString() ?? "");
        FindPostgres(stopFailure).ShouldBeNull();
        FindPostgres(deleteFailure).ShouldBeNull();
        barrier.ConfirmedSessionAfterExecute.ShouldBeTrue();
        barrier.ConfirmedAgentAfterExecute.ShouldBeTrue();
        adapter.Killed.ShouldBeTrue();

        await using var db = LaunchFixture.CreateContext();
        (await db.Agents.AnyAsync(a => a.Id == fixture.AgentId)).ShouldBeFalse();
    }

    /// <summary>
    /// CARD-1190: after the specialist delete commits its locks, Start can resume the same
    /// session id onto a new generation before the kill. The kill must not hit that generation,
    /// and the delete aborts instead of removing the agent.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    public async Task A_ready_gate_delete_does_not_kill_a_generation_started_in_the_unlocked_gap(
        CancellationToken cancellationToken)
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var fixture = await LaunchFixture.CreateAsync(adapter, services =>
            services.AddScoped<IDelegateSessionStopper>(sp => sp.GetRequiredService<AgentSessionService>()));
        fixture.Runtime.Register(fixture.SessionId, adapter);
        DateTime originalGeneration;
        await using (var seed = LaunchFixture.CreateContext())
        {
            var agent = await seed.Agents.SingleAsync(a => a.Id == fixture.AgentId);
            agent.StandingSpecialistRole = AgentTaskRole.Check;
            agent.StandingSpecialistOwnerId = agent.Id;
            var session = await seed.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
            session.Status = SessionStatus.Failed;
            session.EndedAt = DateTime.UtcNow;
            session.TerminationSource = SessionTerminationSource.ProcessExit;
            session.ExitCode = 1;
            session.FailureReason = "Process exited (ProcessExited, code 1).";
            originalGeneration = session.StartedAt;
            await seed.SaveChangesAsync();
        }

        await using var scope = fixture.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        var agents = scope.ServiceProvider.GetRequiredService<AgentService>();
        var resumedGeneration = SessionGeneration.Next(originalGeneration, DateTime.UtcNow.AddMinutes(5));
        agents.SpecialistDeleteBeforeUnlockedKill = async ct =>
        {
            await using var db = LaunchFixture.CreateContext();
            var row = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId, ct);
            row.StartedAt = resumedGeneration;
            row.Status = SessionStatus.Starting;
            row.TerminationSource = SessionTerminationSource.Unknown;
            row.EndedAt = null;
            row.ExitCode = null;
            row.FailureReason = null;
            await db.SaveChangesAsync(ct);
            adapter.StartedAcceptedGeneration = resumedGeneration;
        };

        var conflict = await Should.ThrowAsync<ConflictException>(() =>
            agents.DeleteAsync(fixture.AgentId, cancellationToken));
        conflict.Code.ShouldBe("specialist_delete_generation_changed");
        adapter.Killed.ShouldBeFalse();
        adapter.KillCount.ShouldBe(0);
        adapter.Lifecycle.ShouldBeEmpty();

        await using var verify = LaunchFixture.CreateContext();
        (await verify.Agents.AnyAsync(a => a.Id == fixture.AgentId)).ShouldBeTrue();
        var kept = await verify.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        SessionGeneration.Equal(kept.StartedAt, resumedGeneration).ShouldBeTrue();
        kept.Status.ShouldBe(SessionStatus.Starting);
        kept.TerminationSource.ShouldBe(SessionTerminationSource.Unknown);
    }

    /// <summary>
    /// CARD-1190: a natural exit code 0 can be stamped before the adapter watcher reports a
    /// cause. The kill's boolean and an Unknown reason must not rewrite that stamp into
    /// "Agent process did not become ready."
    /// </summary>
    [Test]
    [Timeout(60_000)]
    public async Task A_ready_gate_kill_leaves_a_natural_zero_exit_when_the_watcher_is_late(
        CancellationToken cancellationToken)
    {
        var adapter = new FakeAgentProtocolAdapter { ReadyResult = false, DelayExitWatcher = true };
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        SessionExitDisposition? observed = null;
        adapter.OnKillGeneration = async () =>
        {
            observed = await SessionExitObservation.ObserveMatchingAsync(
                fixture.Runtime, fixture.SessionId, exitCode: 0, AgentExitReason.ProcessExited,
                LaunchFixture.CreateContext);
        };

        var thrown = await Should.ThrowAsync<Exception>(() => fixture.LaunchInteractiveAsync(ct: cancellationToken));
        thrown.GetBaseException().Message.ShouldContain(AgentSessionService.NotReadyBase);
        observed.ShouldBe(SessionExitDisposition.Applied);
        adapter.ExitReason.ShouldBe(AgentExitReason.Unknown);

        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.TerminationSource.ShouldBe(SessionTerminationSource.ProcessExit);
        session.FailureReason.ShouldBeNull();
        session.ExitCode.ShouldBe(0);
        session.Status.ShouldBe(SessionStatus.Stopped);
    }

    /// <summary>
    /// True when another transaction already holds this session row. A granted
    /// <c>SELECT … FOR UPDATE</c> is recorded on the tuple header; pg_locks lists that lock
    /// only while someone is waiting for it, so a global tuple-lock count cannot see it.
    /// <c>FOR UPDATE NOWAIT</c> on this id conflicts (55P03) exactly when the row is held.
    /// </summary>
    private static async Task<bool> SessionTupleLockHeldAsync(Guid sessionId)
    {
        await using var db = LaunchFixture.CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
            command.CommandText = """SELECT "Id" FROM "AgentSessions" WHERE "Id" = @id FOR UPDATE NOWAIT""";
            var id = command.CreateParameter();
            id.ParameterName = "id";
            id.Value = sessionId;
            command.Parameters.Add(id);
            try
            {
                await command.ExecuteScalarAsync();
                return false;
            }
            catch (Exception ex) when (FindPostgres(ex)?.SqlState == PostgresErrorCodes.LockNotAvailable)
            {
                return true;
            }
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static async Task<string> DescribeSessionLocksAsync(Guid sessionId)
    {
        await using var db = LaunchFixture.CreateContext();
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText =
                """
                SELECT coalesce(string_agg(line, ' | '), '')
                FROM (
                  SELECT 'ctid=' || s.ctid::text || ' xmax=' || s.xmax::text AS line
                  FROM "AgentSessions" s
                  WHERE s."Id" = @id
                  UNION ALL
                  SELECT l.locktype || ':' || l.mode || ':granted=' || l.granted
                    || ':state=' || coalesce(a.state, '')
                    || ':page=' || coalesce(l.page::text, '')
                    || ':tuple=' || coalesce(l.tuple::text, '')
                    || ':q=' || left(regexp_replace(coalesce(a.query, ''), '\s+', ' ', 'g'), 80)
                  FROM pg_locks l
                  LEFT JOIN pg_stat_activity a ON a.pid = l.pid
                  LEFT JOIN pg_class c ON c.oid = l.relation
                  WHERE c.relname = 'AgentSessions'
                ) probes
                """;
            var id = command.CreateParameter();
            id.ParameterName = "id";
            id.Value = sessionId;
            command.Parameters.Add(id);
            var result = await command.ExecuteScalarAsync();
            return result as string ?? "";
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static PostgresException? FindPostgres(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
                return postgres;
        }

        return null;
    }

    /// <summary>
    /// Confirms a row lock only after the locking statement has returned, while its transaction
    /// is still open. With <see cref="CrossWait"/>, a connection that already holds one of the
    /// two rows waits (bounded) until another connection has confirmed the other row, and only
    /// then issues its second lock. That is a real crossing when the writers disagree on order,
    /// and a timeout when both take the session first.
    /// </summary>
    private sealed class ConfirmedLockBarrier : DbCommandInterceptor
    {
        [Flags]
        private enum Held
        {
            None = 0,
            Session = 1,
            Agent = 2,
        }

        private readonly ConcurrentDictionary<DbConnection, Held> _held = new();
        private readonly TaskCompletionSource _sessionLockConfirmed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _agentLockConfirmed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _firstLockConfirmed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource _sessionHoldRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SessionLockConfirmed => _sessionLockConfirmed.Task;
        public Task FirstLockConfirmed => _firstLockConfirmed.Task;
        public bool ConfirmedSessionAfterExecute { get; private set; }
        public bool ConfirmedAgentAfterExecute { get; private set; }

        /// <summary>
        /// When set, the second lock on a connection waits until the opposite row is confirmed
        /// held by someone. Used by the stop/launch race. Off for the single-observer test.
        /// </summary>
        public bool CrossWait { get; init; }

        /// <summary>
        /// When set, the session-lock confirmation waits until <see cref="ReleaseSessionHold"/>
        /// so a probe can see the granted tuple lock before the observer commits.
        /// </summary>
        public bool HoldConfirmedSession { get; init; }

        public void ReleaseSessionHold() => _sessionHoldRelease.TrySetResult();

        public void Configure(DbContextOptionsBuilder builder) => builder.AddInterceptors(this);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            var kind = Classify(command);
            if (kind != Held.None && InExplicitTransaction(eventData))
                await WaitForOppositeAsync(command, kind, cancellationToken);

            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            var reader = await base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
            var kind = Classify(command);
            if (kind != Held.None && InExplicitTransaction(eventData))
            {
                NoteConfirmed(command, kind);
                await HoldConfirmedSessionAsync(kind, cancellationToken);
            }

            return reader;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            // ExecuteUpdate of the session row takes its lock without SELECT ... FOR UPDATE.
            var kind = IsSessionUpdate(command) ? Held.Session : Held.None;
            if (kind != Held.None && InExplicitTransaction(eventData))
                await WaitForOppositeAsync(command, kind, cancellationToken);

            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            var rows = await base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
            var kind = IsSessionUpdate(command) ? Held.Session : Held.None;
            if (kind != Held.None && InExplicitTransaction(eventData))
            {
                NoteConfirmed(command, kind);
                await HoldConfirmedSessionAsync(kind, cancellationToken);
            }

            return rows;
        }

        private async Task WaitForOppositeAsync(DbCommand command, Held kind, CancellationToken cancellationToken)
        {
            if (!CrossWait || command.Connection is null) return;
            if (!_held.TryGetValue(command.Connection, out var have)) return;
            var waitingFor = kind == Held.Session && have == Held.Agent
                ? _sessionLockConfirmed
                : kind == Held.Agent && have == Held.Session
                    ? _agentLockConfirmed
                    : null;
            if (waitingFor is null) return;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // The other writer's first lock is already in flight. Two seconds is enough for it
            // to confirm; a session-first pair never confirms the opposite row and this expires.
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                await waitingFor.Task.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }

        private void NoteConfirmed(DbCommand command, Held kind)
        {
            if (command.Connection is null) return;
            _held.AddOrUpdate(command.Connection, kind, (_, prev) => prev | kind);
            if (kind == Held.Session)
            {
                ConfirmedSessionAfterExecute = true;
                _sessionLockConfirmed.TrySetResult();
            }
            else
            {
                ConfirmedAgentAfterExecute = true;
                _agentLockConfirmed.TrySetResult();
            }

            _firstLockConfirmed.TrySetResult();
        }

        private async Task HoldConfirmedSessionAsync(Held kind, CancellationToken cancellationToken)
        {
            if (kind != Held.Session || !HoldConfirmedSession) return;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                await _sessionHoldRelease.Task.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }

        private static bool InExplicitTransaction(CommandEventData eventData) =>
            eventData.Context?.Database.CurrentTransaction is not null;

        private static Held Classify(DbCommand command)
        {
            var text = command.CommandText;
            if (!text.Contains("FOR UPDATE", StringComparison.Ordinal))
                return Held.None;
            if (text.Contains("\"AgentSessions\"", StringComparison.Ordinal))
                return Held.Session;
            if (text.Contains("\"Agents\"", StringComparison.Ordinal))
                return Held.Agent;
            return Held.None;
        }

        private static bool IsSessionUpdate(DbCommand command)
        {
            var text = command.CommandText;
            return text.Contains("UPDATE", StringComparison.Ordinal)
                && text.Contains("\"AgentSessions\"", StringComparison.Ordinal)
                && !text.Contains("FOR UPDATE", StringComparison.Ordinal);
        }
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
        adapter.SubmittedBodies.ShouldContain(
            ChannelPreamble.WithSessionTag("launch note body", fixture.SessionId));
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
    public async Task C514_Boot_ambiguous_arm_is_not_whole_submit_retried()
    {
        var adapter = new FakeAgentProtocolAdapter { EchoTypedInputToScreen = false, Pid = 4242 };
        var runner = new ScriptedRcRunner { AdvertiseConditional = true };
        var probe = new ScriptedRcProbe { Armed = false, StateFileFound = true };
        runner.Adapter = adapter;
        await using var fixture = await LaunchFixture.CreateAsync(adapter, s =>
        {
            s.AddSingleton<ISessionRunnerClient>(runner);
            s.AddSingleton<IRcBridgeProbe>(probe);
            s.AddSingleton<RemoteControlRecoveryService>();
        });
        adapter.RegisterOnStart = fixture.Runtime;
        adapter.AcceptedStartedAt = DateTime.UtcNow;
        await fixture.LaunchInteractiveAsync(remoteControlName: "C514-boot");
        adapter.ConditionalInputs.Count(i => i == "/remote-control").ShouldBeLessThanOrEqualTo(1);
        adapter.Prompts.Count(p => p == "/remote-control").ShouldBe(0);
    }

    [Test]
    public async Task C514_Rename_requires_armed_and_clear()
    {
        var adapter = new FakeAgentProtocolAdapter { RemoteControlMenuOpen = true, Pid = 4242 };
        var runner = new ScriptedRcRunner { AdvertiseConditional = true, Adapter = adapter };
        var probe = new ScriptedRcProbe { Armed = true, StateFileFound = true, Connections = 2 };
        await using var fixture = await LaunchFixture.CreateAsync(adapter, s =>
        {
            s.AddSingleton<ISessionRunnerClient>(runner);
            s.AddSingleton<IRcBridgeProbe>(probe);
            s.AddSingleton<RemoteControlRecoveryService>();
        });
        adapter.RegisterOnStart = fixture.Runtime;
        await fixture.LaunchInteractiveAsync(remoteControlName: "C514-rename");
        adapter.Prompts.ShouldNotContain(p => p.StartsWith("/rename", StringComparison.Ordinal));
    }

    [Test]
    public async Task C514_Boot_work_and_notes_wait_behind_open_modal()
    {
        var adapter = new FakeAgentProtocolAdapter { RemoteControlMenuOpen = true, Pid = 4242, PromptOutput = "ok" };
        var runner = new ScriptedRcRunner { AdvertiseConditional = true, Adapter = adapter };
        var probe = new ScriptedRcProbe { Armed = false, StateFileFound = true };
        await using var fixture = await LaunchFixture.CreateAsync(adapter, s =>
        {
            s.AddSingleton<ISessionRunnerClient>(runner);
            s.AddSingleton<IRcBridgeProbe>(probe);
            s.AddSingleton<RemoteControlRecoveryService>();
        });
        adapter.RegisterOnStart = fixture.Runtime;
        var card = await fixture.CreateCardAsync();
        await fixture.AssignAgentAsync(card);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var started = await fixture.StartCardSessionAsync(
            card, "original deferred work body c514", remoteControlName: "C514", kind: AgentKind.ClaudeCode,
            ct: deadline.Token);
        adapter.Prompts.ShouldNotContain("original deferred work body c514");
        await using var db = LaunchFixture.CreateContext();
        (await db.SessionQueuedMessages.AnyAsync(m =>
            m.AgentSessionId == started.SessionId && m.DeferredFromRunAttemptId == started.RunAttemptId))
            .ShouldBeTrue();
    }

    [Test]
    public async Task C514_Deferred_original_work_is_durable_before_launch_yields()
    {
        await C514_Boot_work_and_notes_wait_behind_open_modal();
    }

    [Test]
    public async Task C514_Deferred_boot_has_no_first_output_timeout_or_success()
    {
        var adapter = new FakeAgentProtocolAdapter { RemoteControlMenuOpen = true, Pid = 4242 };
        var runner = new ScriptedRcRunner { AdvertiseConditional = true, Adapter = adapter };
        var probe = new ScriptedRcProbe { Armed = true, StateFileFound = true };
        await using var fixture = await LaunchFixture.CreateAsync(adapter, s =>
        {
            s.AddSingleton<ISessionRunnerClient>(runner);
            s.AddSingleton<IRcBridgeProbe>(probe);
            s.AddSingleton<RemoteControlRecoveryService>();
        });
        adapter.RegisterOnStart = fixture.Runtime;
        var card = await fixture.CreateCardAsync();
        await fixture.AssignAgentAsync(card);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var started = await fixture.StartCardSessionAsync(
            card, "deferred work must not time out c514", remoteControlName: "C514", kind: AgentKind.ClaudeCode,
            ct: deadline.Token);
        await using var db = LaunchFixture.CreateContext();
        var attempt = await db.RunAttempts.SingleAsync(a => a.Id == started.RunAttemptId);
        attempt.Phase.ShouldNotBe(RunPhase.TimedOut);
        attempt.Phase.ShouldNotBe(RunPhase.Succeeded);
        attempt.CompletedAt.ShouldBeNull();
    }

    [Test]
    public async Task C514_Deferred_run_cannot_report_success()
    {
        await C514_Deferred_boot_has_no_first_output_timeout_or_success();
    }

    [Test]
    public async Task C514_Unknown_rc_without_menu_preserves_best_effort_work()
    {
        var adapter = new FakeAgentProtocolAdapter { Pid = 4242, PromptOutput = "work landed" };
        var runner = new ScriptedRcRunner { AdvertiseConditional = true, Adapter = adapter };
        var probe = new ScriptedRcProbe { StateFileFound = false };
        await using var fixture = await LaunchFixture.CreateAsync(adapter, s =>
        {
            s.AddSingleton<ISessionRunnerClient>(runner);
            s.AddSingleton<IRcBridgeProbe>(probe);
            s.AddSingleton<RemoteControlRecoveryService>();
        });
        adapter.RegisterOnStart = fixture.Runtime;
        var card = await fixture.CreateCardAsync();
        await fixture.AssignAgentAsync(card);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var started = await fixture.StartCardSessionAsync(
            card, "best effort work prompt c514", remoteControlName: "C514", kind: AgentKind.ClaudeCode,
            ct: deadline.Token);
        adapter.Prompts.ShouldContain("best effort work prompt c514");
        adapter.Prompts.ShouldNotContain("/remote-control");
        await using var db = LaunchFixture.CreateContext();
        (await db.AgentSessions.SingleAsync(s => s.Id == started.SessionId)).Status.ShouldBe(SessionStatus.Running);
    }

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
    public async Task Boot_arm_cancels_a_leftover_queued_remote_control()
    {
        // CARD-0354: health-watch leftover on the persistent session must not be flushed after
        // boot just armed. Delivering it again opens the management menu and used to kill.
        var adapter = new FakeAgentProtocolAdapter { PromptOutput = "remote-control is active" };
        var probe = new CountingRcProbe { Armed = false };
        await using var fixture = await LaunchFixture.CreateAsync(
            adapter, s => s.AddSingleton<IRcBridgeProbe>(probe));
        var queue = fixture.Services.GetRequiredService<SessionMessageQueueService>();
        await queue.EnqueueAsync(
            fixture.SessionId, "/remote-control", MessageSendMode.WhenIdle, CancellationToken.None);

        await fixture.LaunchInteractiveAsync(remoteControlName: "Antiphon-Orchestrator");

        adapter.Prompts.ShouldBe(["/remote-control", "/rename Antiphon-Orchestrator"]);
        await using var db = LaunchFixture.CreateContext();
        var leftover = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == fixture.SessionId);
        leftover.Status.ShouldBe(QueuedMessageStatus.Pending);
        leftover.Origin.ShouldBe(QueuedMessageOrigin.Ui);
        leftover.Body.ShouldBe("/remote-control");
    }

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

        probe.Calls.ShouldBeGreaterThan(0, "D-1: fresh launches probe before an automatic arm");
        adapter.Prompts.ShouldBe(["/rename Antiphon-Orchestrator"]);
        adapter.Prompts.ShouldNotContain("/remote-control");
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

        adapter.Prompts.ShouldBeEmpty();
        adapter.Prompts.ShouldNotContain(p => p.StartsWith("/rename", StringComparison.Ordinal));

        await using var db = LaunchFixture.CreateContext();
        var incident = await db.AgentIncidents.SingleAsync(
            i => i.AgentId == fixture.AgentId && i.Kind == AgentIncidentKind.RcDegraded);
        incident.FailureReason.ShouldBe("RemoteControlMenuPresent");
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

        adapter.Prompts.ShouldBeEmpty();
        adapter.Prompts.ShouldNotContain(p => p.StartsWith("/rename", StringComparison.Ordinal));

        await using var db = LaunchFixture.CreateContext();
        var incident = await db.AgentIncidents.SingleAsync(
            i => i.AgentId == fixture.AgentId && i.Kind == AgentIncidentKind.RcDegraded);
        incident.FailureReason.ShouldBe("RemoteControlMenuPresent");
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
        var probe = new CountingRcProbe { Armed = false };
        await using var fixture = await LaunchFixture.CreateAsync(
            adapter, s => s.AddSingleton<IRcBridgeProbe>(probe));
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

    /// <summary>
    /// CARD-0133 S1b-A: a Codex boot prompt that fails with the body still in the composer must
    /// not be re-typed (CARD-0056 / CARD-0108: splicing a second copy onto the first). The launch
    /// catch still kills; the session is Failed with the real reason instead of waiting out the
    /// 300 s turn-complete timeout.
    /// </summary>
    [Test]
    public async Task Codex_prompt_failure_with_composer_may_hold_body_writes_once_kills_and_fails()
    {
        var adapter = new FakeAgentProtocolAdapter
        {
            PromptFailure = _ => new PromptDeliveryException(
                "The composer STILL SHOWS the body, so it is holding an unsubmitted prompt.",
                composerMayHoldBody: true),
        };
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        var card = await fixture.CreateCardAsync();

        var start = fixture.StartCardSessionAsync(card, "do the work", kind: AgentKind.Codex);

        await Should.ThrowAsync<PromptDeliveryException>(start);
        adapter.Prompts.ShouldBe(["do the work"], "ComposerMayHoldBody skips the remaining re-types");
        adapter.Lifecycle.ShouldBe(["Kill", "Dispose"]);
        adapter.Killed.ShouldBeTrue();

        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.CardId == card);
        session.Status.ShouldBe(SessionStatus.Failed);
        session.TerminationSource.ShouldBe(SessionTerminationSource.SystemRequest);
        var attempt = await db.RunAttempts.SingleAsync(a => a.CardId == card);
        attempt.Phase.ShouldBe(RunPhase.Failed);
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
        var probe = new CountingRcProbe { Armed = false };
        await using var fixture = await LaunchFixture.CreateAsync(
            adapter, s => s.AddSingleton<IRcBridgeProbe>(probe));
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
        var probe = new CountingRcProbe { Armed = false };
        await using var fixture = await LaunchFixture.CreateAsync(
            adapter, s => s.AddSingleton<IRcBridgeProbe>(probe));
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
    private static async Task SetSessionKindAsync(Guid sessionId, AgentKind kind)
    {
        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == sessionId);
        session.AgentKind = kind;
        await db.SaveChangesAsync();
    }

    private static async Task SetSessionBackendAsync(Guid sessionId, SessionBackend backend)
    {
        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == sessionId);
        session.SessionBackend = backend;
        await db.SaveChangesAsync();
    }

    private static async Task AssertGrokRawArgsRefused(IReadOnlyList<string> args)
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        await SetSessionKindAsync(fixture.SessionId, AgentKind.Grok);
        var grokHome = Path.Combine(Path.GetTempPath(), $"antiphon-grok-home-{Guid.NewGuid():N}");
        Directory.CreateDirectory(grokHome);
        try
        {
            var spec = new AgentLaunchSpec(
                "fake", AgentKind.Grok, "fake",
                args,
                new Dictionary<string, string> { ["GROK_HOME"] = grokHome },
                fixture.Workspace, 120, 30);
            if (OperatingSystem.IsWindows())
            {
                var ex = await Should.ThrowAsync<ConflictException>(
                    () => fixture.LaunchInteractiveAsync(spec: spec));
                ex.Code.ShouldBe(GrokRulesArgvPolicy.ProblemCode);
                adapter.Started.ShouldBeFalse();
            }
            else
            {
                await fixture.LaunchInteractiveAsync(spec: spec);
                adapter.Started.ShouldBeTrue();
                foreach (var arg in args)
                    adapter.StartedArgs.ShouldContain(arg);
                await using var db = LaunchFixture.CreateContext();
                var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
                session.Status.ShouldBe(SessionStatus.Running);
                (session.FailureReason ?? "").ShouldNotContain(GrokRulesArgvPolicy.ProblemCode);
                session.FailureReason.ShouldBeNull();
            }
        }
        finally
        {
            try { Directory.Delete(grokHome, recursive: true); } catch { /* best effort */ }
        }
    }

    private static AgentLaunchSpec GrokSpec(string cwd, string grokHome) =>
        new("fake", AgentKind.Grok, "fake", [],
            new Dictionary<string, string> { ["GROK_HOME"] = grokHome },
            cwd, 120, 30);

    [Test]
    [Arguments("start", false)]
    [Arguments("start", true)]
    [Arguments("ready", false)]
    [Arguments("ready", true)]
    public async Task Launch_timeout_is_infrastructure_but_requested_cancellation_has_no_failure_outcome(string gate, bool requested)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new FakeAgentProtocolAdapter
        {
            ThrowOnStartFactory = () => { entered.TrySetResult(); return null; },
            StartGate = gate == "start" ? startGate : null,
            ReadyHold = gate == "ready" ? readyGate : null,
        };
        await using var fixture = await LaunchFixture.CreateAsync(adapter);
        using var caller = new CancellationTokenSource();
        var launch = fixture.LaunchInteractiveAsync(resume: true, ct: caller.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (gate == "ready")
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                while (!adapter.Started) await Task.Delay(10, deadline.Token);
            }
            if (requested) caller.Cancel();
            else if (gate == "start") startGate.TrySetException(new TaskCanceledException("synthetic adapter timeout"));
            else readyGate.TrySetException(new TaskCanceledException("synthetic readiness timeout"));
            await Should.ThrowAsync<OperationCanceledException>(launch);
            adapter.Killed.ShouldBeTrue(); adapter.Disposed.ShouldBeTrue();
            await using var verify = LaunchFixture.CreateContext();
            var session = (await verify.AgentSessions.FindAsync(fixture.SessionId))!;
            session.RestartFailureKind.ShouldBe(requested ? null : RestartFailureKind.Infrastructure);
            session.InteractiveLaunchCompletedAt.ShouldBeNull();
            var state = await verify.AgentSupervisionStates.FindAsync(fixture.AgentId);
            if (state is not null)
            {
                state.ConsecutiveFailures.ShouldBe(0); state.RestartBackoffFailures.ShouldBe(0);
                state.ContinuityHeldAt.ShouldBeNull();
            }
        }
        finally
        {
            startGate.TrySetResult(); readyGate.TrySetResult(true);
            try { await launch; } catch (OperationCanceledException) { }
        }
    }

    [Test]
    public async Task Runner_500_longer_than_both_text_columns_still_records_a_failed_session()
    {
        var message = "Session runner returned 500: " + new string('M', 4500);
        message.Length.ShouldBeGreaterThan(4000);

        var interactive = new FakeAgentProtocolAdapter { ThrowOnStart = new HttpRequestException(message) };
        await using (var fixture = await LaunchFixture.CreateAsync(interactive))
        {
            await Should.ThrowAsync<HttpRequestException>(fixture.LaunchInteractiveAsync());
            await using var db = LaunchFixture.CreateContext();
            var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
            session.Status.ShouldBe(SessionStatus.Failed);
            session.FailureReason.ShouldNotBeNull();
            session.FailureReason.Length.ShouldBe(2000);
            session.FailureReason.ShouldEndWith("…");
            session.FailureReason.ShouldStartWith("Session runner returned 500:");
            (await db.Agents.SingleAsync(a => a.Id == fixture.AgentId)).Status.ShouldBe(AgentStatus.Failed);
        }

        var cardAdapter = new FakeAgentProtocolAdapter { ThrowOnStart = new HttpRequestException(message) };
        await using var cardFixture = await LaunchFixture.CreateAsync(cardAdapter);
        var cardId = await cardFixture.CreateCardAsync();
        await Should.ThrowAsync<HttpRequestException>(cardFixture.StartCardSessionAsync(cardId, "boot-must-not-be-sent"));
        await using var cardDb = LaunchFixture.CreateContext();
        var cardSession = await cardDb.AgentSessions.SingleAsync(s => s.CardId == cardId);
        cardSession.Status.ShouldBe(SessionStatus.Failed);
        cardSession.FailureReason.ShouldNotBeNull();
        cardSession.FailureReason.Length.ShouldBe(2000);
        cardSession.FailureReason.ShouldEndWith("…");
        var attempt = await cardDb.RunAttempts.SingleAsync(a => a.CardId == cardId);
        attempt.Phase.ShouldBe(RunPhase.Failed);
        attempt.ErrorDetails.ShouldNotBeNull();
        attempt.ErrorDetails.Length.ShouldBe(4000);
        attempt.ErrorDetails.ShouldEndWith("…");
        attempt.ErrorDetails.ShouldStartWith("Session runner returned 500:");
    }

    [Test]
    public async Task Runner_500_argv_sentinel_is_absent_from_the_body_the_failure_reason_and_the_server_log(CancellationToken ct)
    {
        const string marker = "GROK-RULES-ARGV-SENTINEL-2dec24ba";
        var sentinel = marker + new string('Q', 3000 - marker.Length);
        var message = $"Could not start terminal process grok --append-system-prompt {sentinel} --session-id keep-me";
        var body = await PostUnmappedLaunchAsync(message, ct);
        body.ShouldNotContain(marker);
        body.ShouldNotContain(sentinel);
        using var json = JsonDocument.Parse(body);
        var detail = json.RootElement.GetProperty("detail").GetString();
        detail.ShouldNotBeNull();
        detail.ShouldContain("<redacted 3000 chars>");
        detail.Length.ShouldBeLessThanOrEqualTo(512);

        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/problem+json"),
        };
        var launchError = await Should.ThrowAsync<HttpRequestException>(
            () => SessionRunnerHttpClient.ThrowForRunnerProblemAsync(response, CancellationToken.None));
        launchError.Message.ShouldNotContain(marker);

        var logs = new CollectingLoggerProvider();
        var adapter = new FakeAgentProtocolAdapter { ThrowOnStart = launchError };
        await using var fixture = await LaunchFixture.CreateAsync(adapter, services =>
            services.AddLogging(builder => builder.AddProvider(logs)));
        await Should.ThrowAsync<HttpRequestException>(fixture.LaunchInteractiveAsync());

        await using var db = LaunchFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.Status.ShouldBe(SessionStatus.Failed);
        session.FailureReason.ShouldNotBeNull();
        session.FailureReason.ShouldNotContain(marker);
        session.FailureReason.ShouldNotContain(sentinel);
        session.FailureReason.ShouldContain("<redacted 3000 chars>");
        var recorded = string.Join('\n', logs.Lines);
        recorded.ShouldContain("Failed to start interactive agent session");
        recorded.ShouldNotContain(marker);
        recorded.ShouldNotContain(sentinel);
    }

    private static async Task<string> PostUnmappedLaunchAsync(string message, CancellationToken ct)
    {
        var root = Path.Combine(Path.GetTempPath(), "c2dec24ba-srv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(new Antiphon.SessionRunner.SessionRunnerSettings
            {
                SessionLogPath = root,
                PtyHostSourceDir = Path.Combine(root, "missing-host"),
                CpuWatchdogEnabled = false,
            }),
            NullLogger<SessionRunnerRuntime>.Instance);
        runtime.CreationEffectForTest = (_, _) => throw new InvalidOperationException(message);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Production" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(runtime);
        var app = builder.Build();
        app.UseRunnerExceptionMapping();
        app.MapSessionLaunchRoute();
        await app.StartAsync(ct);
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(20) };
            using var reply = await http.PostAsJsonAsync("/sessions", new RunnerLaunchRequest(
                Guid.NewGuid(), "/bin/sh", [], new Dictionary<string, string>(), root, 80, 24,
                TranscriptFormat: TranscriptFormats.Grok), ct);
            var text = await reply.Content.ReadAsStringAsync(ct);
            reply.StatusCode.ShouldBe(HttpStatusCode.InternalServerError, $"launch-response-body:{text}");
            return text;
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
            try { Directory.Delete(root, recursive: true); } catch (Exception) { }
        }
    }

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
            CreateCoreAsync(new QueueAdapterFactory([adapter]), extraServices, configureDb: null);

        public static Task<LaunchFixture> CreateAsync(
            FakeAgentProtocolAdapter adapter,
            Action<DbContextOptionsBuilder> configureDb) =>
            CreateCoreAsync(new QueueAdapterFactory([adapter]), extraServices: null, configureDb);

        public static Task<LaunchFixture> CreateAsync(
            FakeAgentProtocolAdapter adapter,
            Action<DbContextOptionsBuilder> configureDb,
            Action<IServiceCollection> extraServices) =>
            CreateCoreAsync(new QueueAdapterFactory([adapter]), extraServices, configureDb);

        private static async Task<LaunchFixture> CreateCoreAsync(
            IAgentProtocolAdapterFactory adapterFactory,
            Action<IServiceCollection>? extraServices,
            Action<DbContextOptionsBuilder>? configureDb = null)
        {
            var harness = await ClaudeDialogFixture.CreateBridgeAsync(new BridgeQueueHarness.HarnessOptions
            {
                AlwaysOn = false,
                ConfigureDbContext = configureDb,
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

        /// <summary>
        /// One usage-bearing transcript row, which is all <c>SessionContextUsage</c> needs to
        /// compute a fullness for this session.
        /// </summary>
        public async Task SeedContextUsageAsync(int inputTokens)
        {
            await using var db = CreateContext();
            var at = DateTime.UtcNow.AddMinutes(-1);
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = SessionId,
                Sequence = 1,
                Kind = TranscriptKinds.AssistantText,
                Uuid = $"fullness-{Guid.NewGuid():N}",
                Role = "assistant",
                Text = "a turn from before the restart",
                InputTokens = inputTokens,
                Timestamp = at,
                CreatedAt = at,
            });
            await db.SaveChangesAsync();
        }

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
            CancellationToken ct = default)
        {
            // Card work admission reads the runtime before sending its first prompt. Interactive
            // fixtures opt into live queue delivery separately through WireDelivery.
            if (Services.GetRequiredService<IAgentProtocolAdapterFactory>() is QueueAdapterFactory queued)
                queued.Runtime = Runtime;
            return Services.GetRequiredService<AgentSessionService>().StartAsync(
                new StartAgentSessionRequest(
                    cardId, "fake", kind, prompt, RemoteControlName: remoteControlName),
                LaunchSpec(Path.Combine(Harness.TempRoot, "repo"), kind),
                ct);
        }

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
        public AgentSessionRuntime? Runtime { private get; set; }

        public IAgentProtocolAdapter Create(AgentKind kind)
        {
            if (!_adapters.TryDequeue(out var adapter))
                throw new InvalidOperationException("No fake adapter was queued for this launch.");
            if (adapter is FakeAgentProtocolAdapter fake && kind == AgentKind.ClaudeCode)
            {
                fake.RegisterOnStart ??= Runtime;
                return fake.WithClaudeDialogObservation();
            }
            return adapter;
        }
    }

    /// <summary>Hands the SAME adapter to every launch — how a relaunch re-touches one process.</summary>
    private sealed class SharedAdapterFactory(IAgentProtocolAdapter adapter) : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) =>
            adapter is FakeAgentProtocolAdapter fake && kind == AgentKind.ClaudeCode
                ? fake.WithClaudeDialogObservation() : adapter;
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
