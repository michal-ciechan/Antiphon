using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Pty;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

[NotInParallel("Headed")]
[Category("Headed")]
public class CodexAdapterIntegrationTests
{
    [Test]
    public async Task Full_round_trip_via_cx_returns_response_text_and_can_be_stopped()
    {
        HeadedCodexGate.SkipIfNotEligible();
        var cx = HeadedCodexGate.ResolveOrThrow();
        var (app, args) = HeadedCodexGate.BuildLaunch(
            cx,
            "-m", "gpt-5.4-mini",
            "-c", "model_reasoning_effort=\"low\"",
            "--no-alt-screen");

        var options = Options.Create(new AgentRegistrySettings
        {
            DefaultDefinition = "codex",
            Definitions = { ["codex"] = new AgentDefinition { Kind = "Codex", Exe = app } },
            CodexReadyQuietPeriodMs = 1_000,
            CodexReadyMaxWaitMs = 60_000,
            CodexDoneQuietPeriodMs = 3_000,
            CodexDoneMaxWaitMs = 300_000,
        });

        await using var adapter = new CodexAdapter(options);
        var spec = new AgentLaunchSpec(
            DefinitionName: "codex",
            Kind: AgentKind.Codex,
            Exe: app,
            Args: args,
            Env: new Dictionary<string, string>(),
            Cwd: Path.GetTempPath(),
            Cols: 120,
            Rows: 30);

        await adapter.StartAsync(spec, CancellationToken.None);

        var ready = await adapter.WaitForReadyAsync(CancellationToken.None);
        ready.ShouldBeTrue();

        await adapter.SendPromptAsync("Reply with exactly PONG and no other text.", CancellationToken.None);
        var result = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);

        result.TurnCompleted.ShouldBeTrue();
        result.ResponseText.ShouldNotBeNull();
        result.ResponseText!.ToLowerInvariant().ShouldContain("pong");
        result.IsAskingQuestion.ShouldBeFalse();

        var killed = await adapter.KillAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        killed.ShouldBeTrue();
    }
}
