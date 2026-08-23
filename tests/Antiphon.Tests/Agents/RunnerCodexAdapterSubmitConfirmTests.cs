using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>
/// CARD-0108 S1: <see cref="RunnerCodexAdapter.SendPromptAsync"/> proves the submit instead of
/// assuming it. Measured 2026-08-20 against codex-cli 0.147.0, the production path (body, 20 ms, a
/// separate CR) left the prompt stranded in the composer <b>6 times out of 6</b> — the CR folding
/// inside the TUI's paste-detection window — while reporting success; one extra Enter ~4 s later
/// submitted 6/6.
/// </summary>
public class RunnerCodexAdapterSubmitConfirmTests
{
    private const string Body = "Reply with exactly PONG and no other text.";

    [Test]
    public async Task A_folded_first_CR_is_recovered_by_pressing_Enter_again_and_never_by_re_typing()
    {
        var client = new ScriptedCodexRunnerClient { ConfirmAfterEnters = 2 };
        var adapter = NewAdapter(client);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        await adapter.SendPromptAsync(Body, CancellationToken.None);

        client.Enters.ShouldBe(2, "the first CR folded; exactly one re-press was needed");
        client.BodyWrites.ShouldBe(1,
            "the body must be typed ONCE — a re-type onto a composer that is holding it sends it twice");
    }

    [Test]
    public async Task An_unconfirmed_submit_over_a_live_transcript_throws_prompt_delivery()
    {
        var client = new ScriptedCodexRunnerClient
        {
            ConfirmAfterEnters = 0, // no Enter ever submits
        };
        // A row from an earlier turn: the transcript pipeline is demonstrably alive, so "no
        // confirming row" is the pipeline saying the body did not submit — not an absent observer.
        client.Seed(Row(1, TranscriptKinds.UserPrompt, "a prompt from the previous turn"));

        var adapter = NewAdapter(client);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        var ex = await Should.ThrowAsync<PromptDeliveryException>(
            () => adapter.SendPromptAsync(Body, CancellationToken.None));

        ex.Message.ShouldContain("no UserPrompt transcript row");
        client.Enters.ShouldBe(4, "the initial CR plus CodexSubmitAttempts (3) re-presses");
        client.BodyWrites.ShouldBe(1);
    }

    [Test]
    public async Task A_body_still_standing_in_the_composer_is_named_in_the_failure_and_blocks_a_re_type()
    {
        var client = new ScriptedCodexRunnerClient
        {
            ConfirmAfterEnters = 0,
            // The measured failure mode: the body arrived in the composer perfectly and only the
            // Enter was lost, so it is still on screen when the confirm loop gives up.
            QuietScreen = $"  codex\n  > {Body}\n  gpt-5.6-luna low\n",
        };
        client.Seed(Row(1, TranscriptKinds.UserPrompt, "a prompt from the previous turn"));

        var adapter = NewAdapter(client);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        var ex = await Should.ThrowAsync<PromptDeliveryException>(
            () => adapter.SendPromptAsync(Body, CancellationToken.None));

        ex.ComposerMayHoldBody.ShouldBeTrue(
            "AgentSessionService.SendBootPromptWithRetryAsync keys its skip-the-re-type on this");
        ex.Message.ShouldContain("STILL SHOWS");
    }

    [Test]
    public async Task A_session_with_no_observable_transcript_degrades_to_a_blind_send_instead_of_failing()
    {
        var client = new ScriptedCodexRunnerClient
        {
            ConfirmAfterEnters = 0,
            ThrowOnTranscript = true, // bind refused / no tailer: nothing to confirm against
        };
        var adapter = NewAdapter(client);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        // Must NOT throw: a missing observer is not evidence that the prompt failed, and failing
        // the launch over it would kill sessions that are working fine (CARD-0055's degrade posture).
        await adapter.SendPromptAsync(Body, CancellationToken.None);

        client.BodyWrites.ShouldBe(1);
    }

    [Test]
    public async Task A_transient_fetch_failure_on_a_later_turn_does_not_confirm_against_the_previous_UserPrompt()
    {
        // CARD-0113 sibling: the same `?? 0` floor is the submit-confirmation baseline. A
        // second send of the same body whose capture fetch misses would otherwise match the
        // previous turn's UserPrompt (sequence > 0) and report Sent without this Enter landing.
        var client = new ScriptedCodexRunnerClient { ConfirmAfterEnters = 1 };
        var adapter = NewAdapter(client);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        await adapter.SendPromptAsync(Body, CancellationToken.None);
        client.BodyWrites.ShouldBe(1);
        client.Enters.ShouldBe(1);

        client.RemainingTranscriptFailures = 1;
        client.ConfirmAfterEnters = 0; // the second Enter must not be treated as a submit

        var ex = await Should.ThrowAsync<PromptDeliveryException>(
            () => adapter.SendPromptAsync(Body, CancellationToken.None));

        ex.Message.ShouldContain("no UserPrompt transcript row");
        client.BodyWrites.ShouldBe(2, "the second turn types the body once");
    }

    [Test]
    public async Task A_confirmed_first_CR_costs_no_extra_enters()
    {
        var client = new ScriptedCodexRunnerClient { ConfirmAfterEnters = 1 };
        var adapter = NewAdapter(client);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        await adapter.SendPromptAsync(Body, CancellationToken.None);

        client.Enters.ShouldBe(1, "a submit that landed first time must not be poked again");
    }

    private static RunnerCodexAdapter NewAdapter(ISessionRunnerClient client) =>
        new(
            client,
            Options.Create(new AgentRegistrySettings
            {
                // The production shape at test speed: 3 extra Enters inside the overall budget.
                CodexSubmitReEnterIntervalMs = 150,
                CodexSubmitAttempts = 3,
                CodexSubmitConfirmTimeoutMs = 2_000,
            }));

    private static AgentLaunchSpec NewSpec() => new(
        DefinitionName: "codex",
        Kind: AgentKind.Codex,
        Exe: "codex.exe",
        Args: [],
        Env: new Dictionary<string, string>(),
        Cwd: Path.GetTempPath(),
        Cols: 120,
        Rows: 30,
        SessionId: Guid.NewGuid());

    private static SessionRunnerTranscriptEvent Row(long seq, string kind, string? text) =>
        new(
            Guid.Empty, seq, kind, $"uuid-{seq}", null, DateTimeOffset.UtcNow, null, text,
            null, null, null, null, null);
}
