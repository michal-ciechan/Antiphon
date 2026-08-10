using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
}
