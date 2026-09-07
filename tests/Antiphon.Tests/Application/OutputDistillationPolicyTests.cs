using System.Text;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class OutputDistillationPolicyTests
{
    [Test]
    public void Defaults_separate_usefulness_from_transport()
    {
        var s = new DelegationSettings();
        s.DistillMinChars.ShouldBe(4000); s.DistillMaxRawChars.ShouldBe(20000);
        s.OutputDistillerMode.ShouldBe(OutputDistillerMode.Shadow); s.OutputDistillerWaitSeconds.ShouldBe(45);
        s.DistilledMaxChars.ShouldBe(1500); s.DistilledMaxRatio.ShouldBe(0.6);
        var inbox = s.CeilingsFor(PtyBackend.InboxConhost, "test");
        var modern = s.CeilingsFor(PtyBackend.ModernConPty, "test");
        inbox.ReplyInlineMaxChars.ShouldBe(3000); modern.ReplyInlineMaxChars.ShouldBe(14400);
        inbox.SingleWriteMaxBytes.ShouldBe(1024); modern.SingleWriteMaxBytes.ShouldBe(86400);
        inbox.BriefInlineMaxBytes.ShouldBe(900); modern.BriefInlineMaxBytes.ShouldBe(43200);
    }
    [Test]
    [Arguments(2999)] [Arguments(3000)] [Arguments(3001)] [Arguments(3500)]
    [Arguments(3999)] [Arguments(4000)] [Arguments(4001)] [Arguments(5500)]
    [Arguments(14399)] [Arguments(14400)] [Arguments(14401)] [Arguments(19999)]
    [Arguments(20000)] [Arguments(20001)]
    public void Report_boundaries(int length)
    {
        var s = new DelegationSettings();
        var task = Target(new string('x', length));
        foreach (var backend in new[] { PtyBackend.InboxConhost, PtyBackend.ModernConPty })
        {
            var ceiling = s.CeilingsFor(backend, "test").ReplyInlineMaxChars;
            AgentReportPolicy.ShouldStore(task, s).ShouldBe(length >= 4000);
            DelegationReportFormatter.FitReport(task.Result!, task, s, ceiling).Excerpted.ShouldBe(length > ceiling);
        }
    }
    [Test]
    [Arguments(3999)] [Arguments(4000)]
    public void Unicode_length_is_not_utf8_bytes_or_tokens(int length)
    {
        var text = new string('é', length - 2) + "🙂";
        text.Length.ShouldBe(length); Encoding.UTF8.GetByteCount(text).ShouldBeGreaterThan(length);
        AgentReportPolicy.ShouldStore(Target(text), new()).ShouldBe(length == 4000);
    }
    [Test]
    [Arguments(AgentTaskStatus.Queued)] [Arguments(AgentTaskStatus.Dispatched)]
    [Arguments(AgentTaskStatus.Working)] [Arguments(AgentTaskStatus.Blocked)]
    public void Nonterminal_tasks_are_ineligible(AgentTaskStatus status)
    {
        var task = Target(new string('x', 5500)); task.Status = status;
        AgentReportPolicy.ShouldStore(task, new()).ShouldBeFalse();
        OutputDistillationService.ShouldRequest(task, new() { OutputDistillerEnabled = true }).ShouldBeFalse();
    }
    internal static AgentTask Target(string raw) => new() { Id = Guid.NewGuid(), Result = raw,
        ReplyTo = AgentTaskReplyTo.Session, Role = AgentTaskRole.Code, Status = AgentTaskStatus.Succeeded };

    [Test]
    [Arguments(AgentTaskRole.Check)] [Arguments(AgentTaskRole.Distill)] [Arguments(AgentTaskRole.Diagnose)]
    public void Specialists_are_never_targets(AgentTaskRole role)
    {
        var task = Target(new string('x', 5500)); task.Role = role;
        AgentReportPolicy.ShouldStore(task, new()).ShouldBeFalse();
        OutputDistillationService.ShouldRequest(task, new() { OutputDistillerEnabled = true }).ShouldBeFalse();
        task.Role = AgentTaskRole.Review; task.ReplyTo = AgentTaskReplyTo.None;
        AgentReportPolicy.ShouldStore(task, new()).ShouldBeFalse();
    }
}
