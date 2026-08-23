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
    public void Grok_contract_suppresses_fullness_and_keeps_the_raw_sum()
    {
        var rows = new[] { Usage(1, input: 18_700_000, cacheRead: 0, cacheCreate: 0, output: 0) };
        var grok = ProviderContractCatalog.For(AgentKind.Grok).ContextWindowUsage;

        var result = SessionContextUsage.Compute(rows, "grok-code", Defaults, contract: grok);

        result.Fullness.ShouldBeNull();
        result.TokensUsed.ShouldBe(18_700_000);
        result.CeilingTokens.ShouldBe(200_000);
        grok.State.ShouldBe(AgentTuiCapabilityState.Degraded);
        grok.CeilingSource.ShouldBe(ContextWindowCeilingSource.SelfReported);
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
    public void Fullness_suppression_is_kind_keyed_not_value_keyed()
    {
        var rows = new[] { Usage(1, input: 18_700_000, cacheRead: 0, cacheCreate: 0, output: 0) };
        foreach (var kind in Enum.GetValues<AgentKind>())
        {
            var contract = ProviderContractCatalog.For(kind).ContextWindowUsage;
            var result = SessionContextUsage.Compute(rows, "model", Defaults, contract: contract);
            var suppressed = contract.State == AgentTuiCapabilityState.Degraded
                && contract.CeilingSource == ContextWindowCeilingSource.SelfReported;
            if (suppressed)
                result.Fullness.ShouldBeNull($"{kind} is Degraded/SelfReported — no badge");
            else
                result.Fullness.ShouldNotBeNull($"{kind} must not inherit Grok's suppression");
        }
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
