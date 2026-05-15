using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Pty;

namespace Antiphon.Tests.Agents;

/// <summary>
/// Exercises ClaudeAdapter against a real local cmd.exe that emits a synthetic
/// "for Ns" done marker — the same signal ClaudeCrunchedDetector matches on.
/// No live API needed; verifies the adapter wires detectors + buffer-clear correctly.
/// </summary>
[NotInParallel("Pty")]
[Category("Pty")]
public class ClaudeAdapterLocalShellTests
{
    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    private static void SkipIfNotWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new SkipTestException("ConPTY only on Windows");
    }

    private static AgentLaunchSpec InteractiveCmdSpec() => new(
        DefinitionName: "claude-fake",
        Kind: AgentKind.ClaudeCode,
        Exe: Cmd,
        Args: new[] { "/d", "/q", "/k", "@echo off & prompt $G" },
        Env: new Dictionary<string, string>(),
        Cwd: Environment.CurrentDirectory,
        Cols: 120,
        Rows: 30);

    private static IOptions<AgentRegistrySettings> FastOptions() => Options.Create(new AgentRegistrySettings
    {
        DefaultDefinition = "claude-fake",
        Definitions = { ["claude-fake"] = new AgentDefinition { Kind = "ClaudeCode", Exe = Cmd } },
        // Tight budgets — synthetic marker arrives instantly, no LLM latency.
        ClaudeReadyQuietPeriodMs = 250,
        ClaudeReadyMaxWaitMs = 5_000,
        ClaudeDoneMaxWaitMs = 5_000,
    });

    [Test]
    public async Task Send_prompt_clears_live_buffer_before_send()
    {
        SkipIfNotWindows();
        await using var adapter = new ClaudeAdapter(FastOptions());
        await adapter.StartAsync(InteractiveCmdSpec(), CancellationToken.None);

        // First turn — populate buffer with prior content + done marker.
        await adapter.SendPromptAsync("echo OLD_CONTENT_X for 1s", CancellationToken.None);
        var first = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);
        first.TurnCompleted.ShouldBeTrue();
        first.RawSnapshot.ShouldContain("OLD_CONTENT_X");

        // Second turn — ClearLiveBuffer in SendPromptAsync should wipe OLD before NEW lands.
        await adapter.SendPromptAsync("echo NEW_CONTENT_Y for 1s", CancellationToken.None);
        var second = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);

        second.TurnCompleted.ShouldBeTrue();
        second.RawSnapshot.ShouldContain("NEW_CONTENT_Y");
        second.RawSnapshot.ShouldNotContain("OLD_CONTENT_X");
    }

    [Test]
    public async Task Wait_for_turn_complete_detects_synthetic_for_Ns_marker()
    {
        SkipIfNotWindows();
        await using var adapter = new ClaudeAdapter(FastOptions());
        await adapter.StartAsync(InteractiveCmdSpec(), CancellationToken.None);

        await adapter.SendPromptAsync("echo synthetic_resp_marker for 2s", CancellationToken.None);
        var result = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);

        result.TurnCompleted.ShouldBeTrue();
        result.RawSnapshot.ShouldContain("synthetic_resp_marker");
        result.ResponseText.ShouldNotBeNullOrEmpty();
        result.IsAskingQuestion.ShouldBeFalse();
    }

    [Test]
    public async Task Wait_for_turn_complete_returns_false_when_no_marker_within_budget()
    {
        SkipIfNotWindows();
        var tight = Options.Create(new AgentRegistrySettings
        {
            DefaultDefinition = "claude-fake",
            Definitions = { ["claude-fake"] = new AgentDefinition { Kind = "ClaudeCode", Exe = Cmd } },
            ClaudeReadyQuietPeriodMs = 250,
            ClaudeReadyMaxWaitMs = 5_000,
            ClaudeDoneMaxWaitMs = 1_500,
        });
        await using var adapter = new ClaudeAdapter(tight);
        await adapter.StartAsync(InteractiveCmdSpec(), CancellationToken.None);

        // Echo without "for Ns" tail — detector never fires.
        await adapter.SendPromptAsync("echo no_marker_here", CancellationToken.None);
        var result = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);

        result.TurnCompleted.ShouldBeFalse();
    }

    [Test]
    public async Task Adapter_throws_when_methods_called_before_start()
    {
        await using var adapter = new ClaudeAdapter(FastOptions());

        Should.Throw<InvalidOperationException>(() => adapter.SnapshotRawOutput());
        await Should.ThrowAsync<InvalidOperationException>(() => adapter.SendPromptAsync("x", CancellationToken.None));
    }
}
