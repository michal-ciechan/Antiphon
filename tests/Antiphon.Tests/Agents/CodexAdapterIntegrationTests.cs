using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Pty;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.TestHelpers;

namespace Antiphon.Tests.Agents;

[NotInParallel("Headed")]
[Category("Headed")]
[ParallelLimiter<ProcessSpawnLimit>]
public class CodexAdapterIntegrationTests
{
    /// <summary>
    /// <b>KNOWN RED against the real CLI as of 2026-08-20 — CARD-0099 S3's to fix, recorded here so
    /// nobody re-derives it.</b> This test skipped silently for its whole life because
    /// <see cref="HeadedCodexGate"/> looked for a <c>cx.ps1</c> that exists nowhere on this machine.
    /// S2 repointed the gate at the npm shim, so it runs now — and fails:
    ///
    /// <code>
    /// result.ResponseText should contain "pong" but was "gpt-5.6-luna low · ~\appdata\local\temp"
    /// </code>
    ///
    /// <para>It returns in ~5 s, i.e. <c>WaitForTurnCompleteAsync</c>'s quiet-period detector fired
    /// before the model had answered, and <c>CodexResponseAnalyzer.ExtractResponse</c> then scraped
    /// the STATUS BAR as the response. Both halves are the screen-scraping turn detection
    /// <c>ProviderContractCatalog.Codex</c> declares — the axis S1's rollout tailer replaces with
    /// structured <c>task_complete</c> rows. Left failing rather than quarantined: the failure is
    /// real, it only runs under <c>ANTIPHON_CODEX_HEADED_TESTS=1</c>, and it is the first evidence
    /// anyone has had that this adapter path does not work against the live CLI.</para>
    ///
    /// <para>The model was moved off <c>gpt-5.4-mini</c> at the same time: that model is deprecated,
    /// and launching on it parks the TUI on a "GPT-5.4 Mini will be deprecated soon" modal that
    /// swallows input exactly as the trust dialog does.</para>
    /// </summary>
    [Test]
    public async Task Full_round_trip_via_cx_returns_response_text_and_can_be_stopped()
    {
        HeadedCodexGate.SkipIfNotEligible();
        var cx = HeadedCodexGate.ResolveOrThrow();
        var (app, args) = HeadedCodexGate.BuildLaunch(
            cx,
            "-m", "gpt-5.6-luna",
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
