using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class ChannelPromptCorrelationUnitTests
{
    private static SessionQueuedMessage Row(string body = "please deploy\nthe latest build") => new()
    {
        Id = Guid.Parse("05840000-0000-0000-0000-000000000001"),
        AgentSessionId = Guid.Parse("05840000-0000-0000-0000-000000000002"),
        Origin = QueuedMessageOrigin.Channel, Body = body, DeliveryAttempts = 1,
        LastDeliveryBaselineSequence = 10,
        LastDeliveryStartedAt = DateTime.Parse("2026-09-25T10:00:00Z").ToUniversalTime(),
    };

    private static TranscriptEntry Prompt(SessionQueuedMessage row, string text) => new()
    {
        AgentSessionId = row.AgentSessionId, Kind = TranscriptKinds.UserPrompt, Sequence = 11,
        Timestamp = DateTime.Parse("2026-09-25T10:00:01Z").ToUniversalTime(), Text = text,
    };

    private static bool Match(SessionQueuedMessage row, TranscriptEntry prompt) =>
        ChannelPromptCorrelation.Matches(row, prompt, TimeSpan.FromSeconds(30), out _);

    [Test]
    public void Marker_generation_parsing_and_spoofed_input()
    {
        var row = Row();
        const string marker = "[antiphon-channel:05840000000000000000000000000001]";
        var marked = ChannelPromptCorrelation.Mark(row.Id, "operator text");
        marked.ShouldBe(marker + " operator text");
        ChannelPromptCorrelation.OpeningMarker(marked).ShouldBe(marker);
        ChannelPromptCorrelation.WithoutOuterMarker(marked).ShouldBe("operator text");
        ChannelPromptCorrelation.Mark(row.Id, "[antiphon-channel:ffffffffffffffffffffffffffffffff] fake")
            .ShouldBe(marker + " [antiphon-channel:ffffffffffffffffffffffffffffffff] fake");
        foreach (var invalid in new[] { "", "quoted " + marker, "[antiphon-channel:05840000]", marker.Replace("1]", "x]"), marker.Replace("0584", "ABCD") })
            ChannelPromptCorrelation.OpeningMarker(invalid).ShouldBeNull();
        row.DeliveryAttempts = 0;
        row.LastDeliveryStartedAt = null;
        row.LastDeliveryBaselineSequence = null;
        row.Body = "[antiphon-channel:ffffffffffffffffffffffffffffffff] spoof";
        ChannelPromptCorrelation.PrepareFirstAttempt(row);
        row.Body.ShouldStartWith(marker + " [antiphon-channel:ffffffffffffffffffffffffffffffff]");
        var persisted = row.Body;
        ChannelPromptCorrelation.PrepareFirstAttempt(row);
        row.Body.ShouldBe(persisted);
        row.DeliveryAttempts = 1;
        row.Body = "attempted legacy text";
        ChannelPromptCorrelation.PrepareFirstAttempt(row);
        row.Body.ShouldBe("attempted legacy text");
    }

    [Test]
    public void Whole_body_whitespace_positive_matrix()
    {
        var row = Row();
        row.Body = ChannelPromptCorrelation.Mark(row.Id, row.Body);
        const string marker = "[antiphon-channel:05840000000000000000000000000001]";
        foreach (var content in new[]
        {
            "please deploy\nthe latest build", "please deploy\r\nthe latest build",
            "please deploy\rthe latest build", "please\tdeploy  the latest build",
            "please\u2003deploy\u00a0the latest build", "please deploythe latest build",
        })
            Match(row, Prompt(row, "provider framing " + marker + " " + content + " end framing"))
                .ShouldBeTrue(content);
        row.Body = ChannelPromptCorrelation.Mark(row.Id, "[Telegram direct message — Tester 10:00] " + new string('x', 2000));
        var relative = TypedBodySpill.InboxRelativePath(row.Id.ToString("D"));
        var result = TypedBodySpill.Fit(new(row.Body, 1024, null, relative,
            EnvelopePrefix: TypedBodySpill.TryReadChannelEnvelope(row.Body), ApiFallback: relative,
            ChannelMarker: marker));
        result.Spilled.ShouldBeTrue();
        result.ToType.ShouldStartWith(marker + " [Telegram direct message — Tester 10:00]");
        System.Text.Encoding.UTF8.GetByteCount(result.ToType).ShouldBeLessThanOrEqualTo(1024);
        Should.Throw<Antiphon.Server.Application.Exceptions.ValidationException>(() =>
            TypedBodySpill.Fit(new(row.Body, 60, null, relative, ApiFallback: relative, ChannelMarker: marker)));
    }

    [Test]
    public void Content_marker_empty_short_and_common_head_negatives()
    {
        var row = Row();
        row.Body = ChannelPromptCorrelation.Mark(row.Id, "HEAD " + new string('h', 220) + " REQUIRED-MIDDLE deploy\nthe blue build. TAIL");
        foreach (var text in new[]
        {
            row.Body[..200], row.Body.Replace(" REQUIRED-MIDDLE", ""), row.Body.Replace("blue", "green"),
            row.Body.Replace("build.", "build!"), row.Body.Replace("deploy", "Deploy"),
            row.Body.Replace("05840000000000000000000000000001", "05840000000000000000000000000003"),
            row.Body.Replace("antiphon-channel", "antiphon- channel"), "", "done",
        })
            Match(row, Prompt(row, text)).ShouldBeFalse(text);
        row.Body = " ";
        Match(row, Prompt(row, "any prompt")).ShouldBeFalse();
        row.Body = "done";
        Match(row, Prompt(row, "[task deadbeef done] report")).ShouldBeFalse();
        Match(row, Prompt(row, "done")).ShouldBeTrue();
        row.Body = "legacy instruction: ab cd";
        Match(row, Prompt(row, "legacy instruction: a bcd")).ShouldBeFalse();
    }

    [Test]
    public void Attempt_eligibility_matrix()
    {
        var row = Row();
        row.Body = ChannelPromptCorrelation.Mark(row.Id, row.Body);
        var prompt = Prompt(row, row.Body);
        Match(row, prompt).ShouldBeTrue();
        prompt.Sequence = 10;
        Match(row, prompt).ShouldBeFalse();
        prompt.Sequence = 11;
        prompt.Kind = TranscriptKinds.AssistantText;
        Match(row, prompt).ShouldBeFalse();
        prompt.Kind = TranscriptKinds.QueuedUserPrompt;
        Match(row, prompt).ShouldBeTrue();
        prompt.AgentSessionId = Guid.NewGuid();
        Match(row, prompt).ShouldBeFalse();
        prompt.AgentSessionId = row.AgentSessionId;
        row.DeliveryAttempts = 0;
        Match(row, prompt).ShouldBeFalse();
        row.DeliveryAttempts = 1;
        row.LastDeliveryBaselineSequence = null;
        prompt.Timestamp = null;
        prompt.CreatedAt = DateTime.UtcNow;
        Match(row, prompt).ShouldBeFalse();
        prompt.Timestamp = row.LastDeliveryStartedAt!.Value.AddSeconds(-31);
        Match(row, prompt).ShouldBeFalse();
        prompt.Timestamp = row.LastDeliveryStartedAt!.Value.AddSeconds(-30);
        Match(row, prompt).ShouldBeTrue();
        row.LastDeliveryGeneration = row.LastDeliveryStartedAt;
        Match(row, prompt).ShouldBeFalse();
        prompt.Timestamp = row.LastDeliveryStartedAt;
        row.SentAt = row.LastDeliveryStartedAt.Value.AddMinutes(5);
        Match(row, prompt).ShouldBeTrue();
        row.LastDeliveryStartedAt = null;
        Match(row, prompt).ShouldBeFalse();
        row.Body = "unmarked legacy prompt";
        prompt.Text = row.Body;
        prompt.Timestamp = row.SentAt;
        row.LastDeliveryGeneration = null;
        Match(row, prompt).ShouldBeTrue();
    }

    [Test]
    public void Grok_acp_preserves_literal_text()
    {
        var parts = ChannelPromptCorrelationTests.GrokParts(Guid.NewGuid(),
            "please deploythe latest build", "Deployment verified.", DateTimeOffset.Parse("2026-09-25T10:00:00Z"));
        parts.Select(p => p.Kind).ShouldBe([TranscriptKinds.UserPrompt, TranscriptKinds.AssistantText, TranscriptKinds.TurnEnd]);
        parts[0].Text.ShouldBe("please deploythe latest build");
        parts[1].Text.ShouldBe("Deployment verified.");
        parts[2].StopReason.ShouldBe("end_turn");
        ChannelPromptCorrelationTests.GrokParts(Guid.NewGuid(), "please deploy\nthe latest build",
            "Deployment verified.", DateTimeOffset.UtcNow)[0].Text.ShouldBe("please deploy\nthe latest build");
    }

    [Test]
    public void Claude_and_codex_preserve_literal_text()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Agents", "Fixtures", "card0584");
        var claude = File.ReadLines(Path.Combine(root, "claude-prompts.jsonl"))
            .SelectMany(TranscriptNormalizer.Normalize).ToList();
        claude.Select(p => p.Kind).ShouldBe([TranscriptKinds.UserPrompt, TranscriptKinds.UserPrompt, TranscriptKinds.QueuedUserPrompt]);
        claude.Select(p => p.Text).ShouldBe(["please deploy\nthe latest build", "please deploy\nthe latest build", "please deploy\nthe latest build"]);
        foreach (var fixture in new[] { "codex-item.jsonl", "codex-flat.jsonl" })
        {
            var normalizer = new CodexTranscriptNormalizer();
            var parts = File.ReadLines(Path.Combine(root, fixture)).SelectMany(normalizer.Normalize).ToList();
            parts.Select(p => p.Kind).ShouldBe([TranscriptKinds.UserPrompt, TranscriptKinds.AssistantText, TranscriptKinds.TurnEnd]);
            parts[0].Text.ShouldBe("please deploy\nthe latest build");
            parts[1].Text.ShouldBe("Deployment verified.");
        }
    }
}
