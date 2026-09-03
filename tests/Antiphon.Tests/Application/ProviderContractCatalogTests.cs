using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Agents.Tui;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0083 S2: every <see cref="AgentKind"/> declares every axis, and the declarations lockstep
/// with today's gates so S3's migration cannot drift from reality (or from this catalog) silently.
/// </summary>
[Category("Unit")]
public sealed class ProviderContractCatalogTests
{
    private static readonly AgentKind[] AllKinds = Enum.GetValues<AgentKind>();

    [Test]
    public void Every_AgentKind_has_a_catalog_entry()
    {
        AllKinds.ShouldNotBeEmpty();
        foreach (var kind in AllKinds)
        {
            var contract = ProviderContractCatalog.For(kind);
            contract.Kind.ShouldBe(kind);
        }
    }

    [Test]
    public void An_undefined_kind_throws_rather_than_defaulting()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ProviderContractCatalog.For((AgentKind)int.MaxValue));
    }

    [Test]
    public void Every_axis_is_declared_with_a_reason()
    {
        foreach (var kind in AllKinds)
        {
            var c = ProviderContractCatalog.For(kind);
            Axis(c.Transcript.State, c.Transcript.Reason, $"{kind}.Transcript");
            Axis(c.TurnCompletion.State, c.TurnCompletion.Reason, $"{kind}.TurnCompletion");
            Axis(c.DeliveryVerification.State, c.DeliveryVerification.Reason, $"{kind}.DeliveryVerification");
            Axis(c.SessionResume.State, c.SessionResume.Reason, $"{kind}.SessionResume");
            Axis(c.ContextWindowUsage.State, c.ContextWindowUsage.Reason, $"{kind}.ContextWindowUsage");
            Axis(c.UsageLimitSignal.State, c.UsageLimitSignal.Reason, $"{kind}.UsageLimitSignal");
            Axis(c.Compaction.State, c.Compaction.Reason, $"{kind}.Compaction");
            Axis(c.BlockingStartupModal.State, c.BlockingStartupModal.Reason, $"{kind}.BlockingStartupModal");
            Axis(c.SubscriptionUsagePoll.State, c.SubscriptionUsagePoll.Reason, $"{kind}.SubscriptionUsagePoll");
            Axis(c.TerminalOverlay.State, c.TerminalOverlay.Reason, $"{kind}.TerminalOverlay");
            Axis(c.LocalCommands.State, c.LocalCommands.Reason, $"{kind}.LocalCommands");
            Axis(c.RefocusCompact.State, c.RefocusCompact.Reason, $"{kind}.RefocusCompact");
        }
    }

    [Test]
    public void Nothing_defaults_to_Supported_via_an_empty_reason()
    {
        foreach (var kind in AllKinds)
        {
            var c = ProviderContractCatalog.For(kind);
            if (c.Transcript.State == AgentTuiCapabilityState.Supported)
                c.Transcript.Reason.ShouldNotBeNullOrWhiteSpace();
            if (c.TurnCompletion.State == AgentTuiCapabilityState.Supported)
                c.TurnCompletion.Reason.ShouldNotBeNullOrWhiteSpace();
            if (c.DeliveryVerification.State == AgentTuiCapabilityState.Supported)
                c.DeliveryVerification.Reason.ShouldNotBeNullOrWhiteSpace();
            if (c.SessionResume.State == AgentTuiCapabilityState.Supported)
                c.SessionResume.Reason.ShouldNotBeNullOrWhiteSpace();
            if (c.SubscriptionUsagePoll.State == AgentTuiCapabilityState.Supported)
                c.SubscriptionUsagePoll.Reason.ShouldNotBeNullOrWhiteSpace();
            if (c.TerminalOverlay.State == AgentTuiCapabilityState.Supported)
                c.TerminalOverlay.Reason.ShouldNotBeNullOrWhiteSpace();
            if (c.LocalCommands.State == AgentTuiCapabilityState.Supported)
                c.LocalCommands.Reason.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Test]
    public void The_catalog_forbids_slash_usage_for_Codex_with_a_reason()
    {
        var poll = ProviderContractCatalog.For(AgentKind.Codex).SubscriptionUsagePoll;
        poll.State.ShouldBe(AgentTuiCapabilityState.Supported);
        poll.Command.ShouldBe("/status");

        var forbidden = ProviderContractCatalog.For(AgentKind.Codex).LocalCommands.Forbidden;
        forbidden.ContainsKey("/usage").ShouldBeTrue();
        forbidden["/usage"].ShouldNotBeNullOrWhiteSpace();
        forbidden["/usage"].ShouldContain("reset", Case.Insensitive);
    }

    [Test]
    public void A_Supported_TerminalOverlay_implies_a_non_null_DismissKey()
    {
        foreach (var kind in AllKinds)
        {
            var overlay = ProviderContractCatalog.For(kind).TerminalOverlay;
            if (overlay.State == AgentTuiCapabilityState.Supported)
            {
                overlay.DismissKey.ShouldNotBeNullOrEmpty($"{kind}.TerminalOverlay.DismissKey");
            }
            else
            {
                overlay.DismissKey.ShouldBeNull($"{kind}.TerminalOverlay.DismissKey stays null unless Supported");
            }
        }
    }

    [Test]
    public void Codex_refocus_compact_is_Unsupported_with_the_measured_reason()
    {
        var axis = ProviderContractCatalog.For(AgentKind.Codex).RefocusCompact;
        axis.State.ShouldBe(AgentTuiCapabilityState.Unsupported);
        axis.Command.ShouldBeNull();
        axis.Reason.ShouldContain("51ee57fc");
        axis.Reason.ShouldContain("work turn");
    }

    [Test]
    public void a_non_Supported_refocus_compact_has_a_null_Command()
    {
        foreach (var kind in AllKinds)
        {
            var axis = ProviderContractCatalog.For(kind).RefocusCompact;
            if (axis.State == AgentTuiCapabilityState.Supported)
            {
                axis.Command.ShouldNotBeNullOrWhiteSpace($"{kind}.RefocusCompact.Command");
            }
            else
            {
                axis.Command.ShouldBeNull($"{kind}.RefocusCompact.Command stays null unless Supported");
            }
        }
    }

    [Test]
    public void Claude_refocus_compact_is_Supported_with_slash_compact()
    {
        var axis = ProviderContractCatalog.For(AgentKind.ClaudeCode).RefocusCompact;
        axis.State.ShouldBe(AgentTuiCapabilityState.Supported);
        axis.Command.ShouldBe("/compact");
    }

    [Test]
    public void Grok_refocus_compact_is_Unknown()
    {
        var axis = ProviderContractCatalog.For(AgentKind.Grok).RefocusCompact;
        axis.State.ShouldBe(AgentTuiCapabilityState.Unknown);
        axis.Command.ShouldBeNull();
        axis.Reason.ShouldContain("Never probed");
    }

    [Test]
    public void Claude_compact_is_declared_WritesUserPrompt_true()
    {
        var commands = ProviderContractCatalog.For(AgentKind.ClaudeCode).LocalCommands.Commands;
        commands.ContainsKey("/compact").ShouldBeTrue();
        commands["/compact"].WritesUserPrompt.ShouldBeTrue();
        commands["/compact"].OpensOverlay.ShouldBeFalse();
        commands["/compact"].Evidence.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public void Claude_remote_control_is_declared_WritesUserPrompt_false()
    {
        var commands = ProviderContractCatalog.For(AgentKind.ClaudeCode).LocalCommands.Commands;
        commands.ContainsKey("/remote-control").ShouldBeTrue();
        commands["/remote-control"].WritesUserPrompt.ShouldBeFalse();
        commands["/remote-control"].OpensOverlay.ShouldBeTrue();
        commands["/remote-control"].Evidence.ShouldContain("CARD-0354");
    }

    [Test]
    public void Grok_usage_is_declared_overlay_opening_and_not_forbidden()
    {
        var grok = ProviderContractCatalog.For(AgentKind.Grok);
        grok.TerminalOverlay.State.ShouldBe(AgentTuiCapabilityState.Supported);
        grok.TerminalOverlay.DismissKey.ShouldBe("\u001b");
        grok.LocalCommands.Commands.ContainsKey("/usage").ShouldBeTrue();
        grok.LocalCommands.Commands["/usage"].OpensOverlay.ShouldBeTrue();
        grok.LocalCommands.Commands["/usage"].WritesUserPrompt.ShouldBeFalse();
        grok.LocalCommands.Forbidden.ContainsKey("/usage").ShouldBeFalse();
        grok.TerminalOverlay.DetectFragments.ShouldBe(["c copy session ID"],
            "CARD-0241: the question popup is a dedicated matcher, not a DetectFragments Esc target");
    }

    [Test]
    public void Claude_overlay_is_Supported_with_empty_DetectFragments()
    {
        var overlay = ProviderContractCatalog.For(AgentKind.ClaudeCode).TerminalOverlay;
        overlay.State.ShouldBe(AgentTuiCapabilityState.Supported);
        overlay.DismissKey.ShouldBe("\u001b");
        overlay.DetectFragments.ShouldBeEmpty("S6 stays off until /model fragments are captured");
    }

    [Test]
    public void Codex_overlay_stays_Unknown_without_a_measured_dismiss()
    {
        var overlay = ProviderContractCatalog.For(AgentKind.Codex).TerminalOverlay;
        overlay.State.ShouldBe(AgentTuiCapabilityState.Unknown);
        overlay.DismissKey.ShouldBeNull();
        overlay.DetectFragments.ShouldBeEmpty();
    }

    [Test]
    public void Codex_status_is_declared_and_does_not_write_a_UserPrompt()
    {
        var commands = ProviderContractCatalog.For(AgentKind.Codex).LocalCommands.Commands;
        commands.ContainsKey("/status").ShouldBeTrue();
        commands["/status"].OpensOverlay.ShouldBeFalse();
        commands["/status"].WritesUserPrompt.ShouldBeFalse();
    }

    [Test]
    public void Degraded_reasons_name_the_weakness()
    {
        foreach (var kind in AllKinds)
        {
            var c = ProviderContractCatalog.For(kind);
            if (c.TurnCompletion.State == AgentTuiCapabilityState.Degraded)
            {
                c.TurnCompletion.Reason.ShouldContain("weaker", Case.Insensitive);
                c.TurnCompletion.Signal.ShouldBe(TurnCompletionSignal.QuietTimeOnly);
            }

            if (c.ContextWindowUsage.State == AgentTuiCapabilityState.Degraded)
                c.ContextWindowUsage.Reason.ShouldContain("Weaker guarantee");
        }
    }

    // ---- lockstep with today's gates --------------------------------------------------------

    [Test]
    public void Transcript_Supported_locksteps_TranscriptEnabledFor()
    {
        foreach (var kind in AllKinds)
        {
            var supported = ProviderContractCatalog.For(kind).Transcript.State
                == AgentTuiCapabilityState.Supported;
            SessionRunnerHttpClient.TranscriptEnabledFor(kind)
                .ShouldBe(supported, $"{kind}: catalog Transcript.State vs TranscriptEnabledFor");
        }
    }

    [Test]
    public void Transcript_format_and_discovery_match_the_runner_mapping()
    {
        var claude = ProviderContractCatalog.For(AgentKind.ClaudeCode).Transcript;
        claude.Format.ShouldBe(TranscriptFormats.Claude);
        claude.Discovery.ShouldBe(TranscriptDiscovery.DiscoveryWithClaims);
        // Transport still sends null for Claude so an old runner keeps its pre-Grok default.
        SessionRunnerHttpClient.TranscriptFormatFor(AgentKind.ClaudeCode).ShouldBeNull();

        var grok = ProviderContractCatalog.For(AgentKind.Grok).Transcript;
        grok.Format.ShouldBe(TranscriptFormats.Grok);
        grok.Discovery.ShouldBe(TranscriptDiscovery.DeterministicPath);
        SessionRunnerHttpClient.TranscriptFormatFor(AgentKind.Grok).ShouldBe(grok.Format);

        // CARD-0099 S1. Codex is the third tailed kind and the second DISCOVERED one: it honours no
        // session-id flag, so unlike Grok its path cannot be computed before launch.
        var codex = ProviderContractCatalog.For(AgentKind.Codex).Transcript;
        codex.Format.ShouldBe(TranscriptFormats.Codex);
        codex.Discovery.ShouldBe(TranscriptDiscovery.DiscoveryWithClaims);
        SessionRunnerHttpClient.TranscriptFormatFor(AgentKind.Codex).ShouldBe(codex.Format);

        foreach (var kind in AllKinds.Where(k =>
            k is not AgentKind.ClaudeCode and not AgentKind.Grok and not AgentKind.Codex))
        {
            var t = ProviderContractCatalog.For(kind).Transcript;
            t.State.ShouldBe(AgentTuiCapabilityState.Unsupported);
            t.Format.ShouldBeNull();
            t.Discovery.ShouldBe(TranscriptDiscovery.None);
            SessionRunnerHttpClient.TranscriptEnabledFor(kind).ShouldBeFalse();
        }
    }

    [Test]
    public void DeliveryVerification_Supported_locksteps_the_queue_kind_list()
    {
        // SessionMessageQueueService.IsVerifiedDeliverySessionAsync: Claude|Grok|Codex — the
        // gate reads this axis, so the list here is only the documented mirror of it (CARD-0099 S1
        // added Codex once its rollout gave CARD-0055 something to confirm against).
        foreach (var kind in AllKinds)
        {
            var supported = ProviderContractCatalog.For(kind).DeliveryVerification.State
                == AgentTuiCapabilityState.Supported;
            var queueVerifies = kind is AgentKind.ClaudeCode or AgentKind.Grok or AgentKind.Codex;
            supported.ShouldBe(queueVerifies, $"{kind}: DeliveryVerification vs IsVerifiedDeliverySessionAsync");
        }
    }

    [Test]
    public void SessionResume_Supported_locksteps_the_resume_and_identity_arg_gates()
    {
        // AgentSessionService resume gate (:695) and UsesSessionIdentityArgs: Claude|Grok.
        foreach (var kind in AllKinds)
        {
            var supported = ProviderContractCatalog.For(kind).SessionResume.State
                == AgentTuiCapabilityState.Supported;
            var resumeGatedOn = kind is AgentKind.ClaudeCode or AgentKind.Grok;
            supported.ShouldBe(resumeGatedOn, $"{kind}: SessionResume vs resume/identity-args gates");
        }
    }

    [Test]
    public void TurnCompletion_Claude_is_structured_matching_ActivityModeFor_Structured()
    {
        var turn = ProviderContractCatalog.For(AgentKind.ClaudeCode).TurnCompletion;
        turn.State.ShouldBe(AgentTuiCapabilityState.Supported);
        turn.Signal.ShouldBe(TurnCompletionSignal.StructuredTranscript);
        turn.HasScreenFallback.ShouldBeTrue();
        AgentTuiLaunchResolver.ActivityModeFor(AgentKind.ClaudeCode)
            .ShouldBe(AgentTuiLaunchActivityMode.Structured);
    }

    [Test]
    public void TurnCompletion_Grok_is_structured_with_screen_fallback()
    {
        var turn = ProviderContractCatalog.For(AgentKind.Grok).TurnCompletion;
        turn.State.ShouldBe(AgentTuiCapabilityState.Supported);
        turn.Signal.ShouldBe(TurnCompletionSignal.StructuredTranscript);
        turn.HasScreenFallback.ShouldBeTrue();
    }

    [Test]
    public void ActivityModeFor_Grok_is_Structured_after_CARD_0080_S2()
    {
        // Deliberate S3 fix: ActivityModeFor used to map Grok → QuietTime, stale since the
        // Grok tailer landed (CARD-0080 S2). It now reads TurnCompletion.Signal.
        AgentTuiLaunchResolver.ActivityModeFor(AgentKind.Grok)
            .ShouldBe(AgentTuiLaunchActivityMode.Structured);
        foreach (var kind in new[] { AgentKind.OpenCode, AgentKind.Raw })
        {
            AgentTuiLaunchResolver.ActivityModeFor(kind)
                .ShouldBe(AgentTuiLaunchActivityMode.QuietTime);
        }
    }

    [Test]
    public void ActivityModeFor_Codex_is_Structured_after_CARD_0099_S1()
    {
        // Same derivation, one card later: the Codex rollout tailer makes task_complete the
        // activity signal, so Codex can no longer be listed with the quiet-time-only kinds.
        AgentTuiLaunchResolver.ActivityModeFor(AgentKind.Codex)
            .ShouldBe(AgentTuiLaunchActivityMode.Structured);
    }

    [Test]
    public void Grok_structuredActivity_TUI_row_is_Supported_after_CARD_0080_S2()
    {
        // Deliberate S3/D2 fix: AgentTuiRunnerCatalog.structuredActivity is derived from
        // TurnCompletion, so Grok can no longer say Degraded "ACP session updates are not tailed"
        // while the tailer is live.
        var row = new AgentTuiRunnerCatalog().Get(AgentKind.Grok).Capabilities
            .Single(c => c.Name == "structuredActivity");
        var turn = ProviderContractCatalog.For(AgentKind.Grok).TurnCompletion;
        row.State.ShouldBe(AgentTuiCapabilityState.Supported);
        row.State.ShouldBe(turn.State);
        row.Reason.ShouldBe(turn.Reason);
        row.Reason.ShouldContain("CARD-0080 S2");
    }

    [Test]
    public void Tui_structuredActivity_is_derived_from_TurnCompletion_for_every_kind()
    {
        var tui = new AgentTuiRunnerCatalog();
        foreach (var kind in AllKinds)
        {
            var row = tui.Get(kind).Capabilities.Single(c => c.Name == "structuredActivity");
            var turn = ProviderContractCatalog.For(kind).TurnCompletion;
            row.State.ShouldBe(turn.State, $"{kind}.structuredActivity.State");
            row.Reason.ShouldBe(turn.Reason, $"{kind}.structuredActivity.Reason");
        }
    }

    [Test]
    public void TurnCompletion_without_a_transcript_is_quiet_time_Degraded()
    {
        foreach (var kind in new[] { AgentKind.OpenCode, AgentKind.Raw })
        {
            var turn = ProviderContractCatalog.For(kind).TurnCompletion;
            turn.State.ShouldBe(AgentTuiCapabilityState.Degraded);
            turn.Signal.ShouldBe(TurnCompletionSignal.QuietTimeOnly);
            turn.HasScreenFallback.ShouldBeFalse();
        }
    }

    /// <summary>
    /// CARD-0099 S1. The three axes the Codex transcript pipeline moves, asserted together because
    /// they are one fact: a Codex delegate cannot settle without a TurnEnd row, and CARD-0055
    /// delivery confirmation stays permanently degraded to the screen-only verdict it replaced
    /// unless DeliveryVerification is Supported.
    /// </summary>
    [Test]
    public void Codex_transcript_turn_completion_and_delivery_verification_after_CARD_0099_S1()
    {
        var codex = ProviderContractCatalog.For(AgentKind.Codex);

        codex.Transcript.State.ShouldBe(AgentTuiCapabilityState.Supported);
        codex.Transcript.Format.ShouldBe(TranscriptFormats.Codex);
        // Discovery, not a deterministic path: Codex honours no session-id flag and its TUI never
        // prints its id, so the CARD-0006 claim rules are what make a bind safe.
        codex.Transcript.Discovery.ShouldBe(TranscriptDiscovery.DiscoveryWithClaims);
        codex.Transcript.Reason.ShouldContain("rollout");
        codex.Transcript.Reason.ShouldContain("lazily");

        codex.TurnCompletion.State.ShouldBe(AgentTuiCapabilityState.Supported);
        codex.TurnCompletion.Signal.ShouldBe(TurnCompletionSignal.StructuredTranscript);
        codex.TurnCompletion.HasScreenFallback.ShouldBeTrue();
        codex.TurnCompletion.Reason.ShouldContain("task_complete");

        codex.DeliveryVerification.State.ShouldBe(AgentTuiCapabilityState.Supported);

        // The two derived consumers that turn these facts into behaviour.
        SessionRunnerHttpClient.TranscriptEnabledFor(AgentKind.Codex).ShouldBeTrue();
        SessionRunnerHttpClient.TranscriptFormatFor(AgentKind.Codex).ShouldBe(TranscriptFormats.Codex);
    }

    [Test]
    public void UsageLimitSignal_unsurveyed_kinds_stay_Unknown_pending_S1()
    {
        foreach (var kind in new[] { AgentKind.Codex, AgentKind.OpenCode })
        {
            var axis = ProviderContractCatalog.For(kind).UsageLimitSignal;
            axis.State.ShouldBe(AgentTuiCapabilityState.Unknown);
            axis.Form.ShouldBe(UsageLimitSignalForm.Unknown);
            axis.StatesResetTime.ShouldBeNull();
            axis.Reason.ShouldBe("pending CARD-0083 S1 survey");
        }
    }

    [Test]
    public void Grok_usage_limit_is_structural_on_agent_result_and_states_no_reset()
    {
        var axis = ProviderContractCatalog.For(AgentKind.Grok).UsageLimitSignal;
        axis.State.ShouldBe(AgentTuiCapabilityState.Supported);
        axis.Form.ShouldBe(UsageLimitSignalForm.StructuralField);
        axis.StatesResetTime.ShouldBe(false);
        axis.Reason.ShouldContain("agent_result");
        axis.Reason.ShouldContain("402");
    }

    [Test]
    public void Claude_usage_limit_is_structural_and_states_reset_time()
    {
        var axis = ProviderContractCatalog.For(AgentKind.ClaudeCode).UsageLimitSignal;
        axis.State.ShouldBe(AgentTuiCapabilityState.Supported);
        axis.Form.ShouldBe(UsageLimitSignalForm.StructuralField);
        axis.StatesResetTime.ShouldBe(true);
    }

    [Test]
    public void Grok_compaction_is_marked_checkpoint_rows_not_session_recap()
    {
        var axis = ProviderContractCatalog.For(AgentKind.Grok).Compaction;
        axis.State.ShouldBe(AgentTuiCapabilityState.Supported);
        axis.Marking.ShouldBe(CompactionMarking.Marked);
        axis.Reason.ShouldContain("compaction_checkpoint");
        axis.Reason.ShouldContain("session_recap is a recap");
    }

    [Test]
    public void Claude_compaction_records_the_unmarked_auto_hazard()
    {
        var axis = ProviderContractCatalog.For(AgentKind.ClaudeCode).Compaction;
        axis.State.ShouldBe(AgentTuiCapabilityState.Supported);
        axis.Marking.ShouldBe(CompactionMarking.UnmarkedAuto);
    }

    [Test]
    public void BlockingStartupModal_matches_the_adapter_handlers()
    {
        var claude = ProviderContractCatalog.For(AgentKind.ClaudeCode).BlockingStartupModal;
        claude.State.ShouldBe(AgentTuiCapabilityState.Supported);
        claude.Kind.ShouldBe(BlockingStartupModalKind.AutoAnswerable);
        claude.PerScope.ShouldBe(BlockingStartupModalScope.Cwd);

        var grok = ProviderContractCatalog.For(AgentKind.Grok).BlockingStartupModal;
        grok.State.ShouldBe(AgentTuiCapabilityState.Supported);
        grok.Kind.ShouldBe(BlockingStartupModalKind.AutoAnswerable);
        grok.PerScope.ShouldBe(BlockingStartupModalScope.Cwd);
        grok.Reason.ShouldContain("CARD-0315");
        grok.Reason.ShouldContain("CARD-0324");
        grok.Reason.ShouldContain("Approve in your browser");
        grok.Reason.ShouldContain("grok login");

        var codex = ProviderContractCatalog.For(AgentKind.Codex).BlockingStartupModal;
        codex.State.ShouldBe(AgentTuiCapabilityState.Supported);
        codex.Kind.ShouldBe(BlockingStartupModalKind.AutoAnswerable);
        codex.PerScope.ShouldBe(BlockingStartupModalScope.Cwd);
    }

    [Test]
    public void ContextWindowUsage_ceiling_sources()
    {
        var claude = ProviderContractCatalog.For(AgentKind.ClaudeCode).ContextWindowUsage;
        claude.State.ShouldBe(AgentTuiCapabilityState.Supported);
        claude.CeilingSource.ShouldBe(ContextWindowCeilingSource.Configured);

        var grok = ProviderContractCatalog.For(AgentKind.Grok).ContextWindowUsage;
        grok.State.ShouldBe(AgentTuiCapabilityState.Supported);
        grok.CeilingSource.ShouldBe(ContextWindowCeilingSource.SelfReported);
        grok.SelfReportedCeilingTokens.ShouldBe(500_000);
        grok.UsageAccounting.ShouldBe(ProviderUsageAccounting.TurnSumInclusiveCache);
    }

    [Test]
    public void Tui_sessionResume_Supported_kinds_match_the_contract()
    {
        // Cheap cross-check against the existing TUI catalog's sessionResume row. Codex/OpenCode
        // are Unknown on both; Raw is Unsupported here (no identity args) vs Unknown on the TUI
        // display list — different axis family, settled fact vs unprobed.
        var tui = new AgentTuiRunnerCatalog();
        foreach (var kind in new[] { AgentKind.ClaudeCode, AgentKind.Grok })
        {
            var row = tui.Get(kind).Capabilities.Single(c => c.Name == "sessionResume");
            row.State.ShouldBe(AgentTuiCapabilityState.Supported);
            ProviderContractCatalog.For(kind).SessionResume.State.ShouldBe(AgentTuiCapabilityState.Supported);
        }
    }

    private static void Axis(AgentTuiCapabilityState state, string reason, string name)
    {
        Enum.IsDefined(state).ShouldBeTrue(name);
        reason.ShouldNotBeNullOrWhiteSpace(name);
    }
}
