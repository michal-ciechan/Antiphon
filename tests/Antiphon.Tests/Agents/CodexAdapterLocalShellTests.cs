using System.Diagnostics;
using System.Runtime.InteropServices;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Pty;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Agents;

/// <summary>
/// Exercises CodexAdapter against a real local cmd.exe. This keeps the test
/// offline while proving the adapter can start, send input, detect a quiet
/// completed turn, detect questions, and stop the underlying process.
/// </summary>
[NotInParallel("Pty")]
[Category("Pty")]
public class CodexAdapterLocalShellTests
{
    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    private static void SkipIfNotWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new SkipTestException("ConPTY only on Windows");
    }

    private static AgentLaunchSpec InteractiveCmdSpec() => new(
        DefinitionName: "codex-fake",
        Kind: AgentKind.Codex,
        Exe: Cmd,
        Args: new[] { "/d", "/q", "/k", "@echo off & prompt $G" },
        Env: new Dictionary<string, string>(),
        Cwd: Environment.CurrentDirectory,
        Cols: 120,
        Rows: 30);

    private static AgentLaunchSpec TrustPromptCmdSpec(string batchPath) => new(
        DefinitionName: "codex-fake",
        Kind: AgentKind.Codex,
        Exe: Cmd,
        Args: new[] { "/d", "/q", "/k", batchPath },
        Env: new Dictionary<string, string>(),
        Cwd: Environment.CurrentDirectory,
        Cols: 120,
        Rows: 30);

    private static IOptions<AgentRegistrySettings> FastOptions() => Options.Create(new AgentRegistrySettings
    {
        DefaultDefinition = "codex-fake",
        Definitions = { ["codex-fake"] = new AgentDefinition { Kind = "Codex", Exe = Cmd } },
        CodexReadyQuietPeriodMs = 250,
        CodexReadyMaxWaitMs = 5_000,
        CodexDoneQuietPeriodMs = 250,
        CodexDoneMaxWaitMs = 5_000,
    });

    [Test]
    public async Task Wait_for_turn_complete_returns_question_state_after_quiet_output()
    {
        SkipIfNotWindows();
        await using var adapter = new CodexAdapter(FastOptions());
        await adapter.StartAsync(InteractiveCmdSpec(), CancellationToken.None);
        (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeTrue();

        await adapter.SendPromptAsync("echo Should we continue?", CancellationToken.None);
        var result = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);

        result.TurnCompleted.ShouldBeTrue();
        result.RawSnapshot.ShouldContain("Should we continue?");
        result.ResponseText.ShouldNotBeNull();
        result.ResponseText.ShouldContain("Should we continue?");
        result.IsAskingQuestion.ShouldBeTrue();
    }

    [Test]
    public async Task Wait_for_ready_accepts_codex_directory_trust_prompt()
    {
        SkipIfNotWindows();
        using var bat = new PtyTempBatch("""
            @echo off
            echo Do you trust the contents of this directory?
            echo 1. Yes, continue
            set /p CHOICE=
            echo READY_AFTER_TRUST
            prompt $G
            """);
        await using var adapter = new CodexAdapter(FastOptions());
        await adapter.StartAsync(TrustPromptCmdSpec(bat.Path), CancellationToken.None);

        var ready = await adapter.WaitForReadyAsync(CancellationToken.None);

        ready.ShouldBeTrue();
        adapter.SnapshotRawOutput().ShouldContain("READY_AFTER_TRUST");
    }

    [Test]
    public async Task Question_detection_ignores_question_mark_in_prompt_echo()
    {
        SkipIfNotWindows();
        await using var adapter = new CodexAdapter(FastOptions());
        await adapter.StartAsync(InteractiveCmdSpec(), CancellationToken.None);
        (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeTrue();

        await adapter.SendPromptAsync("echo answer has no question & rem prompt has a question?", CancellationToken.None);
        var result = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);

        result.TurnCompleted.ShouldBeTrue();
        result.ResponseText.ShouldNotBeNull();
        result.ResponseText.ShouldContain("answer has no question");
        result.IsAskingQuestion.ShouldBeFalse();
    }

    [Test]
    public async Task Kill_terminates_codex_process_with_stopped_exit_reason()
    {
        SkipIfNotWindows();
        await using var adapter = new CodexAdapter(FastOptions());
        await adapter.StartAsync(InteractiveCmdSpec(), CancellationToken.None);
        (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeTrue();

        await Task.Delay(300);
        var sw = Stopwatch.StartNew();
        var killed = await adapter.KillAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        sw.Stop();

        killed.ShouldBeTrue();
        adapter.ExitReason.ShouldBe(AgentExitReason.KilledByRequest);
        sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2.5));
    }
}
