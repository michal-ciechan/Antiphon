using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Pty;

namespace Antiphon.Tests.Agents;

[NotInParallel("Headed")]
[Category("Headed")]
public class ClaudeAdapterIntegrationTests
{
    [Test]
    public async Task Full_round_trip_via_seam_returns_response_text()
    {
        HeadedClaudeGate.SkipIfNotEligible();
        var clOrClaude = HeadedClaudeGate.ResolveOrThrow();
        var (app, args) = HeadedClaudeGate.BuildLaunch(clOrClaude);

        var options = Options.Create(new AgentRegistrySettings
        {
            DefaultDefinition = "claude",
            Definitions = { ["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = app } },
            ClaudeReadyQuietPeriodMs = 5_000,
            ClaudeReadyMaxWaitMs = 60_000,
            ClaudeDoneMaxWaitMs = 180_000,
        });

        await using var adapter = new ClaudeAdapter(options);
        var spec = new AgentLaunchSpec(
            DefinitionName: "claude",
            Kind: AgentKind.ClaudeCode,
            Exe: app,
            Args: args,
            Env: new Dictionary<string, string>(),
            Cwd: Path.GetTempPath(),
            Cols: 120,
            Rows: 30);

        await adapter.StartAsync(spec, CancellationToken.None);

        var ready = await adapter.WaitForReadyAsync(CancellationToken.None);
        ready.ShouldBeTrue();

        await adapter.SendPromptAsync("say PONG", CancellationToken.None);
        var result = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);

        result.TurnCompleted.ShouldBeTrue();
        result.ResponseText.ShouldNotBeNull();
        result.ResponseText!.ToLowerInvariant().ShouldContain("pong");
        result.IsAskingQuestion.ShouldBeFalse();

        var killed = await adapter.KillAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        killed.ShouldBeTrue();
    }
}
