using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0082 S1: live context fullness is a pure function of the newest usage-bearing row,
/// invalidated by a later CompactBoundary or /clear, with the ceiling resolved from config.
/// </summary>
[Category("Unit")]
public class SessionContextUsageTests
{
    private static readonly ContextWindowSettings Defaults = new();

    [Test]
    public void Formula_sums_input_cache_and_output_of_the_newest_usage_row()
    {
        var rows = new[]
        {
            Usage(1, input: 10, cacheRead: 100, cacheCreate: 20, output: 5),
            Usage(2, input: 2, cacheRead: 122_765, cacheCreate: 3_744, output: 7_098),
        };

        var result = SessionContextUsage.Compute(rows, fallbackModelId: null, Defaults);

        // 2 + 122765 + 3744 + 7098 = 133609; default ceiling 200000
        result.TokensUsed.ShouldBe(133_609);
        result.CeilingTokens.ShouldBe(200_000);
        result.Fullness.ShouldNotBeNull();
        result.Fullness!.Value.ShouldBe(133_609 / 200_000.0, 1e-12);
    }

    [Test]
    public void Output_tokens_are_included()
    {
        var rows = new[] { Usage(1, input: 100, cacheRead: 0, cacheCreate: 0, output: 50) };

        var withoutOutputWouldBe = 100 / 200_000.0;
        var result = SessionContextUsage.Compute(rows, null, Defaults);

        result.TokensUsed.ShouldBe(150);
        result.Fullness.ShouldNotBeNull();
        result.Fullness!.Value.ShouldBeGreaterThan(withoutOutputWouldBe);
        result.Fullness.Value.ShouldBe(150 / 200_000.0, 1e-12);
    }

    [Test]
    public void A_later_compact_boundary_invalidates_to_unknown()
    {
        var rows = new[]
        {
            Usage(1, input: 50_000, cacheRead: 0, cacheCreate: 0, output: 1_000),
            Row(2, TranscriptKinds.CompactBoundary, "Context compacted (manual)"),
        };

        var result = SessionContextUsage.Compute(rows, null, Defaults);

        result.Fullness.ShouldBeNull();
        result.TokensUsed.ShouldBe(51_000, "the stale sum is still visible; fullness is what is unknown");
    }

    [Test]
    public void A_later_auto_compact_boundary_also_invalidates()
    {
        var rows = new[]
        {
            Usage(1, input: 180_000, cacheRead: 0, cacheCreate: 0, output: 0),
            Row(2, TranscriptKinds.CompactBoundary, "Context compacted (auto)"),
        };

        SessionContextUsage.Compute(rows, null, Defaults).Fullness.ShouldBeNull();
    }

    [Test]
    public void A_later_clear_local_command_invalidates_to_unknown()
    {
        var rows = new[]
        {
            Usage(1, input: 50_000, cacheRead: 0, cacheCreate: 0, output: 0),
            Row(2, TranscriptKinds.UserPrompt,
                "<command-name>/clear</command-name>\n<command-message>clear</command-message>"),
        };

        SessionContextUsage.Compute(rows, null, Defaults).Fullness.ShouldBeNull();
    }

    [Test]
    public void A_compact_or_clear_before_the_usage_row_does_not_invalidate()
    {
        var rows = new[]
        {
            Row(1, TranscriptKinds.CompactBoundary, "Context compacted (manual)"),
            Row(2, TranscriptKinds.UserPrompt,
                "<command-name>/clear</command-name>\n<command-message>clear</command-message>"),
            Usage(3, input: 1_000, cacheRead: 0, cacheCreate: 0, output: 10),
        };

        var result = SessionContextUsage.Compute(rows, null, Defaults);

        result.Fullness.ShouldNotBeNull();
        result.TokensUsed.ShouldBe(1_010);
    }

    [Test]
    public void Ceiling_comes_from_the_usage_row_model_override()
    {
        var settings = new ContextWindowSettings
        {
            DefaultContextTokens = 200_000,
            ModelOverrides = { ["[1m]"] = 1_000_000 },
        };
        var rows = new[]
        {
            Usage(1, input: 200_000, cacheRead: 0, cacheCreate: 0, output: 0, model: "claude-opus-4-[1m]"),
        };

        var result = SessionContextUsage.Compute(rows, fallbackModelId: "ignored", settings);

        result.CeilingTokens.ShouldBe(1_000_000);
        result.ModelId.ShouldBe("claude-opus-4-[1m]");
        result.Fullness.ShouldNotBeNull();
        result.Fullness!.Value.ShouldBe(0.2, 1e-12);
    }

    [Test]
    public void Ceiling_falls_back_to_the_session_effective_model_when_the_row_has_none()
    {
        var settings = new ContextWindowSettings
        {
            DefaultContextTokens = 200_000,
            ModelOverrides = { ["opus"] = 200_000, ["[1m]"] = 1_000_000 },
        };
        var rows = new[] { Usage(1, input: 100_000, cacheRead: 0, cacheCreate: 0, output: 0, model: null) };

        var result = SessionContextUsage.Compute(rows, fallbackModelId: "claude-opus-4-[1m]", settings);

        result.CeilingTokens.ShouldBe(1_000_000, "longest matching key ([1m]) beats the shorter 'opus'");
        result.ModelId.ShouldBe("claude-opus-4-[1m]");
    }

    [Test]
    public void Ceiling_falls_back_to_the_default_when_neither_row_nor_session_has_a_model()
    {
        var rows = new[] { Usage(1, input: 50_000, cacheRead: 0, cacheCreate: 0, output: 0, model: null) };

        var result = SessionContextUsage.Compute(rows, fallbackModelId: null, Defaults);

        result.CeilingTokens.ShouldBe(200_000);
        result.ModelId.ShouldBeNull();
        result.Fullness.ShouldNotBeNull();
        result.Fullness!.Value.ShouldBe(0.25, 1e-12);
    }

    [Test]
    public void Override_match_is_case_insensitive()
    {
        var settings = new ContextWindowSettings
        {
            ModelOverrides = { ["FABLE"] = 400_000 },
        };
        var rows = new[] { Usage(1, input: 100_000, cacheRead: 0, cacheCreate: 0, output: 0, model: "claude-fable-5") };

        SessionContextUsage.Compute(rows, null, settings).CeilingTokens.ShouldBe(400_000);
    }

    [Test]
    public void No_usage_bearing_row_is_unknown()
    {
        var rows = new[]
        {
            Row(1, TranscriptKinds.UserPrompt, "hello"),
            Row(2, TranscriptKinds.AssistantText, "hi", isApiError: true),
        };

        var result = SessionContextUsage.Compute(rows, "claude-opus-4", Defaults);

        result.Fullness.ShouldBeNull();
        result.TokensUsed.ShouldBeNull();
        result.ModelId.ShouldBe("claude-opus-4");
    }

    [Test]
    public void An_api_error_stub_never_wins_even_when_it_carries_zeroed_usage()
    {
        var rows = new[]
        {
            Usage(1, input: 80_000, cacheRead: 0, cacheCreate: 0, output: 0),
            new TranscriptContextRow(
                2, TranscriptKinds.AssistantText, "You've hit your session limit",
                InputTokens: 0, OutputTokens: 0, CacheReadTokens: 0, CacheCreationTokens: 0,
                Model: null, IsApiError: true),
        };

        var result = SessionContextUsage.Compute(rows, null, Defaults);

        result.Fullness.ShouldNotBeNull();
        result.TokensUsed.ShouldBe(80_000);
    }

    [Test]
    public void Empty_rows_are_unknown()
    {
        SessionContextUsage.Compute([], null, Defaults).Fullness.ShouldBeNull();
    }

    [Test]
    public void State_is_NoUsageYet_with_no_usage_rows()
    {
        var rows = new[]
        {
            Row(1, TranscriptKinds.UserPrompt, "hello"),
            Row(2, TranscriptKinds.AssistantText, "hi"),
        };

        var result = SessionContextUsage.Compute(rows, "claude-opus-4", Defaults);

        result.Fullness.ShouldBeNull();
        result.TokensUsed.ShouldBeNull();
        result.State.ShouldBe(ContextFullnessState.NoUsageYet);
        SessionContextUsage.Compute([], null, Defaults).State.ShouldBe(ContextFullnessState.NoUsageYet);
    }

    [Test]
    public void State_is_Compacted_after_a_boundary()
    {
        var rows = new[]
        {
            Usage(1, input: 50_000, cacheRead: 0, cacheCreate: 0, output: 1_000),
            Row(2, TranscriptKinds.CompactBoundary, "Context compacted (manual)"),
        };

        var result = SessionContextUsage.Compute(rows, null, Defaults);

        result.Fullness.ShouldBeNull();
        result.TokensUsed.ShouldBe(51_000);
        result.State.ShouldBe(ContextFullnessState.Compacted);
    }

    [Test]
    public void State_is_Cleared_after_a_clear_command()
    {
        var rows = new[]
        {
            Usage(1, input: 50_000, cacheRead: 0, cacheCreate: 0, output: 0),
            Row(2, TranscriptKinds.UserPrompt,
                "<command-name>/clear</command-name>\n<command-message>clear</command-message>"),
        };

        var result = SessionContextUsage.Compute(rows, null, Defaults);

        result.Fullness.ShouldBeNull();
        result.State.ShouldBe(ContextFullnessState.Cleared);
    }

    [Test]
    public void State_is_Suppressed_for_a_degraded_self_reported_contract()
    {
        var rows = new[] { Usage(1, input: 18_700_000, cacheRead: 0, cacheCreate: 0, output: 0) };
        var suppressed = new ContextWindowUsageContract(
            AgentTuiCapabilityState.Degraded,
            "synthetic Degraded+SelfReported",
            ContextWindowCeilingSource.SelfReported);

        var result = SessionContextUsage.Compute(rows, "model", Defaults, contract: suppressed);

        result.Fullness.ShouldBeNull();
        result.State.ShouldBe(ContextFullnessState.Suppressed);
    }

    [Test]
    public void State_is_Known_with_fullness()
    {
        var rows = new[] { Usage(1, input: 40_000, cacheRead: 0, cacheCreate: 0, output: 0) };

        var result = SessionContextUsage.Compute(rows, null, Defaults);

        result.Fullness.ShouldNotBeNull();
        result.Fullness!.Value.ShouldBe(40_000 / 200_000.0, 1e-12);
        result.State.ShouldBe(ContextFullnessState.Known);
    }

    [Test]
    public void Fullness_over_100_percent_logs_a_warning_naming_model_and_ceiling()
    {
        var sink = new List<string>();
        var rows = new[]
        {
            Usage(1, input: 250_000, cacheRead: 0, cacheCreate: 0, output: 0, model: "claude-fable-5"),
        };

        var result = SessionContextUsage.Compute(rows, null, Defaults, new ListLogger(sink));

        result.Fullness.ShouldNotBeNull();
        result.Fullness!.Value.ShouldBeGreaterThan(1.0);
        sink.ShouldHaveSingleItem();
        sink[0].ShouldContain("[Warning]");
        sink[0].ShouldContain("claude-fable-5");
        sink[0].ShouldContain("200000");
    }

    [Test]
    public void An_auto_compact_at_under_80_percent_logs_a_warning()
    {
        var sink = new List<string>();
        var rows = new[]
        {
            Usage(1, input: 100_000, cacheRead: 0, cacheCreate: 0, output: 0, model: "claude-opus-4"),
            Row(2, TranscriptKinds.CompactBoundary, "Context compacted (auto)"),
        };

        var result = SessionContextUsage.Compute(rows, null, Defaults, new ListLogger(sink));

        result.Fullness.ShouldBeNull("the boundary still invalidates");
        sink.ShouldHaveSingleItem();
        sink[0].ShouldContain("[Warning]");
        sink[0].ShouldContain("(auto)");
        sink[0].ShouldContain("claude-opus-4");
        sink[0].ShouldContain("200000");
    }

    [Test]
    public void An_auto_compact_at_or_above_80_percent_does_not_warn_about_headroom()
    {
        var sink = new List<string>();
        var rows = new[]
        {
            Usage(1, input: 160_000, cacheRead: 0, cacheCreate: 0, output: 0, model: "claude-opus-4"),
            Row(2, TranscriptKinds.CompactBoundary, "Context compacted (auto)"),
        };

        SessionContextUsage.Compute(rows, null, Defaults, new ListLogger(sink));

        sink.ShouldBeEmpty();
    }

    [Test]
    public void Grok_catalog_computes_occupancy_from_a_single_call_row()
    {
        var rows = new[] { GrokTurn(1, input: 137_657, modelCalls: 1, cacheRead: 120_000) };
        var grok = ProviderContractCatalog.For(AgentKind.Grok).ContextWindowUsage;

        var result = SessionContextUsage.Compute(rows, "grok-code", Defaults, contract: grok);

        grok.State.ShouldBe(AgentTuiCapabilityState.Supported);
        grok.CeilingSource.ShouldBe(ContextWindowCeilingSource.SelfReported);
        grok.UsageAccounting.ShouldBe(ProviderUsageAccounting.TurnSumInclusiveCache);
        grok.SelfReportedCeilingTokens.ShouldBe(500_000);
        result.TokensUsed.ShouldBe(137_657, "kind-aware TokensOf: InputTokens alone");
        result.CeilingTokens.ShouldBe(500_000);
        result.Fullness.ShouldNotBeNull();
        result.Fullness!.Value.ShouldBe(137_657 / 500_000.0, 1e-12);
    }

    [Test]
    public void Claude_contract_over_the_same_huge_rows_stays_unclamped()
    {
        var rows = new[] { Usage(1, input: 18_700_000, cacheRead: 0, cacheCreate: 0, output: 0) };
        var claude = ProviderContractCatalog.For(AgentKind.ClaudeCode).ContextWindowUsage;

        var result = SessionContextUsage.Compute(rows, "claude-opus-4", Defaults, contract: claude);

        result.Fullness.ShouldNotBeNull();
        result.Fullness!.Value.ShouldBeGreaterThan(1.0);
        result.TokensUsed.ShouldBe(18_700_000);
    }

    [Test]
    public void Fullness_suppression_is_contract_keyed()
    {
        var rows = new[] { Usage(1, input: 18_700_000, cacheRead: 0, cacheCreate: 0, output: 0) };
        var suppressed = new ContextWindowUsageContract(
            AgentTuiCapabilityState.Degraded,
            "synthetic Degraded+SelfReported",
            ContextWindowCeilingSource.SelfReported);
        SessionContextUsage.Compute(rows, "model", Defaults, contract: suppressed)
            .Fullness.ShouldBeNull("the kept CARD-0153 gate is contract-keyed");

        var live = new ContextWindowUsageContract(
            AgentTuiCapabilityState.Supported,
            "synthetic Supported+SelfReported",
            ContextWindowCeilingSource.SelfReported);
        SessionContextUsage.Compute(rows, "model", Defaults, contract: live)
            .Fullness.ShouldNotBeNull("Supported+SelfReported is not gags");
    }

    [Test]
    public void Every_kind_computes_or_abstains_per_its_contract()
    {
        foreach (var kind in Enum.GetValues<AgentKind>())
        {
            var contract = ProviderContractCatalog.For(kind).ContextWindowUsage;
            if (kind == AgentKind.ClaudeCode)
            {
                var rows = new[] { Usage(1, input: 10_000, cacheRead: 0, cacheCreate: 0, output: 0) };
                SessionContextUsage.Compute(rows, "claude-opus-4", Defaults, contract: contract)
                    .Fullness.ShouldNotBeNull("Claude computes from an additive row");
                continue;
            }

            if (kind == AgentKind.Grok)
            {
                var loopSum = new[] { GrokTurn(1, input: 18_747_424, modelCalls: 103) };
                SessionContextUsage.Compute(loopSum, "grok-4.6-build", Defaults, contract: contract)
                    .Fullness.ShouldBeNull("18.7M loop-sum is not occupancy by arithmetic, not a gag");
                var single = new[] { GrokTurn(1, input: 137_657, modelCalls: 1) };
                SessionContextUsage.Compute(single, "grok-4.6-build", Defaults, contract: contract)
                    .Fullness.ShouldNotBeNull("Grok computes from a single-call row");
                continue;
            }

            var generic = new[] { Usage(1, input: 10_000, cacheRead: 0, cacheCreate: 0, output: 0) };
            SessionContextUsage.Compute(generic, "model", Defaults, contract: contract)
                .Fullness.ShouldNotBeNull($"{kind} must not inherit a fullness gag");
        }
    }

    // CARD-0157 S3: Grok occupancy against the Supported test contract (catalog stays Degraded
    // until S4). Fixtures modelled on sessions 98c61e03 / 1636e434.
    private static readonly ContextWindowUsageContract GrokLive = new(
        AgentTuiCapabilityState.Supported,
        "CARD-0157 S3 pin",
        ContextWindowCeilingSource.SelfReported,
        UsageAccounting: ProviderUsageAccounting.TurnSumInclusiveCache,
        SelfReportedCeilingTokens: 500_000);

    [Test]
    public void Grok_multi_call_turn_is_not_occupancy_and_does_not_report_3740_percent()
    {
        var rows = new[] { GrokTurn(1, input: 18_747_424, modelCalls: 103, cacheRead: 18_482_432) };

        var result = SessionContextUsage.Compute(rows, "grok-4.6-build", Defaults, contract: GrokLive);

        result.Fullness.ShouldBeNull();
        result.TokensUsed.ShouldBeNull();
        result.CeilingTokens.ShouldBe(500_000);
    }

    [Test]
    public void Grok_single_call_turn_is_input_tokens_alone_against_500k()
    {
        var rows = new[] { GrokTurn(1, input: 137_657, modelCalls: 1, cacheRead: 120_000) };

        var result = SessionContextUsage.Compute(rows, "grok-4.6-build", Defaults, contract: GrokLive);

        result.TokensUsed.ShouldBe(137_657);
        result.CeilingTokens.ShouldBe(500_000);
        result.Fullness.ShouldNotBeNull();
        result.Fullness!.Value.ShouldBe(137_657 / 500_000.0, 1e-12);
        result.ModelId.ShouldBe("grok-4.6-build");
    }

    [Test]
    public void Grok_multi_call_after_single_call_leaves_the_single_call_anchor()
    {
        var t0 = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);
        var rows = new[]
        {
            GrokTurn(1, input: 137_657, modelCalls: 1, timestamp: t0),
            GrokTurn(2, input: 3_200_000, modelCalls: 20, timestamp: t0.AddMinutes(5)),
        };

        var result = SessionContextUsage.Compute(rows, "grok-4.6-build", Defaults, contract: GrokLive);

        result.TokensUsed.ShouldBe(137_657);
        result.Fullness!.Value.ShouldBe(137_657 / 500_000.0, 1e-12);
    }

    [Test]
    public void Grok_usage_bearing_boundary_resets_occupancy_to_tokens_after()
    {
        var t0 = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);
        var rows = new[]
        {
            GrokTurn(1, input: 137_657, modelCalls: 1, timestamp: t0),
            GrokTurn(2, input: 3_200_000, modelCalls: 20, timestamp: t0.AddMinutes(5)),
            GrokBoundary(3, input: 34_833, timestamp: t0.AddMinutes(6)),
        };

        var result = SessionContextUsage.Compute(rows, "grok-4.6-build", Defaults, contract: GrokLive);

        result.TokensUsed.ShouldBe(34_833);
        result.Fullness!.Value.ShouldBe(34_833 / 500_000.0, 1e-12);
    }

    [Test]
    public void Grok_single_call_after_boundary_refreshes_occupancy()
    {
        var t0 = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);
        var rows = new[]
        {
            GrokTurn(1, input: 137_657, modelCalls: 1, timestamp: t0),
            GrokBoundary(2, input: 34_833, timestamp: t0.AddMinutes(6)),
            GrokTurn(3, input: 99_350, modelCalls: 1, timestamp: t0.AddMinutes(10)),
        };

        var result = SessionContextUsage.Compute(rows, "grok-4.6-build", Defaults, contract: GrokLive);

        result.TokensUsed.ShouldBe(99_350);
        result.Fullness!.Value.ShouldBe(99_350 / 500_000.0, 1e-12);
    }

    [Test]
    public void Grok_retail_older_boundary_with_higher_sequence_does_not_beat_a_newer_turn()
    {
        var t0 = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);
        var rows = new[]
        {
            GrokTurn(1, input: 137_657, modelCalls: 1, timestamp: t0.AddMinutes(10)),
            GrokBoundary(2, input: 34_833, timestamp: t0),
        };

        var result = SessionContextUsage.Compute(rows, "grok-4.6-build", Defaults, contract: GrokLive);

        result.TokensUsed.ShouldBe(137_657, "timestamp-newest wins; sequence-only would pick the re-tailed boundary");
        result.Fullness!.Value.ShouldBe(137_657 / 500_000.0, 1e-12);
    }

    [Test]
    public void Grok_tokens_less_boundary_newest_invalidates_to_unknown()
    {
        var t0 = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);
        var rows = new[]
        {
            GrokTurn(1, input: 137_657, modelCalls: 1, timestamp: t0),
            GrokBoundary(2, input: null, timestamp: t0.AddMinutes(1)),
        };

        var result = SessionContextUsage.Compute(rows, "grok-4.6-build", Defaults, contract: GrokLive);

        result.Fullness.ShouldBeNull();
        result.TokensUsed.ShouldBe(137_657);
    }

    [Test]
    public void Grok_ceiling_is_catalog_constant_unless_an_override_matches()
    {
        var rows = new[] { GrokTurn(1, input: 137_657, modelCalls: 1, model: "grok-4.6-build") };

        SessionContextUsage.Compute(rows, "grok-4.6-build", Defaults, contract: GrokLive)
            .CeilingTokens.ShouldBe(500_000);

        var overridden = new ContextWindowSettings
        {
            DefaultContextTokens = 200_000,
            ModelOverrides = { ["grok-4.6"] = 400_000 },
        };
        SessionContextUsage.Compute(rows, "grok-4.6-build", overridden, contract: GrokLive)
            .CeilingTokens.ShouldBe(400_000);

        var claudeRows = new[] { Usage(1, input: 10_000, cacheRead: 0, cacheCreate: 0, output: 0, model: "claude-opus-4") };
        var claude = ProviderContractCatalog.For(AgentKind.ClaudeCode).ContextWindowUsage;
        SessionContextUsage.Compute(claudeRows, "claude-opus-4", overridden, contract: claude)
            .CeilingTokens.ShouldBe(200_000, "Claude is Configured; grok override must not leak");
    }

    [Test]
    public void A_manual_compact_at_under_80_percent_does_not_fire_the_auto_headroom_warning()
    {
        var sink = new List<string>();
        var rows = new[]
        {
            Usage(1, input: 10_000, cacheRead: 0, cacheCreate: 0, output: 0),
            Row(2, TranscriptKinds.CompactBoundary, "Context compacted (manual)"),
        };

        SessionContextUsage.Compute(rows, null, Defaults, new ListLogger(sink));

        sink.ShouldBeEmpty();
    }

    private static TranscriptContextRow Usage(
        long sequence, int input, int cacheRead, int cacheCreate, int output, string? model = "claude-opus-4")
        => new(
            sequence, TranscriptKinds.AssistantText, "ok",
            input, output, cacheRead, cacheCreate, model);

    private static TranscriptContextRow Row(
        long sequence, string kind, string? text, bool? isApiError = null)
        => new(sequence, kind, text, null, null, null, null, null, isApiError);

    private static TranscriptContextRow GrokTurn(
        long sequence,
        int input,
        int modelCalls,
        int cacheRead = 0,
        DateTime? timestamp = null,
        string? model = "grok-4.6-build")
        => new(
            sequence, TranscriptKinds.TurnEnd, "ok",
            input, OutputTokens: 0, CacheReadTokens: cacheRead, CacheCreationTokens: 0, model,
            ModelCalls: modelCalls, Timestamp: timestamp);

    private static TranscriptContextRow GrokBoundary(long sequence, int? input, DateTime? timestamp = null)
        => new(
            sequence, TranscriptKinds.CompactBoundary,
            input is int after
                ? $"Context compacted (auto): tokens x -> {after}"
                : "Context compacted (auto)",
            input, null, null, null, null,
            Timestamp: timestamp);

    private sealed class ListLogger(List<string> sink) : ILogger
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
            sink.Add($"[{logLevel}] {formatter(state, exception)}");
        }
    }
}
