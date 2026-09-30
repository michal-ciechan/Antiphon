using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>Synthetic raw task_complete records reconstructed from the normalized CARD-0719 tails.</summary>
public class CodexQuotaEvidenceTests
{
    private const string Literal = "You’ve hit your usage limit. Visit https://chatgpt.com/codex/settings/usage to purchase more credits or try again at Sep 26th, 2026 11:16 AM.";

    private static string Error(string message, string cls = "usage_limit_exceeded") =>
        JsonSerializer.Serialize(new
        {
            timestamp = "2026-09-25T16:34:07.146Z",
            type = "event_msg",
            payload = new { type = "task_complete", turn_id = "quota-turn", error = new { message, codex_error_info = cls } },
        });

    [Test]
    public void Literal_refusal_is_error_turn_end()
    {
        var part = new CodexTranscriptNormalizer("Etc/UTC").Normalize(Error(Literal)).ShouldHaveSingleItem();
        part.Kind.ShouldBe(TranscriptKinds.TurnEnd);
        part.IsApiError.ShouldBe(true);
        part.ApiErrorClass.ShouldBe("usage_limit_exceeded");
        part.Text.ShouldBe(Literal);
    }

    [Test]
    public void Compact_refusal_preserves_prefix_and_class()
    {
        var text = "Error running remote compact task: " + Literal;
        var part = new CodexTranscriptNormalizer("Europe/London").Normalize(Error(text)).ShouldHaveSingleItem();
        part.Text.ShouldBe(text);
        part.ApiErrorClass.ShouldBe("usage_limit_exceeded");
    }

    [Test]
    public void Json_wrapped_diagnostic_preserves_human_message()
    {
        var wrapped = JsonSerializer.Serialize(new { type = "error", error = new { type = "usage_limit_exceeded", message = Literal } });
        var part = new CodexTranscriptNormalizer().Normalize(Error(wrapped, "other")).ShouldHaveSingleItem();
        part.Text.ShouldBe(Literal);
        part.ApiErrorClass.ShouldBe("usage_limit_exceeded");
    }

    [Test]
    public void Utc_zone_is_carried_only_on_error()
    {
        var part = new CodexTranscriptNormalizer("Etc/UTC").Normalize(Error(Literal)).ShouldHaveSingleItem();
        part.ApiErrorTimeZoneId.ShouldBe("Etc/UTC");
        part.ApiErrorStatus.ShouldBeNull();
    }

    [Test]
    public void London_zone_is_carried()
    {
        new CodexTranscriptNormalizer("Europe/London").Normalize(Error(Literal))
            .ShouldHaveSingleItem().ApiErrorTimeZoneId.ShouldBe("Europe/London");
    }

    [Test]
    public void Windows_zone_id_is_carried_without_translation()
    {
        new CodexTranscriptNormalizer("GMT Standard Time").Normalize(Error(Literal))
            .ShouldHaveSingleItem().ApiErrorTimeZoneId.ShouldBe("GMT Standard Time");
    }

    [Test]
    public void Ordinary_assistant_text_cannot_become_quota_evidence()
    {
        var line = JsonSerializer.Serialize(new { type = "event_msg", payload = new { type = "agent_message", message = Literal } });
        var part = new CodexTranscriptNormalizer("Etc/UTC").Normalize(line).ShouldHaveSingleItem();
        part.Kind.ShouldBe(TranscriptKinds.AssistantText);
        part.IsApiError.ShouldBeNull();
        part.ApiErrorTimeZoneId.ShouldBeNull();
    }

    [Test]
    public void Re_tail_identity_and_diagnostic_bound_are_stable()
    {
        var line = Error(Literal + new string('x', 700));
        var first = new CodexTranscriptNormalizer("Etc/UTC").Normalize(line).ShouldHaveSingleItem();
        var second = new CodexTranscriptNormalizer("Etc/UTC").Normalize(line).ShouldHaveSingleItem();
        first.Uuid.ShouldBe(second.Uuid);
        first.Text.ShouldBe(second.Text);
        first.Text!.Length.ShouldBeLessThanOrEqualTo(600);
        first.ApiErrorTimeZoneId.ShouldBe(second.ApiErrorTimeZoneId);
    }
}
