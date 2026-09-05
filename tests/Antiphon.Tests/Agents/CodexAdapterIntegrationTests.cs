using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.TestHelpers;

namespace Antiphon.Tests.Agents;

[NotInParallel("Headed")]
[Category("Headed")]
[Category("Card0108")]
[ParallelLimiter<ProcessSpawnLimit>]
[Category("Integration")]
public class CodexAdapterIntegrationTests
{
    /// <summary>
    /// The first observed full <c>-Kind Codex</c> round trip: launch a real codex-cli, submit a
    /// prompt, and read the model's ACTUAL answer back out. CARD-0108 S3.
    ///
    /// <para><b>What this used to be, and why it failed.</b> It drove the in-process
    /// <c>CodexAdapter</c> — an adapter production never constructs — and returned
    /// <c>"gpt-5.6-luna low · &lt;cwd&gt;"</c>, the Codex TUI's own STATUS BAR, as
    /// <c>ResponseText</c> in about 5 s. That was CARD-0108, two stacked defects and not one:
    /// the production submit path (body, 20 ms, a separate CR) stranded the prompt in the composer
    /// — measured 6 times out of 6, the CR folding inside Codex's paste-detection window — so no
    /// turn ever ran and no rollout file was ever created; then the 3 s quiet done-detector
    /// certified that silent non-turn as a completed turn and the response analyzer scraped what
    /// was on screen. (The comment here previously blamed CARD-0099 S3; it was CARD-0108's, and
    /// S1-S3 of that card are what this test now exercises.)</para>
    ///
    /// <para><b>What it is now.</b> The production <see cref="RunnerCodexAdapter"/> over an
    /// in-process <see cref="DirectSessionRunnerClient"/> with Codex transcript tailing opted in —
    /// so both halves of the fix are under test: <c>SendPromptAsync</c> must confirm the submit
    /// against a real <c>UserPrompt</c> rollout row (pressing Enter again, never re-typing, when
    /// the first CR folds), and <c>WaitForTurnCompleteAsync</c> must take its verdict and its reply
    /// text from the real <c>task_complete</c>/<c>AgentMessage</c> rows. The assertion is unchanged:
    /// <c>ResponseText</c> contains "pong".</para>
    ///
    /// <para>The cwd is a UNIQUE temp directory per run, not the shared <c>Path.GetTempPath()</c>
    /// this used before: the Codex tailer discovers its rollout and proves ownership by
    /// <c>session_meta.cwd</c> (CARD-0006 rule C2), and a cwd shared with every other Codex session
    /// on the machine makes that evidence worthless. A fresh directory also means a first-launch
    /// trust dialog, which the adapter's own <c>AcceptTrustPromptIfVisibleAsync</c> answers.</para>
    ///
    /// <para>Headed and opt-in (<c>ANTIPHON_CODEX_HEADED_TESTS=1</c>): it spends a real model
    /// turn.</para>
    /// </summary>
    [Test]
    public async Task Full_round_trip_via_the_production_runner_adapter_returns_the_models_answer()
    {
        HeadedCodexGate.SkipIfRealServiceNotEligible();
        var cx = HeadedCodexGate.ResolveOrThrow();
        var (app, args) = HeadedCodexGate.BuildLaunch(
            cx,
            "-m", "gpt-5.6-luna",
            "-c", "model_reasoning_effort=\"low\"",
            "--no-alt-screen",
            "--dangerously-bypass-approvals-and-sandbox");

        var options = Options.Create(new AgentRegistrySettings
        {
            DefaultDefinition = "codex",
            Definitions = { ["codex"] = new AgentDefinition { Kind = "Codex", Exe = app } },
            CodexReadyQuietPeriodMs = 1_000,
            CodexReadyMaxWaitMs = 60_000,
            CodexDoneQuietPeriodMs = 3_000,
            // Bounded well below the 5-minute production ceiling: if the round trip is broken again
            // this must fail in a couple of minutes, not park a headed run for five.
            CodexDoneMaxWaitMs = 120_000,
        });

        var cwd = Directory.CreateTempSubdirectory("antiphon-codex-roundtrip").FullName;
        var sessionLogPath = Directory.CreateTempSubdirectory("antiphon-codex-runner").FullName;
        // modern, because that is what this deployment runs (ADR 0002) and the inbox conhost is a
        // different pty with different measured behaviour.
        await using var client = new DirectSessionRunnerClient(
            sessionLogPath, ptyBackend: "modern", codexTranscript: true);
        var adapter = new RunnerCodexAdapter(client, options);

        try
        {
            var sessionId = Guid.NewGuid();
            var spec = new AgentLaunchSpec(
                DefinitionName: "codex",
                Kind: AgentKind.Codex,
                Exe: app,
                Args: args,
                Env: HeadedCodexGate.RealServiceEnv(),
                Cwd: cwd,
                Cols: 120,
                Rows: 30,
                SessionId: sessionId);

            await adapter.StartAsync(spec, CancellationToken.None);

            var ready = await adapter.WaitForReadyAsync(CancellationToken.None);
            ready.ShouldBeTrue();

            await adapter.SendPromptAsync(
                "Reply with exactly PONG and no other text.", CancellationToken.None);
            var result = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);

            // The evidence CARD-0108 asks for, kept rather than thrown away: a passing run prints
            // nothing, and "somebody once saw a real Codex round trip work" is the whole point of
            // this test.
            WriteRoundTripLog(result);

            result.TurnCompleted.ShouldBeTrue(
                "no task_complete row and no Working-indicator lifecycle was ever observed. Screen:\n"
                + adapter.SnapshotRenderedScreen());
            result.ResponseText.ShouldNotBeNull();
            result.ResponseText!.ToLowerInvariant().ShouldContain(
                "pong",
                Case.Sensitive,
                $"ResponseText must be the MODEL's answer, not the status bar. Got: {result.ResponseText}");
            result.IsAskingQuestion.ShouldBeFalse();

            // Live schema-drift canary: a fixture cannot detect a codex-cli change that drops the
            // final_answer phase or its turn_id. The generic report gate needs the normalized
            // final text to carry the same identity as task_complete.
            var transcript = await client.GetTranscriptAsync(sessionId, CancellationToken.None);
            var end = transcript.Entries.Last(e => e.Kind == TranscriptKinds.TurnEnd);
            var finalTexts = transcript.Entries
                .Where(e => e.Kind == TranscriptKinds.AssistantText && e.ApiCallId == end.ApiCallId)
                .ToArray();
            end.ApiCallId.ShouldNotBeNullOrWhiteSpace();
            finalTexts.ShouldNotBeEmpty("the turn-ending Codex response must retain task_complete's identity");
            finalTexts.ShouldContain(e => e.Text!.Contains("PONG", StringComparison.OrdinalIgnoreCase));

            var killed = await adapter.KillAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            killed.ShouldBeTrue();
        }
        finally
        {
            await adapter.DisposeAsync();
            TryDelete(cwd);
            TryDelete(sessionLogPath);
        }
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, true); } catch { /* best effort */ }
    }

    private static void WriteRoundTripLog(AgentTurnResult result)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "TestOutput", "CodexCanary");
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, nameof(CodexAdapterIntegrationTests) + ".log"),
            $"# {DateTime.UtcNow:O}{Environment.NewLine}"
            + $"TurnCompleted={result.TurnCompleted}{Environment.NewLine}"
            + $"IsAskingQuestion={result.IsAskingQuestion}{Environment.NewLine}"
            + $"ResponseText=<<{result.ResponseText}>>{Environment.NewLine}");
    }
}
