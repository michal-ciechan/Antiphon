using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0212 — catalog capability → refuse / ignore decision.</summary>
[Category("Unit")]
public class RemoteControlPolicyTests
{
    [Test]
    public void Permits_is_true_only_for_kinds_whose_catalog_row_is_Supported()
    {
        var catalog = new AgentTuiRunnerCatalog();
        var permitted = Enum.GetValues<AgentKind>().Where(RemoteControlPolicy.Permits).ToArray();
        var supported = Enum.GetValues<AgentKind>().Where(catalog.SupportsRemoteControl).ToArray();
        permitted.ShouldBe(supported);
        permitted.ShouldBe([AgentKind.ClaudeCode]);
    }

    [Test]
    public void Require_does_not_throw_when_not_wanted()
    {
        Should.NotThrow(() => RemoteControlPolicy.Require(AgentKind.Grok, wanted: false, "agent 'X'"));
        Should.NotThrow(() => RemoteControlPolicy.Require(AgentKind.ClaudeCode, wanted: false, "agent 'X'"));
    }

    [Test]
    public void Require_Grok_true_throws_remote_control_refused()
    {
        var ex = Should.Throw<ConflictException>(() =>
            RemoteControlPolicy.Require(AgentKind.Grok, wanted: true, "agent 'X'"));
        ex.Code.ShouldBe("remote_control_refused");
        ex.Message.ShouldContain("Grok");
        ex.Message.ShouldContain("remoteControlEnabled: false");
    }

    [Test]
    public void Require_Codex_true_throws_remote_control_refused()
    {
        var ex = Should.Throw<ConflictException>(() =>
            RemoteControlPolicy.Require(AgentKind.Codex, wanted: true, "agent 'X'"));
        ex.Code.ShouldBe("remote_control_refused");
        ex.Message.ShouldContain("Codex");
        ex.Message.ShouldContain("remoteControlEnabled: false");
    }

    [Test]
    public void Permits_unknown_kind_is_false_not_an_exception()
    {
        RemoteControlPolicy.Permits((AgentKind)99).ShouldBeFalse();
    }
}
