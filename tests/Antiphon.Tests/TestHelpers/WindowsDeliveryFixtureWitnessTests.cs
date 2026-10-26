using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.Application;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Unit")]
public sealed class WindowsDeliveryFixtureWitnessTests
{
    [Test]
    public void Native_fixture_requires_both_readable_composer_and_the_launched_generation()
    {
        var id = Guid.NewGuid();
        var original = new AgentLaunchSpec("fakeclaude", AgentKind.ClaudeCode, "fakeclaude.exe", [],
            new Dictionary<string, string> { ["ANTIPHON_FAKE_SWALLOW_ENTER"] = "9" }, @"C:\fixture", 120, 30);
        var spec = original.WithNativeClaudeDialog();
        spec.Env["ANTIPHON_FAKE_SWALLOW_ENTER"].ShouldBe("9");
        spec.Env["ANTIPHON_FAKE_CLAUDE_COMPOSER"].ShouldBe("1");
        original.Env.ContainsKey("ANTIPHON_FAKE_CLAUDE_COMPOSER").ShouldBeFalse();
        var generation = spec.AcceptedStartedAt.ShouldNotBeNull();
        var guard = new ScriptedClaudeInputGuard();
        guard.Bind(generation);
        var raw = guard.Observe(id, 1, "Fake Claude ready\r\nSWALLOWENTER:count=9\r\n");
        ProviderDialogObservationService.Refusal(raw, id, generation)
            .ShouldBe(ConditionalInputOutcomes.ObservationUnknown, "the old append-only fake withholds every submit");
        var readable = guard.Observe(id, 2, ClaudeDialogFixture.ComposerScreen(draft: "whole body"));
        ProviderDialogObservationService.Refusal(readable, id, generation.AddTicks(10))
            .ShouldBe(ConditionalInputOutcomes.ObservationUnknown, "a separately minted database generation still withholds input");
        ProviderDialogObservationService.Refusal(readable, id, generation).ShouldBeNull();
        spec.WithNativeClaudeDialog().AcceptedStartedAt.ShouldBe(generation, "an explicit launch generation survives configuration");
    }

    [Test]
    public async Task Receipt_faults_intercept_the_guarded_body_and_the_separate_Enter()
    {
        var inner = new FakeSessionRunnerClient();
        var forward = new SessionQueueReceiptPlumbingTests.ForwardingClient(inner);
        var id = Guid.NewGuid();
        var request = new RunnerConditionalInputRequest(DateTime.UnixEpoch, 1, "\u001b[200~line one\nline two\u001b[201~", true);
        (await forward.SendConditionalInputAsync(id, request, default)).Outcome.ShouldBe(ConditionalInputOutcomes.Written);
        forward.CutAtEnter = "before";
        await Should.ThrowAsync<InvalidOperationException>(() => forward.SendConditionalInputAsync(id, request with { Input = "\r" }, default));
        inner.ConditionalInputCalls.Count.ShouldBe(1, "before-Enter cut must precede the guarded backend write");
        forward.CutAtEnter = "after";
        await Should.ThrowAsync<InvalidOperationException>(() => forward.SendConditionalInputAsync(id, request with { Input = "\r" }, default));
        inner.ConditionalInputCalls.Select(c => c.Request.Input).ShouldBe([request.Input, "\r"]);
        forward.Payloads.ShouldBe([request.Input, "\r"]);
    }

    [Test]
    public void Posix_recipient_rules_receipt_uses_the_remote_grammar_on_a_Windows_host()
    {
        var id = Guid.NewGuid();
        var windowsCombined = "/runner/task\\instructions\\grok\\" + id.ToString("N") + "\\rules.md";
        Should.Throw<GrokRulesTransportException>(() => GrokRulesTransport.ValidateReceiptPath(windowsCombined, id))
            .Reason.ShouldBe("path");
        var path = CodexCliRemoteDeliveryFixture.RulesPath("/runner/task", id);
        path.ShouldBe("/runner/task/instructions/grok/" + id.ToString("N") + "/rules.md");
        GrokRulesTransport.ValidateReceiptPath(path, id);
    }
    [Test]
    public void Sign_in_screen_retains_its_anchors_after_terminal_rendering()
    {
        var paint = Antiphon.FakeGrok.SignInScreen.Render(@"C:\fixture\cwd");
        var legacy = new TerminalScreen(120, 30);
        legacy.Feed(paint.ReplaceLineEndings("\n"));
        var fixedScreen = new TerminalScreen(120, 30);
        fixedScreen.Feed(paint);
        Console.WriteLine("Legacy LF sign-in visible=" + GrokSignInPromptDetector.IsVisibleOnScreen(legacy.GetScreenText()));
        GrokStartupScreen.Classify(fixedScreen.GetScreenText()).Reason.ShouldBe(GrokStartupReason.SignIn);
    }

}
