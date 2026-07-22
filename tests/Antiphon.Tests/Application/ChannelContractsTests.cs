using System.Reflection;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// Golden tests for the frozen channel contract strings (PR 0 of the Telegram bot agents plan):
/// the envelope grammar, batch markers, preamble preset, note bodies, and NO_REPLY semantics.
/// Everything downstream (bridge, queue batching, recovery notes, fakeclaude, docs) cites these
/// shapes — a change here is a deliberate contract change, reviewed as such.
/// </summary>
public class ChannelContractsTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static ChatChannel GroupChannel => new()
    {
        Provider = "telegram",
        ExternalId = "-100123",
        Kind = ChatChannelKind.Group,
        Title = "Family",
    };

    private static ChatChannel DirectChannel => new()
    {
        Provider = "telegram",
        ExternalId = "555",
        Kind = ChatChannelKind.Direct,
    };

    [Test]
    public void Envelope_contains_title_author_username_and_local_time()
    {
        var when = new DateTimeOffset(2026, 7, 21, 14, 32, 0, TimeSpan.Zero);
        var line = ChannelPromptFormat.Format(GroupChannel, "Mike", "mciechan", when, " hello there ", Utc);
        line.ShouldBe("[Telegram \"Family\" — Mike (@mciechan) 14:32] hello there");
    }

    [Test]
    public void Direct_message_envelope_omits_title()
    {
        var when = new DateTimeOffset(2026, 7, 21, 9, 5, 0, TimeSpan.Zero);
        var line = ChannelPromptFormat.Format(DirectChannel, "Mike", null, when, "hi", Utc);
        line.ShouldBe("[Telegram direct message — Mike 09:05] hi");
    }

    [Test]
    public void Envelope_renders_local_time_in_the_given_zone()
    {
        var when = new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);
        var plusTwo = TimeZoneInfo.CreateCustomTimeZone("test+2", TimeSpan.FromHours(2), "t", "t");
        var line = ChannelPromptFormat.Format(DirectChannel, "Mike", null, when, "hi", plusTwo);
        line.ShouldContain("14:00]");
    }

    [Test]
    public void Batch_format_places_all_but_newest_under_context_marker()
    {
        var batch = ChannelPromptFormat.FormatBatch(["[T] first", "[T] second"], "[T] third");

        batch.ShouldBe(
            ChannelPromptFormat.BatchContextMarker + "\n"
            + "[T] first\n[T] second\n\n"
            + ChannelPromptFormat.BatchCurrentMarker + "\n"
            + "[T] third");
    }

    [Test]
    public void Batch_of_one_uses_plain_envelope()
    {
        ChannelPromptFormat.FormatBatch([], "[T] only").ShouldBe("[T] only");
    }

    [Test]
    public void Preset_contains_envelope_reply_contract_no_reply_and_compaction_note()
    {
        var preset = ChannelPreamble.TelegramPresetTemplate;

        preset.ShouldContain(ChannelPreamble.AgentNamePlaceholder);
        preset.ShouldContain(ChannelPreamble.ChannelsPlaceholder);
        preset.ShouldContain("[Telegram \"Family\" — Mike (@mike) 14:32]"); // envelope example
        preset.ShouldContain(ChannelPromptFormat.BatchContextMarker);
        preset.ShouldContain(ChannelPromptFormat.BatchCurrentMarker);
        preset.ShouldContain("untrusted");
        preset.ShouldContain("4000 characters");
        preset.ShouldContain(ChannelContracts.NoReplyToken);
        preset.ShouldContain("compaction");
    }

    [Test]
    public void Preamble_placeholders_render_agent_name_and_bound_channel_titles()
    {
        var rendered = ChannelPreamble.Render(
            ChannelPreamble.TelegramPresetTemplate,
            "Mikey",
            [("telegram", "Family"), ("telegram", "Ops")]);

        rendered.ShouldContain("You are Mikey,");
        rendered.ShouldContain("Bound channels: telegram \"Family\", telegram \"Ops\"");
        rendered.ShouldNotContain(ChannelPreamble.AgentNamePlaceholder);
        rendered.ShouldNotContain(ChannelPreamble.ChannelsPlaceholder);
    }

    [Test]
    public void Preamble_with_no_bound_channels_says_none_yet()
    {
        ChannelPreamble.Render("{channels}", "X", []).ShouldBe("none yet");
    }

    [Test]
    public void Note_bodies_reference_workspace_files_and_no_reply()
    {
        ChannelPreamble.BootstrapBody.ShouldContain("CLAUDE.md");
        ChannelPreamble.BootstrapBody.ShouldContain("READY");

        ChannelPreamble.RestartResumeBody.ShouldContain("resumed after a restart");
        ChannelPreamble.RestartResumeBody.ShouldContain(ChannelContracts.NoReplyToken);

        ChannelPreamble.RecoveryNoteBody.ShouldContain("compacted");
        ChannelPreamble.RecoveryNoteBody.ShouldContain("MEMORY.md");
        ChannelPreamble.RecoveryNoteBody.ShouldContain(ChannelContracts.NoReplyToken);
    }

    [Test]
    public void IsNoReply_matches_whole_turn_only()
    {
        ChannelContracts.IsNoReply("NO_REPLY").ShouldBeTrue();
        ChannelContracts.IsNoReply("no_reply").ShouldBeTrue();
        ChannelContracts.IsNoReply("  NO_REPLY \n").ShouldBeTrue();

        ChannelContracts.IsNoReply("Sure — NO_REPLY").ShouldBeFalse();
        ChannelContracts.IsNoReply("NO_REPLY, as instructed").ShouldBeFalse();
        ChannelContracts.IsNoReply("I'll reply NO_REPLY when idle").ShouldBeFalse();
        ChannelContracts.IsNoReply(null).ShouldBeFalse();
        ChannelContracts.IsNoReply("").ShouldBeFalse();
    }

    // The raiseAlert parameter must default to true so every existing incident path still alerts.
    // (Behavioral coverage comes from the existing supervision suites; this pins the signature.)
    [Test]
    public void Record_incident_raise_alert_parameter_defaults_to_true()
    {
        var method = typeof(AgentSupervisorService).GetMethod("RecordIncidentAsync", BindingFlags.Public | BindingFlags.Instance);
        method.ShouldNotBeNull();
        var raiseAlert = method!.GetParameters().Single(p => p.Name == "raiseAlert");
        raiseAlert.HasDefaultValue.ShouldBeTrue();
        raiseAlert.DefaultValue.ShouldBe(true);
    }
}
