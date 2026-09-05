using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0397: machine-turn follow-up matching is task-id + first-line header, not 120-char
/// body containment. Grok/PtyHost records <c>UserPrompt</c> with newlines joined out.
/// </summary>
[Category("Unit")]
public class ChannelMachineTurnMatchTests
{
    [Test]
    public void Flattened_task_done_fails_120_char_body_containment_and_passes_id_and_header()
    {
        var taskId = Guid.NewGuid();
        var shortId = DelegationReportFormatter.Short(taskId);
        var body = $"[task {shortId} done] git=landed\n\nWrote developer notes.";
        var flattened = Flatten(body);
        flattened.ShouldContain("git=landedWrote");
        flattened.ShouldNotContain("\n");

        LegacyPromptsMatch(Normalize(body), flattened).ShouldBeFalse(
            "today's 120-char probe includes the newline and misses the Grok-joined UserPrompt");
        ChannelContracts.HeaderProbe(body).ShouldBe($"[task {shortId} done] git=landed");
        flattened.ShouldContain(ChannelContracts.HeaderProbe(body));
        var ids = ChannelContracts.CollectInjectionShortIds(flattened);
        ids.TaskIds.ShouldContain(shortId);
        ids.CheckIds.ShouldBeEmpty();
        ChannelContracts.IsAntiphonInjectionPrompt(flattened).ShouldBeTrue();
    }

    [Test]
    public void Batched_two_task_headers_collect_both_ids()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var prompt =
            $"[task {DelegationReportFormatter.Short(a)} done] one\n\n"
            + $"[task {DelegationReportFormatter.Short(b)} done] two";
        var ids = ChannelContracts.CollectInjectionShortIds(prompt);
        ids.TaskIds.Count.ShouldBe(2);
        ids.TaskIds.ShouldContain(DelegationReportFormatter.Short(a));
        ids.TaskIds.ShouldContain(DelegationReportFormatter.Short(b));
    }

    [Test]
    public void Superseded_joined_check_header_is_injection_shaped_and_collects_the_check_id()
    {
        var taskId = Guid.NewGuid();
        var shortId = DelegationReportFormatter.Short(taskId);
        var joined =
            "SUPERSEDED — captured 2026-09-05T10:00:00Z, but this task SETTLED at "
            + $"2026-09-05T10:01:00Z, after[check {shortId} #1] still looping?";
        ChannelContracts.IsAntiphonInjectionPrompt(joined).ShouldBeTrue();
        ChannelContracts.CollectInjectionShortIds(joined).CheckIds.ShouldContain(shortId);
        ChannelContracts.CollectInjectionShortIds(joined).TaskIds.ShouldBeEmpty();
    }

    [Test]
    public void Scheduled_banner_is_injection_shaped_and_has_no_task_id()
    {
        var banner = "[scheduled: standup · daily · fire #1 · due 09:00, fired 09:00:01]\nstatus please";
        ChannelContracts.IsAntiphonInjectionPrompt(banner).ShouldBeTrue();
        ChannelContracts.CollectInjectionShortIds(banner).TaskIds.ShouldBeEmpty();
        ChannelContracts.CollectInjectionShortIds(banner).CheckIds.ShouldBeEmpty();
        ChannelContracts.HeaderProbe(banner).ShouldStartWith("[scheduled: standup");
    }

    [Test]
    public void Header_probe_is_the_first_line_never_the_report_body()
    {
        var taskId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var header = $"[task {DelegationReportFormatter.Short(taskId)} done] header";
        var body = header
            + "\n\n--- deliverable ---\nunique-body-token "
            + DelegationReportFormatter.Short(otherId);
        ChannelContracts.HeaderProbe(body).ShouldBe(header);
        ChannelContracts.HeaderProbe(body).ShouldNotContain("unique-body-token");
        ChannelContracts.HeaderProbe(body).ShouldNotContain("deliverable");

        var ids = ChannelContracts.CollectInjectionShortIds(
            $"[task {DelegationReportFormatter.Short(otherId)} done] something else");
        ids.TaskIds.ShouldNotContain(DelegationReportFormatter.Short(taskId));
        ids.TaskIds.ShouldContain(DelegationReportFormatter.Short(otherId));
    }

    [Test]
    public void Check_id_via_conversation_key_matches_the_header_short_id()
    {
        var taskId = Guid.NewGuid();
        var key = AgentTaskCheckService.ConversationKey(taskId);
        AgentTaskCheckService.TryParseCheckConversationKey(key, out var parsed).ShouldBeTrue();
        DelegationReportFormatter.Short(parsed).ShouldBe(DelegationReportFormatter.Short(taskId));
        var ids = ChannelContracts.CollectInjectionShortIds(
            $"[check {DelegationReportFormatter.Short(taskId)} #1] still looping?");
        ids.CheckIds.ShouldContain(DelegationReportFormatter.Short(parsed));
        ids.TaskIds.ShouldBeEmpty();
    }

    [Test]
    public void Antiphon_task_marker_is_injection_shaped_but_not_a_source_task_id()
    {
        var marker = DelegationReportFormatter.TaskMarker(Guid.NewGuid());
        ChannelContracts.IsAntiphonInjectionPrompt(marker).ShouldBeTrue();
        ChannelContracts.CollectInjectionShortIds(marker).TaskIds.ShouldBeEmpty();
        ChannelContracts.CollectInjectionShortIds(marker).CheckIds.ShouldBeEmpty();
    }

    [Test]
    public void Channel_envelope_and_operator_prose_are_not_injection_shaped()
    {
        ChannelContracts.IsAntiphonInjectionPrompt(
            "[Telegram \"Family\" — Mike 14:32] send me the PDF").ShouldBeFalse();
        ChannelContracts.IsAntiphonInjectionPrompt("run the tests please").ShouldBeFalse();
        ChannelContracts.IsAntiphonInjectionPrompt("done").ShouldBeFalse();
        ChannelContracts.IsAntiphonInjectionPrompt(null).ShouldBeFalse();
        ChannelContracts.IsAntiphonInjectionPrompt("").ShouldBeFalse();
    }

    [Test]
    public void System_and_session_tokens_are_injection_shaped()
    {
        ChannelContracts.IsAntiphonInjectionPrompt(ChannelPreamble.RestartResumeBody).ShouldBeTrue();
        ChannelContracts.IsAntiphonInjectionPrompt(
            ChannelPreamble.WithSessionTag(ChannelPreamble.BootstrapBody, Guid.NewGuid()))
            .ShouldBeTrue();
    }

    [Test]
    public void Header_probe_skips_leading_blank_lines_and_empty_body()
    {
        ChannelContracts.HeaderProbe("\n\n[check ab12cd34 #1] still looping?\n\nbody")
            .ShouldBe("[check ab12cd34 #1] still looping?");
        ChannelContracts.HeaderProbe(null).ShouldBe("");
        ChannelContracts.HeaderProbe("").ShouldBe("");
        ChannelContracts.HeaderProbe("\n\n").ShouldBe("");
    }

    private static string Flatten(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal);

    private static string Normalize(string s) => s.ReplaceLineEndings("\n").Trim();

    private static bool LegacyPromptsMatch(string pending, string turn)
    {
        var probe = pending.Length <= 120 ? pending : pending[..120];
        return turn.Contains(probe, StringComparison.Ordinal);
    }
}
