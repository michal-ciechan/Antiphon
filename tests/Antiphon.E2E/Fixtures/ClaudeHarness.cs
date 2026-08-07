using System.Runtime.InteropServices;
using Antiphon.Agents.Pty;
using TUnit.Core.Exceptions;

namespace Antiphon.E2E;

/// <summary>
/// Drives a real Claude TUI in a ConPTY for the headed delegation tests.
///
/// Three things here are load-bearing and were each learned the hard way against the live CLI:
///  * the trust-folder dialog must be POLLED for, not checked once (§<see cref="WaitUntilUsableAsync"/>);
///  * a body and its submitting carriage return must be SEPARATE writes (§<see cref="SubmitAsync"/>);
///  * turn completion is detected by a quiet window, not by matching TUI wording.
/// </summary>
public sealed class ClaudeHarness : IAsyncDisposable
{
    private readonly PtyAgentRunner _runner;

    private ClaudeHarness(PtyAgentRunner runner) => _runner = runner;

    public static async Task<ClaudeHarness> StartAsync(
        string cwd, IDictionary<string, string> env, string model)
    {
        TrustDirectory(cwd);

        var runner = new PtyAgentRunner();
        var (app, args) = BuildLaunch(ResolveClaudeOrThrow(), "--dangerously-skip-permissions", "--model", model);
        await runner.StartAsync(app, args, cwd: cwd, env: env, cols: 120, rows: 30);

        var harness = new ClaudeHarness(runner);
        if (!await harness.WaitUntilUsableAsync())
        {
            await runner.DisposeAsync();
            throw new SkipTestException($"real Claude TUI did not reach a usable state in {cwd}");
        }

        runner.ClearLiveBuffer();
        return harness;
    }

    /// <summary>
    /// Pre-accept the trust dialog for a directory by writing Claude's own config flag.
    ///
    /// Answering the dialog interactively is not reliable: <see cref="PtyAgentRunner.SnapshotScreen"/>
    /// includes scrollback, so once the dialog has been rendered its text stays in the buffer even
    /// after it is dismissed — a poll loop cannot tell "still waiting" from "already answered", and
    /// keeps sending Enter into a live session. Setting the flag Claude itself sets means the dialog
    /// never appears, which is deterministic.
    ///
    /// Only ever ADDS an entry for a throwaway test directory; existing entries are untouched.
    /// </summary>
    private static void TrustDirectory(string cwd)
    {
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");
        if (!File.Exists(configPath))
            return;

        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(configPath))?.AsObject();
            if (node is null)
                return;

            if (node["projects"] is not System.Text.Json.Nodes.JsonObject projects)
            {
                projects = new System.Text.Json.Nodes.JsonObject();
                node["projects"] = projects;
            }

            // Claude keys projects inconsistently — some entries use backslashes, some forward
            // slashes (observed in a real ~/.claude.json). Writing only one form silently misses,
            // and the dialog appears anyway. Write both.
            var full = Path.GetFullPath(cwd);
            foreach (var key in new[] { full, full.Replace('\\', '/') })
            {
                if (projects[key] is not System.Text.Json.Nodes.JsonObject project)
                {
                    project = new System.Text.Json.Nodes.JsonObject();
                    projects[key] = project;
                }
                project["hasTrustDialogAccepted"] = true;
                project["projectOnboardingSeenCount"] = 1;
                project["hasCompletedProjectOnboarding"] = true;
            }

            File.WriteAllText(configPath, node.ToJsonString(new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
            }));
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            // Best effort — the interactive fallback below still gets a chance.
        }
    }

    public string Screen() => _runner.SnapshotScreen();

    /// <summary>
    /// Type a body and submit it. Line endings are normalised to LF first (a CR mid-body acts as
    /// Enter and would submit the fragment before it), then the submitting carriage return is a
    /// SEPARATE write after a pause — body and CR in one write are treated as a bracketed paste and
    /// the CR is folded into a literal newline, leaving the text sitting unsent in the composer.
    /// This is the same discipline <c>SessionMessageQueueService.DeliverAsync</c> uses in production.
    /// </summary>
    public async Task SubmitAsync(string body)
    {
        var normalized = body.ReplaceLineEndings("\n");

        // STEP 1 — get the text in, and confirm it arrived. Input written before the TUI's backend
        // has finished connecting is accepted by the terminal and silently DROPPED (the readiness
        // detector's whole reason for existing, and its 9s minimum is not always enough on a cold
        // start). A dropped body leaves an empty composer, every later carriage return is a no-op,
        // and the failure surfaces minutes downstream as "the model ignored the instruction".
        var probe = Squash(normalized)[..Math.Min(24, Squash(normalized).Length)];
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await _runner.WriteAsync(normalized);
            if (await _runner.WaitForScreenAsync(
                    s => Squash(s).Contains(probe, StringComparison.Ordinal), TimeSpan.FromSeconds(10)))
                break;

            if (attempt == 3)
            {
                throw new InvalidOperationException(
                    $"The TUI never accepted the typed body after 3 attempts — input is being "
                    + $"dropped, which usually means the session was not ready.\nScreen:\n{Screen()}");
            }

            // Clear the composer before retyping. A partially-landed body plus a retry concatenates
            // into one garbled prompt — worse than the dropped input we are recovering from.
            await _runner.WriteAsync("");  // Ctrl+U — kill line
            await Task.Delay(2_000);
        }

        await Task.Delay(1_200);

        // VERIFY the submit, don't assume it — and verify with a POSITIVE signal that a turn
        // started, not with the absence of the text from the composer. "Composer looks empty" is
        // true before the text has even rendered, so it reports success for a submission that never
        // happened and the test then fails minutes later, somewhere unrelated.
        //
        // While Claude is working it always shows an interrupt hint. That appearing is proof the
        // prompt was accepted; it not appearing means the carriage return did nothing.
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await _runner.WriteAsync("\r");
            if (await _runner.WaitForScreenAsync(TurnHasStarted, TimeSpan.FromSeconds(12)))
                return;

            // A modal can appear the moment a prompt is submitted (a permission request). Clear it
            // and count that as the turn having started.
            if (await ClaudeBlockingPromptDetector.WaitForAsync(_runner, TimeSpan.FromSeconds(1)) is { } modal)
            {
                await ClaudeBlockingPromptDetector.TryAnswerAsync(_runner, modal);
                return;
            }
        }

        throw new InvalidOperationException(
            $"The TUI never started a turn after 4 submit attempts — the prompt was not accepted.\n"
            + $"Screen:\n{Screen()}");
    }

    /// <summary>
    /// True once the TUI is visibly working. Claude renders an interrupt hint and a token/elapsed
    /// counter for the whole of a turn, so either is a reliable "the prompt was accepted" signal.
    /// </summary>
    private static bool TurnHasStarted(string screen)
    {
        // ONLY the interrupt hint. An elapsed-seconds counter looks tempting but also appears on
        // COMPLETED tool annotations ("Listing 1 directory… (21s)"), which never leave the screen —
        // using it means the turn appears to run forever and every wait times out.
        var compact = string.Concat(screen.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        return compact.Contains("esctointerrupt");
    }

    private static string Squash(string value) =>
        string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Wait for the turn to genuinely finish.
    ///
    /// Quiet alone is not enough: a hook, a tool call or a slow first token can leave the pty silent
    /// for longer than any sane quiet window, and the caller then scrapes hook output as if it were
    /// the model's report. So wait for the working indicator to GO AWAY first — a positive end
    /// signal — and only then require a quiet window to be sure the text has finished rendering.
    /// </summary>
    public async Task<bool> WaitForTurnEndAsync(TimeSpan? timeout = null)
    {
        var budget = timeout ?? TimeSpan.FromMinutes(5);

        // The turn is over when the COMPOSER comes back — a positive signal, unlike silence.
        // Quiet-window detection does not work against current Claude builds: the TUI animates its
        // status line continuously, so the pty never goes quiet even when the model has finished,
        // and every quiet-based wait burns its whole budget on a turn that ended minutes earlier.
        var ended = await _runner.WaitForScreenAsync(
            s => ComposerIsLive(s) && !TurnHasStarted(s), budget);
        if (!ended)
            return false;

        // Trailing text can render just after the composer returns; give it a moment to land before
        // anything scrapes the screen for the report.
        await Task.Delay(2_000);
        return true;
    }

    /// <summary>
    /// The tail of the screen as the delegate's report. Deliberately loose: these tests assert that
    /// a report EXISTS and carries specific evidence through the pipeline, never its wording — a
    /// model is not a deterministic fixture.
    /// </summary>
    public string LastMessage()
    {
        var lines = Screen().ReplaceLineEndings("\n").Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => l.Trim().Length > 0)
            // Drop TUI chrome: the composer, the hint bar, and box-drawing rules.
            .Where(l => !l.TrimStart().StartsWith('>')
                && !l.Contains("for shortcuts")
                && !l.Contains("bypass permissions")
                && l.Trim('─', '│', '╭', '╮', '╰', '╯', ' ').Length > 0)
            .ToList();

        return string.Join("\n", lines.TakeLast(40)).Trim();
    }

    /// <summary>
    /// Reach a usable prompt, answering the trust-folder dialog on the way.
    ///
    /// A scratch directory is unknown to Claude, so its first screen can be "Is this a project you
    /// created or one you trust?" — a modal that swallows every keystroke until answered. It renders
    /// during the quiet window a readiness check waits for, so a single check lands too early about
    /// half the time; the session then looks ready, accepts an instruction, and silently does
    /// nothing. Polling is what makes this reliable.
    /// </summary>
    private async Task<bool> WaitUntilUsableAsync()
    {
        if (!await new ClaudeReadyDetector().WaitAsync(_runner))
            return false;

        // Insurance only — TrustDirectory should have prevented the dialog. If one is up anyway,
        // answer it with the DIGIT and verify the screen clears; Enter accepts whatever happens to
        // be highlighted, which is not reliably option 1 (pinned by ClaudeTrustPromptCanaryTests).
        if (await ClaudeBlockingPromptDetector.WaitForAsync(_runner) is { } blocking)
        {
            if (!await ClaudeBlockingPromptDetector.TryAnswerAsync(_runner, blocking))
                return false;
        }

        // A quiet window is not proof the composer is accepting input — the TUI renders its prompt
        // long before the backend finishes connecting, and anything typed in between is swallowed.
        // The empty-composer PLACEHOLDER only appears once the composer is genuinely live, which
        // makes it a positive readiness signal rather than an inference from silence.
        await _runner.WaitForScreenAsync(ComposerIsLive, TimeSpan.FromSeconds(45));
        return true;
    }

    private static bool ComposerIsLive(string screen)
    {
        var compact = string.Concat(screen.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        return compact.Contains("forshortcuts") || compact.Contains("trycreateautil");
    }

    /// <summary>
    /// True when the session is sitting on a modal instead of working — useful for failing a test
    /// with a real explanation rather than a mystery timeout.
    /// </summary>
    public ClaudeBlockingPrompt? BlockedOn() => ClaudeBlockingPromptDetector.Detect(Screen());

    public ValueTask DisposeAsync() => _runner.DisposeAsync();

    // ---- eligibility + launch ------------------------------------------------------------------

    public static void SkipIfNotEligible()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new SkipTestException("Headed tests require Windows ConPTY");
        if (Environment.GetEnvironmentVariable("ANTIPHON_HEADED_TESTS") != "1")
            throw new SkipTestException("Set ANTIPHON_HEADED_TESTS=1 to opt in to headed-claude tests");
        if (ResolveClaude() is null)
            throw new SkipTestException("claude not found on PATH; cannot run headed tests");
    }

    private static string ResolveClaudeOrThrow() =>
        ResolveClaude() ?? throw new InvalidOperationException("claude not found on PATH");

    private static string? ResolveClaude()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var name in new[] { "claude.exe", "claude.cmd", "claude.bat", "claude.ps1" })
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    private static (string App, string[] Args) BuildLaunch(string claude, params string[] extraArgs)
    {
        if (claude.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            var args = new List<string> { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", claude };
            args.AddRange(extraArgs);
            return ("pwsh.exe", args.ToArray());
        }
        if (claude.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return (claude, extraArgs);

        var cmdArgs = new List<string> { "/d", "/c", claude };
        cmdArgs.AddRange(extraArgs);
        return (Path.Combine(Environment.SystemDirectory, "cmd.exe"), cmdArgs.ToArray());
    }
}
