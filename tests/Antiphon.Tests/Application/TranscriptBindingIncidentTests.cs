using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// Server half of CARD-0006: what the operator actually sees when the runner refuses to bind a
/// transcript. The refusal itself is the safe outcome (the alternative bound an agent to the human
/// operator's own Claude conversation on 2026-08-09), but a session with no transcript ingests
/// nothing, reads permanently idle, and cannot dispatch channel replies — so it has to surface as
/// an incident rather than one WRN line in the runner log.
///
/// Every assertion is scoped to the row this test created (CLAUDE.md: the integration suite shares
/// one Postgres, so an unscoped count also asserts "no other test has data right now").
/// </summary>
[Category("Integration")]
[NotInParallel("TranscriptBindingIncidents")]
public class TranscriptBindingIncidentTests
{
    [Test]
    public async Task Transcript_fault_event_creates_incident_and_alert()
    {
        await using var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = false,
            ConfigureServices = s => s.AddSingleton<TranscriptBindingIncidentService>(),
        });

        await harness.Provider.GetRequiredService<TranscriptBindingIncidentService>()
            .OnTranscriptFaultAsync(
                new SessionRunnerTranscriptFaultEvent(
                    harness.SessionId,
                    TranscriptFaultKinds.AdoptionRefused,
                    "no prompt in it matches input delivered to this session",
                    CandidatePath: @"C:\Users\someone\.claude\projects\C--src-Antiphon\37512455.jsonl"),
                CancellationToken.None);

        await using var db = BridgeQueueHarness.CreateContext();
        var incident = await db.AgentIncidents.SingleAsync(
            i => i.AgentId == harness.AgentId && i.Kind == AgentIncidentKind.TranscriptBindFailed);
        incident.Severity.ShouldBe(AlertSeverity.Warning, "no channel binding — degraded, not urgent");
        incident.SessionId.ShouldBe(harness.SessionId);
        incident.FailureReason.ShouldBe(TranscriptFaultKinds.AdoptionRefused);
        incident.Message.ShouldContain("37512455.jsonl", customMessage: "the refused candidate must be named");

        // Incidents are the supervisor's alerts 1:1, deduped per agent+kind.
        var alert = await db.Alerts.SingleAsync(
            a => a.AgentId == harness.AgentId
                && a.DedupKey == $"supervisor:{AgentIncidentKind.TranscriptBindFailed}:{harness.AgentId}");
        alert.Severity.ShouldBe(AlertSeverity.Warning);
    }

    /// <summary>
    /// A channel-bound agent with no transcript cannot answer its channel AT ALL — reply dispatch
    /// runs off ingested turn ends and there are none — and a wrongly-bound transcript there is the
    /// privacy failure this card exists for. That earns Critical, which reaches Telegram.
    /// </summary>
    [Test]
    public async Task Transcript_fault_for_a_channel_bound_agent_is_critical()
    {
        await using var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = false,
            ConfigureServices = s => s.AddSingleton<TranscriptBindingIncidentService>(),
        });

        var channelId = await BindChannelAsync(harness.AgentId);
        try
        {
            await harness.Provider.GetRequiredService<TranscriptBindingIncidentService>()
                .OnTranscriptFaultAsync(
                    new SessionRunnerTranscriptFaultEvent(
                        harness.SessionId,
                        TranscriptFaultKinds.TranscriptMissing,
                        "the child exited without producing an identifiable transcript",
                        CandidatePath: null),
                    CancellationToken.None);

            await using var db = BridgeQueueHarness.CreateContext();
            var incident = await db.AgentIncidents.SingleAsync(
                i => i.AgentId == harness.AgentId && i.Kind == AgentIncidentKind.TranscriptBindFailed);
            incident.Severity.ShouldBe(AlertSeverity.Critical);
            incident.Message.ShouldContain("channel replies cannot be dispatched");

            (await db.Alerts.SingleAsync(a => a.AgentId == harness.AgentId
                && a.DedupKey == $"supervisor:{AgentIncidentKind.TranscriptBindFailed}:{harness.AgentId}"))
                .Severity.ShouldBe(AlertSeverity.Critical);
        }
        finally
        {
            await using var cleanup = BridgeQueueHarness.CreateContext();
            await cleanup.ChatChannels.Where(c => c.Id == channelId).ExecuteDeleteAsync();
        }
    }

    /// <summary>
    /// A heuristic bind PASSED every adoption rule, so it is not a problem — but which file an
    /// agent is reading from belongs on the record. Timeline row, no alert (mirrors ContextCompacted).
    /// </summary>
    [Test]
    public async Task Heuristic_bind_event_creates_info_incident_without_alert()
    {
        await using var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = false,
            ConfigureServices = s => s.AddSingleton<TranscriptBindingIncidentService>(),
        });

        await harness.Provider.GetRequiredService<TranscriptBindingIncidentService>()
            .OnHeuristicBindAsync(
                new SessionRunnerTranscriptBoundEvent(
                    harness.SessionId,
                    @"C:\Users\lndco\.claude\projects\C--src-Antiphon\a1b2c3.jsonl",
                    TranscriptBindMethods.Discovery),
                CancellationToken.None);

        await using var db = BridgeQueueHarness.CreateContext();
        var incident = await db.AgentIncidents.SingleAsync(
            i => i.AgentId == harness.AgentId && i.Kind == AgentIncidentKind.TranscriptBoundByDiscovery);
        incident.Severity.ShouldBe(AlertSeverity.Info);
        incident.Message.ShouldContain(TranscriptBindMethods.Discovery);

        (await db.Alerts.AnyAsync(a => a.AgentId == harness.AgentId
            && a.DedupKey == $"supervisor:{AgentIncidentKind.TranscriptBoundByDiscovery}:{harness.AgentId}"))
            .ShouldBeFalse("a successful bind is a timeline row, not an alert");
    }

    /// <summary>
    /// Pins the §6 decision so nobody "fixes" it later: a session with NO transcript reads IDLE, not
    /// working. The launch flow depends on it — the boot prompt and launch note are enqueued
    /// <c>WhenIdle</c> BEFORE any transcript exists, so "no transcript ⇒ working" would deadlock
    /// every fresh launch, and a transcript-less session would never receive anything at all.
    /// </summary>
    [Test]
    public async Task Empty_transcript_still_reads_idle()
    {
        await using var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = false,
        });

        await using var db = BridgeQueueHarness.CreateContext();
        (await db.TranscriptEntries.AnyAsync(t => t.AgentSessionId == harness.SessionId))
            .ShouldBeFalse("precondition: this session has ingested nothing");

        (await SessionMessageQueueService.IsWorkingAsync(db, harness.SessionId, CancellationToken.None))
            .ShouldBeFalse("no transcript must read idle, or every WhenIdle delivery strands");
    }

    /// <summary>
    /// CARD-0073 S1: a delegate task session has no Agents.PersistentSessionId pointing at it —
    /// the owner is on the task row. Dropping that lookup silenced every bind/fault outcome for
    /// the population the card measured (57 discovery binds, 5 incidents).
    /// </summary>
    [Test]
    public async Task Delegate_task_session_fault_records_incident_on_the_task_agent()
    {
        await using var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = false,
            ConfigureServices = s => s.AddSingleton<TranscriptBindingIncidentService>(),
        });

        var delegateSessionId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        await using (var db = BridgeQueueHarness.CreateContext())
        {
            var now = DateTime.UtcNow;
            db.AgentSessions.Add(new AgentSession
            {
                Id = delegateSessionId,
                DefinitionName = "fake",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = Path.Combine(harness.TempRoot, "delegate-cwd"),
                Cols = 120,
                Rows = 30,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
            });
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = "delegate owner drop",
                Goal = "delegate owner drop",
                WorkingDirectory = Path.Combine(harness.TempRoot, "delegate-cwd"),
                AgentId = harness.AgentId,
                AgentSessionId = delegateSessionId,
                AgentName = "task-delegate",
                Status = AgentTaskStatus.Dispatched,
                Ephemeral = true,
                CreatedAt = now,
                DispatchedAt = now,
            });
            await db.SaveChangesAsync();
        }

        try
        {
            await harness.Provider.GetRequiredService<TranscriptBindingIncidentService>()
                .OnTranscriptFaultAsync(
                    new SessionRunnerTranscriptFaultEvent(
                        delegateSessionId,
                        TranscriptFaultKinds.TranscriptMissing,
                        "No cwd-matching transcript candidates after 60s (0 file(s) under the transcript root, 0 cwd-matched, 0 refused).",
                        CandidatePath: null),
                    CancellationToken.None);

            await using var db = BridgeQueueHarness.CreateContext();
            var incident = await db.AgentIncidents.SingleAsync(
                i => i.AgentId == harness.AgentId
                    && i.SessionId == delegateSessionId
                    && i.Kind == AgentIncidentKind.TranscriptBindFailed);
            incident.FailureReason.ShouldBe(TranscriptFaultKinds.TranscriptMissing);
            incident.Message.ShouldContain("0 cwd-matched");
        }
        finally
        {
            await using var cleanup = BridgeQueueHarness.CreateContext();
            await cleanup.AgentTasks.Where(t => t.Id == taskId).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task Delegate_task_session_heuristic_bind_records_incident_on_the_task_agent()
    {
        await using var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = false,
            ConfigureServices = s => s.AddSingleton<TranscriptBindingIncidentService>(),
        });

        var delegateSessionId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        await using (var db = BridgeQueueHarness.CreateContext())
        {
            var now = DateTime.UtcNow;
            db.AgentSessions.Add(new AgentSession
            {
                Id = delegateSessionId,
                DefinitionName = "fake",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = Path.Combine(harness.TempRoot, "delegate-bind-cwd"),
                Cols = 120,
                Rows = 30,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
            });
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = "delegate bind owner",
                Goal = "delegate bind owner",
                WorkingDirectory = Path.Combine(harness.TempRoot, "delegate-bind-cwd"),
                AgentId = harness.AgentId,
                AgentSessionId = delegateSessionId,
                AgentName = "task-delegate",
                Status = AgentTaskStatus.Dispatched,
                Ephemeral = true,
                CreatedAt = now,
                DispatchedAt = now,
            });
            await db.SaveChangesAsync();
        }

        try
        {
            await harness.Provider.GetRequiredService<TranscriptBindingIncidentService>()
                .OnHeuristicBindAsync(
                    new SessionRunnerTranscriptBoundEvent(
                        delegateSessionId,
                        @"C:\Users\lndco\.claude\projects\C--tmp-worktree\deadbeef.jsonl",
                        TranscriptBindMethods.Discovery),
                    CancellationToken.None);

            await using var db = BridgeQueueHarness.CreateContext();
            var incident = await db.AgentIncidents.SingleAsync(
                i => i.AgentId == harness.AgentId
                    && i.SessionId == delegateSessionId
                    && i.Kind == AgentIncidentKind.TranscriptBoundByDiscovery);
            incident.Severity.ShouldBe(AlertSeverity.Info);
            incident.Message.ShouldContain(TranscriptBindMethods.Discovery);
        }
        finally
        {
            await using var cleanup = BridgeQueueHarness.CreateContext();
            await cleanup.AgentTasks.Where(t => t.Id == taskId).ExecuteDeleteAsync();
        }
    }

    /// <summary>
    /// When nothing owns the session the outcome must still be visible: Error, not a silent
    /// return and not a Warning that operators filter out. No incident row — there is nowhere
    /// to hang one — matching ChannelReplyDispatcher.ReportLostAsync.
    ///
    /// <para>CARD-0101 added the second half. A log line is not a surface: this branch was the one
    /// way the INCIDENT stream could fall silent while the runner log kept reporting the same fault
    /// every five minutes, which is exactly the "zero new incidents, therefore solved" misreading
    /// the 2026-08-20 investigation made about a cascade that was still live. There is still no
    /// incident row (AgentIncident.AgentId is required), but there is now a standalone alert
    /// carrying the session id — the same shape as an unclaimed AutoCompactFailed.</para>
    /// </summary>
    [Test]
    public async Task Unowned_session_fault_logs_error_and_raises_a_standalone_alert()
    {
        var logs = new List<string>();
        await using var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = false,
            ConfigureServices = s =>
            {
                s.AddSingleton<TranscriptBindingIncidentService>();
                s.AddSingleton<ILogger<TranscriptBindingIncidentService>>(new ListLogger<TranscriptBindingIncidentService>(logs));
            },
        });

        var orphan = Guid.NewGuid();
        await harness.Provider.GetRequiredService<TranscriptBindingIncidentService>()
            .OnTranscriptFaultAsync(
                new SessionRunnerTranscriptFaultEvent(
                    orphan,
                    TranscriptFaultKinds.TranscriptMissing,
                    "No cwd-matching transcript candidates after 60s",
                    CandidatePath: null),
                CancellationToken.None);

        await using var db = BridgeQueueHarness.CreateContext();
        (await db.AgentIncidents.AnyAsync(
            i => i.SessionId == orphan && i.Kind == AgentIncidentKind.TranscriptBindFailed))
            .ShouldBeFalse("an unowned session has nowhere to hang an incident");

        logs.ShouldContain(l => l.Contains("[Error]", StringComparison.Ordinal)
            && l.Contains(orphan.ToString("D"), StringComparison.OrdinalIgnoreCase)
            && l.Contains("No agent owns", StringComparison.OrdinalIgnoreCase));

        var alert = await db.Alerts.SingleAsync(
            a => a.DedupKey == TranscriptBindingIncidentService.UnownedFaultDedupKey(orphan));
        alert.Severity.ShouldBe(AlertSeverity.Warning, "a fresh fault is not yet an escalation");
        alert.SessionId.ShouldBe(orphan);
        alert.AgentId.ShouldBeNull();
    }

    /// <summary>
    /// CARD-0101 item 4, server half. The existing Critical path is reserved for channel-bound
    /// agents; a delegate task agent is never channel-bound, so on 2026-08-20 every one of ~250
    /// incidents across six sessions was Warning while eleven agent-hours ran unreadable. A refusal
    /// that has been CONTINUOUS past the stuck threshold now raises a distinct Critical incident
    /// regardless of channel binding — the row that says "still broken, for hours" rather than
    /// the thirty-eighth row that says "broken".
    /// </summary>
    [Test]
    public async Task A_prolonged_refusal_escalates_to_critical_without_a_channel_binding()
    {
        await using var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = false,
            ConfigureServices = s =>
            {
                s.AddSingleton(Options.Create(new TranscriptBindingSettings
                {
                    StuckAfterMinutes = 30,
                    StuckRepeatMinutes = 60,
                }));
                s.AddSingleton<TranscriptBindingIncidentService>();
            },
        });

        // The 5409c537 shape: 37 reports, 3h6m unbound, no channel binding anywhere.
        await harness.Provider.GetRequiredService<TranscriptBindingIncidentService>()
            .OnTranscriptFaultAsync(
                new SessionRunnerTranscriptFaultEvent(
                    harness.SessionId,
                    TranscriptFaultKinds.AdoptionRefused,
                    "no prompt in it matches input delivered to this session",
                    CandidatePath: null,
                    UnboundSeconds: 11165,
                    Repeat: 37),
                CancellationToken.None);

        await using var db = BridgeQueueHarness.CreateContext();

        // The base incident is unchanged: the repeats are the evidence the fault is still live.
        var baseIncident = await db.AgentIncidents.SingleAsync(
            i => i.AgentId == harness.AgentId && i.Kind == AgentIncidentKind.TranscriptBindFailed);
        baseIncident.Severity.ShouldBe(AlertSeverity.Warning);

        var stuck = await db.AgentIncidents.SingleAsync(
            i => i.AgentId == harness.AgentId && i.Kind == AgentIncidentKind.TranscriptBindStuck);
        stuck.Severity.ShouldBe(AlertSeverity.Critical, "no channel binding must not mean no escalation");
        stuck.SessionId.ShouldBe(harness.SessionId);
        stuck.Message.ShouldContain("3.1h", customMessage: "the operator needs the duration, not just the fact");
        stuck.Message.ShouldContain("37 report(s)");

        var stuckAlert = await db.Alerts.SingleAsync(
            a => a.AgentId == harness.AgentId
                && a.DedupKey == $"supervisor:{AgentIncidentKind.TranscriptBindStuck}:{harness.AgentId}");
        stuckAlert.Severity.ShouldBe(AlertSeverity.Critical, "Critical is what reaches Telegram");
    }

    /// <summary>
    /// A first-minute refusal is normal operation on a session whose first turn is slow. Escalating
    /// on it would make the new Critical exactly as ignorable as the Warning it exists to escape.
    /// </summary>
    [Test]
    public async Task A_fresh_refusal_records_the_incident_but_does_not_escalate()
    {
        await using var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = false,
            ConfigureServices = s =>
            {
                s.AddSingleton(Options.Create(new TranscriptBindingSettings { StuckAfterMinutes = 30 }));
                s.AddSingleton<TranscriptBindingIncidentService>();
            },
        });

        await harness.Provider.GetRequiredService<TranscriptBindingIncidentService>()
            .OnTranscriptFaultAsync(
                new SessionRunnerTranscriptFaultEvent(
                    harness.SessionId,
                    TranscriptFaultKinds.AdoptionRefused,
                    "no prompt in it matches input delivered to this session",
                    CandidatePath: null,
                    UnboundSeconds: 65,
                    Repeat: 1),
                CancellationToken.None);

        await using var db = BridgeQueueHarness.CreateContext();
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == harness.AgentId && i.Kind == AgentIncidentKind.TranscriptBindFailed))
            .ShouldBeTrue("the base incident is unconditional");
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == harness.AgentId && i.Kind == AgentIncidentKind.TranscriptBindStuck))
            .ShouldBeFalse("65 seconds unbound is a slow first turn, not a stuck session");
    }

    /// <summary>
    /// The underlying fault repeats every five minutes for as long as it lasts. If the escalation
    /// repeated with it, 37 Warnings would become 37 Criticals and the signal would be right back
    /// where it started. Gated on the database, not in-memory state, so a server restart or an
    /// event-stream reconnect cannot reset it.
    /// </summary>
    [Test]
    public async Task The_escalation_does_not_re_fire_within_its_repeat_window()
    {
        await using var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = false,
            ConfigureServices = s =>
            {
                s.AddSingleton(Options.Create(new TranscriptBindingSettings
                {
                    StuckAfterMinutes = 30,
                    StuckRepeatMinutes = 60,
                }));
                s.AddSingleton<TranscriptBindingIncidentService>();
            },
        });

        var service = harness.Provider.GetRequiredService<TranscriptBindingIncidentService>();
        for (var repeat = 37; repeat <= 39; repeat++)
        {
            await service.OnTranscriptFaultAsync(
                new SessionRunnerTranscriptFaultEvent(
                    harness.SessionId,
                    TranscriptFaultKinds.AdoptionRefused,
                    "no prompt in it matches input delivered to this session",
                    CandidatePath: null,
                    UnboundSeconds: 11165 + (repeat - 37) * 300,
                    Repeat: repeat),
                CancellationToken.None);
        }

        await using var db = BridgeQueueHarness.CreateContext();
        (await db.AgentIncidents.CountAsync(
            i => i.AgentId == harness.AgentId && i.Kind == AgentIncidentKind.TranscriptBindFailed))
            .ShouldBe(3, "the five-minute repeat is deliberately untouched");
        (await db.AgentIncidents.CountAsync(
            i => i.AgentId == harness.AgentId && i.Kind == AgentIncidentKind.TranscriptBindStuck))
            .ShouldBe(1, "one escalation per repeat window, or the escalation is just more noise");
    }

    [Test]
    public async Task ClaimRevoked_fault_records_TranscriptClaimRevoked_warning()
    {
        await using var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = false,
            ConfigureServices = s => s.AddSingleton<TranscriptBindingIncidentService>(),
        });

        var namesake = Guid.NewGuid();
        await harness.Provider.GetRequiredService<TranscriptBindingIncidentService>()
            .OnTranscriptFaultAsync(
                new SessionRunnerTranscriptFaultEvent(
                    harness.SessionId,
                    TranscriptFaultKinds.ClaimRevoked,
                    $"Reclaimed by namesake session {namesake:D}",
                    CandidatePath: $@"C:\Users\someone\.claude\projects\C--src-Antiphon\{namesake:D}.jsonl",
                    UnboundSeconds: 0,
                    Repeat: 1),
                CancellationToken.None);

        await using var db = BridgeQueueHarness.CreateContext();
        var incident = await db.AgentIncidents.SingleAsync(
            i => i.AgentId == harness.AgentId && i.Kind == AgentIncidentKind.TranscriptClaimRevoked);
        incident.Severity.ShouldBe(AlertSeverity.Warning);
        incident.FailureReason.ShouldBe(TranscriptFaultKinds.ClaimRevoked);
        incident.Message.ShouldContain("handed back", Case.Insensitive);
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == harness.AgentId && i.Kind == AgentIncidentKind.TranscriptBindStuck))
            .ShouldBeFalse();
    }

    [Test]
    public async Task ClaimRevoked_fault_is_critical_when_the_displaced_agent_is_channel_bound()
    {
        await using var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = false,
            ConfigureServices = s => s.AddSingleton<TranscriptBindingIncidentService>(),
        });

        var channelId = await BindChannelAsync(harness.AgentId);
        try
        {
            await harness.Provider.GetRequiredService<TranscriptBindingIncidentService>()
                .OnTranscriptFaultAsync(
                    new SessionRunnerTranscriptFaultEvent(
                        harness.SessionId,
                        TranscriptFaultKinds.ClaimRevoked,
                        "Reclaimed by namesake session",
                        CandidatePath: null),
                    CancellationToken.None);

            await using var db = BridgeQueueHarness.CreateContext();
            var incident = await db.AgentIncidents.SingleAsync(
                i => i.AgentId == harness.AgentId && i.Kind == AgentIncidentKind.TranscriptClaimRevoked);
            incident.Severity.ShouldBe(AlertSeverity.Critical);
        }
        finally
        {
            await using var cleanup = BridgeQueueHarness.CreateContext();
            await cleanup.ChatChannels.Where(c => c.Id == channelId).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task ClaimRevoked_fault_never_escalates_to_TranscriptBindStuck()
    {
        await using var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = false,
            ConfigureServices = s =>
            {
                s.AddSingleton(Options.Create(new TranscriptBindingSettings
                {
                    StuckAfterMinutes = 1,
                    StuckRepeatMinutes = 1,
                }));
                s.AddSingleton<TranscriptBindingIncidentService>();
            },
        });

        await harness.Provider.GetRequiredService<TranscriptBindingIncidentService>()
            .OnTranscriptFaultAsync(
                new SessionRunnerTranscriptFaultEvent(
                    harness.SessionId,
                    TranscriptFaultKinds.ClaimRevoked,
                    "Reclaimed by namesake session",
                    CandidatePath: null,
                    UnboundSeconds: 11165,
                    Repeat: 37),
                CancellationToken.None);

        await using var db = BridgeQueueHarness.CreateContext();
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == harness.AgentId && i.Kind == AgentIncidentKind.TranscriptBindStuck))
            .ShouldBeFalse();
        (await db.AgentIncidents.CountAsync(
            i => i.AgentId == harness.AgentId && i.Kind == AgentIncidentKind.TranscriptClaimRevoked))
            .ShouldBe(1);
    }

    /// <summary>
    /// CARD-0195. A settled delegate task keeps its <c>AgentId</c> after the agent row is deleted —
    /// <c>AgentTasks.AgentId</c> has no foreign key, deliberately, so retiring an agent does not
    /// cascade the delegation history away. Measured 2026-08-25: 447 of the 539 task rows carrying
    /// an <c>AgentId</c> pointed at an agent that no longer existed, so this is the ordinary end
    /// state, not a race.
    ///
    /// <para>The old lookup handed that dead id back, the insert died 23503 on
    /// <c>FK_AgentIncidents_Agents_AgentId</c>, and the catch logged "Recording a transcript fault
    /// for session X failed" with no cause and no surface — seven times across two days, the last
    /// of them the CARD-0194 casualty (session <c>8be1afc5</c>). The fault is real either way, so
    /// it must degrade to the unowned branch's standalone alert rather than vanish.</para>
    /// </summary>
    [Test]
    public async Task Fault_for_a_task_whose_agent_row_is_gone_alerts_instead_of_swallowing_a_foreign_key_error()
    {
        var logs = new List<string>();
        await using var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = false,
            ConfigureServices = s =>
            {
                s.AddSingleton<TranscriptBindingIncidentService>();
                s.AddSingleton<ILogger<TranscriptBindingIncidentService>>(new ListLogger<TranscriptBindingIncidentService>(logs));
            },
        });

        var delegateSessionId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var deletedAgentId = Guid.NewGuid(); // never inserted: the reaped delegate agent
        await using (var db = BridgeQueueHarness.CreateContext())
        {
            var now = DateTime.UtcNow;
            db.AgentSessions.Add(new AgentSession
            {
                Id = delegateSessionId,
                DefinitionName = "fake",
                AgentKind = AgentKind.Codex,
                Status = SessionStatus.Running,
                Cwd = Path.Combine(harness.TempRoot, "reaped-cwd"),
                Cols = 120,
                Rows = 30,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
            });
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = "reaped delegate",
                Goal = "reaped delegate",
                WorkingDirectory = Path.Combine(harness.TempRoot, "reaped-cwd"),
                AgentId = deletedAgentId,
                AgentSessionId = delegateSessionId,
                AgentName = "task-delegate",
                Status = AgentTaskStatus.Failed,
                Ephemeral = true,
                CreatedAt = now,
                DispatchedAt = now,
            });
            await db.SaveChangesAsync();
        }

        try
        {
            await harness.Provider.GetRequiredService<TranscriptBindingIncidentService>()
                .OnTranscriptFaultAsync(
                    new SessionRunnerTranscriptFaultEvent(
                        delegateSessionId,
                        TranscriptFaultKinds.TranscriptMissing,
                        "the child exited without ever producing a Codex rollout we could identify",
                        CandidatePath: null),
                    CancellationToken.None);

            await using var db = BridgeQueueHarness.CreateContext();
            (await db.AgentIncidents.AnyAsync(i => i.SessionId == delegateSessionId))
                .ShouldBeFalse("there is no agent row left to hang an incident on");

            logs.ShouldNotContain(
                l => l.Contains("Recording a transcript fault", StringComparison.Ordinal),
                "the dead id must never reach SaveChanges, so nothing should have been caught");

            var alert = await db.Alerts.SingleAsync(
                a => a.DedupKey == TranscriptBindingIncidentService.UnownedFaultDedupKey(delegateSessionId));
            alert.Severity.ShouldBe(AlertSeverity.Warning);
            alert.SessionId.ShouldBe(delegateSessionId);
            alert.AgentId.ShouldBeNull();
        }
        finally
        {
            await using var cleanup = BridgeQueueHarness.CreateContext();
            await cleanup.AgentTasks.Where(t => t.Id == taskId).ExecuteDeleteAsync();
        }
    }

    /// <summary>
    /// CARD-0195, the backstop half: whatever else ever breaks the write, the log must name what
    /// the database said (AGENTS.md: "never report a DB failure without the DB's own message") and
    /// the fault must still reach a surface. Forced here with an over-length message — the incident
    /// row's <c>Message</c> is <c>varchar(4000)</c> and nothing truncates it — because that fails
    /// deterministically on a real Postgres without needing the FK shape the fix above removed.
    /// </summary>
    [Test]
    public async Task A_fault_whose_incident_cannot_be_written_names_the_database_error_and_still_alerts()
    {
        var logs = new List<string>();
        await using var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = false,
            ConfigureServices = s =>
            {
                s.AddSingleton<TranscriptBindingIncidentService>();
                s.AddSingleton<ILogger<TranscriptBindingIncidentService>>(new ListLogger<TranscriptBindingIncidentService>(logs));
            },
        });

        await harness.Provider.GetRequiredService<TranscriptBindingIncidentService>()
            .OnTranscriptFaultAsync(
                new SessionRunnerTranscriptFaultEvent(
                    harness.SessionId,
                    TranscriptFaultKinds.TranscriptMissing,
                    new string('x', 5000),
                    CandidatePath: null),
                CancellationToken.None);

        await using var db = BridgeQueueHarness.CreateContext();
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == harness.AgentId && i.Kind == AgentIncidentKind.TranscriptBindFailed))
            .ShouldBeFalse("the row is what failed to write");

        logs.ShouldContain(
            l => l.Contains("[Error]", StringComparison.Ordinal)
                && l.Contains(harness.SessionId.ToString("D"), StringComparison.OrdinalIgnoreCase)
                && l.Contains("22001", StringComparison.Ordinal),
            customMessage: "the log must carry Postgres's own error, not just \"failed\". Saw:\n"
                + string.Join('\n', logs));

        var alert = await db.Alerts.SingleAsync(
            a => a.DedupKey == TranscriptBindingIncidentService.WriteFailureDedupKey(harness.SessionId));
        alert.SessionId.ShouldBe(harness.SessionId);
        alert.AgentId.ShouldBeNull();
    }

    private static async Task<Guid> BindChannelAsync(Guid agentId)
    {
        await using var db = BridgeQueueHarness.CreateContext();
        var channel = new ChatChannel
        {
            Id = Guid.NewGuid(),
            Provider = "telegram",
            ExternalId = $"transcript-fault-{Guid.NewGuid():N}",
            Kind = ChatChannelKind.Direct,
            Title = "Bound channel (test)",
            AgentId = agentId,
            Enabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.ChatChannels.Add(channel);
        await db.SaveChangesAsync();
        return channel.Id;
    }

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
                sink.Add($"[{logLevel}] {formatter(state, exception)}");
        }
    }
}
