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
///
/// The marker is emitted via a scratch batch script that <c>type</c>s it out of a file
/// (CARD-0015): the marker text must never appear in any command line, because interactive
/// cmd.exe writes the caret-EXPANDED command into the console title (<c>ESC]0;cmd.exe - echo
/// OLD_CONTENT_X for 1s</c>) BEFORE the command's output prints — so the old
/// <c>echo … f^or 1s</c> scheme let the done detector fire on the title chunk, ahead of the
/// output it was supposed to signal. Under parallel load the title→output gap outgrows the
/// detector's 50 ms poll, the next turn's buffer-clear lands in it, and the stale output's
/// marker then completes the wrong turn.
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

    private static AgentLaunchSpec InteractiveCmdSpec(string? cwd = null) => new(
        DefinitionName: "claude-fake",
        Kind: AgentKind.ClaudeCode,
        Exe: Cmd,
        Args: new[] { "/d", "/q" },
        Env: new Dictionary<string, string>(),
        Cwd: cwd ?? Environment.CurrentDirectory,
        Cols: 120,
        Rows: 30);

    /// <summary>
    /// Scratch dir holding <c>emit.cmd</c> (echoes its argument, then types the marker file)
    /// and <c>tail.txt</c> (the literal " for 1s" marker). Typing <c>.\emit.cmd SOMETHING</c>
    /// puts SOMETHING and then the marker on the output stream while no command line — typed
    /// echo or title — ever contains "for". The explicit <c>.\</c> matters: this machine sets
    /// <c>NoDefaultCurrentDirectoryInExePath</c>, which disables implicit current-dir lookup.
    /// </summary>
    private sealed class MarkerScript : IDisposable
    {
        public string Dir { get; } = Path.Combine(
            Path.GetTempPath(), "antiphon-claddapter-" + Guid.NewGuid().ToString("N")[..8]);

        public MarkerScript()
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Path.Combine(Dir, "emit.cmd"),
                "@echo %1\r\n@type \"%~dp0tail.txt\"\r\n");
            File.WriteAllText(Path.Combine(Dir, "tail.txt"), " for 1s\r\n");
        }

        public void Dispose()
        {
            try { Directory.Delete(Dir, recursive: true); } catch { /* scratch — best effort */ }
        }
    }

    private static IOptions<AgentRegistrySettings> FastOptions() => Options.Create(new AgentRegistrySettings
    {
        DefaultDefinition = "claude-fake",
        Definitions = { ["claude-fake"] = new AgentDefinition { Kind = "ClaudeCode", Exe = Cmd } },
        // Tight budgets — synthetic marker arrives instantly, no LLM latency. Quiet period must
        // still exceed the ConPTY echo round-trip under parallel test load (250ms flaked: quiet
        // was declared in the latency gap before cmd's output arrived).
        ClaudeReadyQuietPeriodMs = 750,
        ClaudeReadyMaxWaitMs = 15_000,
        ClaudeDoneMaxWaitMs = 15_000,
    });

    [Test]
    public async Task Send_prompt_clears_live_buffer_before_send()
    {
        SkipIfNotWindows();
        using var script = new MarkerScript();
        await using var adapter = new ClaudeAdapter(FastOptions());
        await adapter.StartAsync(InteractiveCmdSpec(script.Dir), CancellationToken.None);

        // First turn — populate buffer with prior content + done marker. The marker reaches
        // the stream only as emit.cmd's file output, so the detector cannot fire before the
        // turn's real output is in the buffer (see class doc).
        await adapter.SendPromptAsync(".\\emit.cmd OLD_CONTENT_X", CancellationToken.None);
        var first = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);
        first.TurnCompleted.ShouldBeTrue();
        first.RawSnapshot.ShouldContain("OLD_CONTENT_X");

        // Second turn — ClearLiveBuffer in SendPromptAsync should wipe OLD before NEW lands.
        await adapter.SendPromptAsync(".\\emit.cmd NEW_CONTENT_Y", CancellationToken.None);
        var second = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);

        second.TurnCompleted.ShouldBeTrue();
        second.RawSnapshot.ShouldContain("NEW_CONTENT_Y");
        second.RawSnapshot.ShouldNotContain("OLD_CONTENT_X");
    }

    [Test]
    public async Task Wait_for_turn_complete_detects_synthetic_for_Ns_marker()
    {
        SkipIfNotWindows();
        using var script = new MarkerScript();
        await using var adapter = new ClaudeAdapter(FastOptions());
        await adapter.StartAsync(InteractiveCmdSpec(script.Dir), CancellationToken.None);

        await adapter.SendPromptAsync(".\\emit.cmd synthetic_resp_marker", CancellationToken.None);
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
