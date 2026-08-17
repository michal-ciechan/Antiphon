using System.Diagnostics;
using System.Runtime.InteropServices;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Locks the Grok TUI submit/CLI contract against FakeGrok — the same ConPTY path as the real
/// <c>grok</c> CLI, without auth or a live model. Mirrors <see cref="FakeClaudeContractTests"/>
/// for the behaviours Antiphon's queue and profile probes actually depend on.
/// </summary>
[NotInParallel("Headed")]
[Category("Pty")]
public class FakeGrokContractTests
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static string FakeGrokExe =>
        Path.Combine(AppContext.BaseDirectory, "fakegrok", "fakegrok.exe");

    private static void SkipIfUnavailable()
    {
        if (!IsWindows) throw new SkipTestException("ConPTY only on Windows");
        if (!File.Exists(FakeGrokExe))
            throw new SkipTestException($"fakegrok.exe not staged at {FakeGrokExe} — build the solution first");
    }

    private static async Task<PtyAgentRunner> LaunchReadyFakeAsync(
        IDictionary<string, string>? env = null,
        string[]? args = null)
    {
        var runner = new PtyAgentRunner("inbox");
        await runner.StartAsync(FakeGrokExe, args ?? [], cols: 120, rows: 30, env: env);
        runner.Backend!.Backend.ShouldBe(PtyBackend.InboxConhost);
        var ready = await runner.WaitForOutputAsync(
            s => s.Contains("Fake Grok ready"),
            TimeSpan.FromSeconds(15));
        ready.ShouldBeTrue("fake Grok should print its readiness banner: " + runner.SnapshotText());
        runner.ClearLiveBuffer();
        return runner;
    }

    [Test]
    public async Task Version_flag_prints_the_measured_grok_line_and_exits()
    {
        SkipIfUnavailable();
        var psi = new ProcessStartInfo(FakeGrokExe, "--version")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        process.ExitCode.ShouldBe(0);
        output.Trim().ShouldBe("grok 1.0.4 (fakegrok) [stable]");
    }

    [Test]
    public async Task Models_command_prints_the_measured_prose_catalogue()
    {
        SkipIfUnavailable();
        var psi = new ProcessStartInfo(FakeGrokExe, "models")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        process.ExitCode.ShouldBe(0);
        output.ShouldContain("Default model: grok-4.6");
        output.ShouldContain("* grok-4.6 (default)");
        output.ShouldContain("- grok-4.5");
    }

    [Test]
    public async Task Text_and_CR_in_one_write_does_NOT_submit()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        await runner.WriteAsync("queued message\r");

        var submitted = await runner.WaitForOutputAsync(
            s => s.Contains("SUBMITTED:queued message"), TimeSpan.FromSeconds(2));
        submitted.ShouldBeFalse("text+CR in one write must be treated as a paste, not a submit");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Body_then_separate_CR_submits()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        await runner.WriteAsync("queued message");
        await Task.Delay(25);
        await runner.WriteAsync("\r");

        var submitted = await runner.WaitForOutputAsync(
            s => s.Contains("SUBMITTED:queued message"), TimeSpan.FromSeconds(5));
        submitted.ShouldBeTrue("body followed by a separate CR must submit");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Unbracketed_body_with_CR_line_endings_fragments_into_partial_turns()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        await runner.WriteAsync("line one\rline two");
        await Task.Delay(25);
        await runner.WriteAsync("\r");

        var first = await runner.WaitForOutputAsync(
            s => s.Contains("SUBMITTED:line one"), TimeSpan.FromSeconds(5));
        first.ShouldBeTrue("a mid-body CR must submit the fragment before it");
        var second = await runner.WaitForOutputAsync(
            s => s.Contains("SUBMITTED:line two"), TimeSpan.FromSeconds(5));
        second.ShouldBeTrue("the tail after a mid-body CR must submit on the following Enter");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Unbracketed_body_with_LF_line_endings_submits_as_one_intact_turn()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        var body = "HEAD first line of a big paste\n"
            + string.Join("\n", Enumerable.Range(1, 10).Select(i => $"line {i} " + new string('x', 60)))
            + "\nTAIL last line";
        await runner.WriteAsync(body);
        await Task.Delay(25);
        await runner.WriteAsync("\r");

        var intact = await runner.WaitForOutputAsync(
            s =>
            {
                var flat = s.Replace("\r", "").Replace("\n", "");
                return System.Text.RegularExpressions.Regex.IsMatch(
                    flat, @"SUBMITTED:(?:(?!FAKE response).)*HEAD first line(?:(?!FAKE response).)*TAIL last line");
            },
            TimeSpan.FromSeconds(5));
        intact.ShouldBeTrue("LF endings must stay in the composer and submit as one turn");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Enter_on_an_empty_composer_submits_nothing()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        await runner.WriteAsync("\r");
        var submitted = await runner.WaitForOutputAsync(
            s => s.Contains("SUBMITTED:"), TimeSpan.FromSeconds(2));
        submitted.ShouldBeFalse("bare Enter on an empty composer is a no-op");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Session_id_writes_grok_session_files_under_GROK_HOME()
    {
        SkipIfUnavailable();
        var home = Path.Combine(Path.GetTempPath(), $"fakegrok-home-{Guid.NewGuid():N}");
        var cwd = Path.Combine(Path.GetTempPath(), $"fakegrok-cwd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        var sessionId = Guid.NewGuid().ToString("D");
        try
        {
            await using var runner = await LaunchReadyFakeAsync(
                env: new Dictionary<string, string> { ["GROK_HOME"] = home },
                args: ["--cwd", cwd, "--session-id", sessionId, "--model", "grok-4.5"]);

            await runner.WriteAsync("remember this");
            await Task.Delay(25);
            await runner.WriteAsync("\r");
            (await runner.WaitForOutputAsync(
                s => s.Contains("SUBMITTED:remember this"), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue();

            var sessionDir = Path.Combine(home, "sessions", Uri.EscapeDataString(Path.GetFullPath(cwd)), sessionId);
            File.Exists(Path.Combine(sessionDir, "summary.json")).ShouldBeTrue();
            var updates = await File.ReadAllTextAsync(Path.Combine(sessionDir, "updates.jsonl"));
            updates.ShouldContain("user_message_chunk");
            updates.ShouldContain("remember this");
            updates.ShouldContain("agent_message_chunk");

            await runner.KillAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            try { Directory.Delete(home, true); } catch { /* best effort */ }
            try { Directory.Delete(cwd, true); } catch { /* best effort */ }
        }
    }

    [Test]
    public async Task Resume_of_a_missing_session_exits_nonzero()
    {
        SkipIfUnavailable();
        var home = Path.Combine(Path.GetTempPath(), $"fakegrok-home-{Guid.NewGuid():N}");
        Directory.CreateDirectory(home);
        try
        {
            var psi = new ProcessStartInfo(FakeGrokExe, $"--resume {Guid.NewGuid():D}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.Environment["GROK_HOME"] = home;
            using var process = Process.Start(psi)!;
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            process.ExitCode.ShouldNotBe(0);
            stderr.ShouldContain("Session not found");
        }
        finally
        {
            try { Directory.Delete(home, true); } catch { /* best effort */ }
        }
    }
}
