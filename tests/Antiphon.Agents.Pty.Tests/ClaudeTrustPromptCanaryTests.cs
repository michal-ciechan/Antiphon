using System.Diagnostics;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Trust-prompt canary against REAL Claude.
///
/// A TUI blocked on a modal is invisible to everything downstream: it writes no transcript records,
/// so it is neither mid-turn nor idle, and any queued delivery strands behind it forever. This pins
/// the two things we need to be true of that state:
///
///   1. it is DETECTED, and detected FAST — a modal renders in one frame, so a detector that needs
///      tens of seconds is useless for deciding "is this session stuck?";
///   2. it can be ANSWERED, and the answer verifiably clears the screen.
///
/// Against Claude 2.1.258 the dialog is an unnumbered highlighted list whose default is No. The
/// answerer moves the highlight (j / Down / Ctrl+N) and only then sends Enter. Measured 2026-09-05
/// on 2.1.258: the first rung <c>j</c> moves the highlight, but only on the modern ConPTY backend
/// (inbox conhost delivers the bytes and the snapshot corrupts; the Select binding never fires).
/// Production session-runner is modern; this canary pins that backend.
///
/// Opt-in headed: ANTIPHON_HEADED_TESTS=1 + claude on PATH; self-skips otherwise.
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[Category("HeadedCanary")]
[ParallelLimiter<ProcessSpawnLimit>]
public class ClaudeTrustPromptCanaryTests
{
    /// <summary>The budget the user asked for: a couple of seconds, not tens.</summary>
    private static readonly TimeSpan DetectionBudget = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The dialog ignores keys for a short window after it opens. Production answers after the 5 s
    /// quiet gate; the canary must not type within the first second of detecting the dialog.
    /// </summary>
    private static readonly TimeSpan RefuseWindow = TimeSpan.FromMilliseconds(1500);

    [Test]
    public async Task An_untrusted_directory_blocks_the_tui_and_is_detected_within_seconds()
    {
        ClSession.SkipIfNotEligible();
        using var dir = new UntrustedDirectory();

        await using var runner = new PtyAgentRunner("modern");
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(), "--dangerously-skip-permissions");
        await runner.StartAsync(app, args, cwd: dir.Path, env: ClSession.HeadedSafeEnv(), cols: 120, rows: 30);

        var appeared = await runner.WaitForScreenAsync(
            ClaudeBlockingPromptDetector.IsBlocked, TimeSpan.FromSeconds(45));
        if (!appeared)
        {
            throw new SkipTestException(
                "no trust dialog appeared — this directory is already trusted in ~/.claude.json, "
                + "so there is nothing to canary. Screen:\n" + runner.SnapshotScreen());
        }

        var stopwatch = Stopwatch.StartNew();
        var prompt = await ClaudeBlockingPromptDetector.WaitForAsync(runner, DetectionBudget);
        stopwatch.Stop();

        prompt.ShouldNotBeNull("a rendered modal must be detected. Screen:\n" + runner.SnapshotScreen());
        stopwatch.Elapsed.ShouldBeLessThan(
            DetectionBudget,
            "detection has to be fast enough to decide 'this session is stuck' in real time");

        prompt!.Kind.ShouldBe(ClaudeBlockingPromptKind.TrustFolder);
        prompt.Layout.ShouldBe(ClaudeTrustDialogLayout.HighlightedList);
        Console.WriteLine($"Detected in {stopwatch.ElapsedMilliseconds}ms: [{prompt.Kind}/{prompt.Layout}] {prompt.Title}");

        await Task.Delay(RefuseWindow);

        var (cleared, detail) = await ClaudeBlockingPromptDetector.TryAnswerDetailedAsync(runner, prompt);
        Console.WriteLine("Answer detail: " + detail);
        cleared.ShouldBeTrue("answering must clear the dialog. Detail: " + detail
            + "\nScreen:\n" + runner.SnapshotScreen());
        detail.ShouldContain("j", Case.Sensitive,
            "measured 2026-09-05 on Claude 2.1.258 / modern ConPTY: first ladder rung j moves the highlight");

        ClaudeBlockingPromptDetector.IsBlocked(runner.SnapshotScreen())
            .ShouldBeFalse("the TUI must be usable once the dialog is answered");

        var trusted = await WaitForTrustedAsync(dir.Path, TimeSpan.FromSeconds(8));
        trusted.ShouldBeTrue(
            "Claude must persist hasTrustDialogAccepted for " + ClaudeProjectTrust.ProjectKey(dir.Path));
    }

    [Test]
    public async Task The_2_1_258_dialog_is_the_highlighted_list_layout()
    {
        ClSession.SkipIfNotEligible();
        using var dir = new UntrustedDirectory();

        await using var runner = new PtyAgentRunner("modern");
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(), "--dangerously-skip-permissions");
        await runner.StartAsync(app, args, cwd: dir.Path, env: ClSession.HeadedSafeEnv(), cols: 120, rows: 30);

        var appeared = await runner.WaitForScreenAsync(
            ClaudeBlockingPromptDetector.IsBlocked, TimeSpan.FromSeconds(45));
        if (!appeared)
        {
            throw new SkipTestException(
                "no trust dialog appeared — this directory is already trusted in ~/.claude.json, "
                + "so there is nothing to canary. Screen:\n" + runner.SnapshotScreen());
        }

        var prompt = ClaudeBlockingPromptDetector.Detect(runner.SnapshotScreen());
        prompt.ShouldNotBeNull();
        prompt!.Kind.ShouldBe(ClaudeBlockingPromptKind.TrustFolder);
        prompt.Layout.ShouldBe(
            ClaudeTrustDialogLayout.HighlightedList,
            "a TUI change that drops the highlighted-list shape must fail this canary, not silently "
            + "type the wrong key. Screen:\n" + runner.SnapshotScreen());
        ClaudeBlockingPromptDetector.ReadHighlight(runner.SnapshotScreen())
            .ShouldBe(ClaudeTrustDialogHighlight.No);
    }

    [Test]
    public async Task A_trusted_directory_never_reads_as_blocked()
    {
        ClSession.SkipIfNotEligible();
        using var dir = new UntrustedDirectory();
        var seeded = ClaudeProjectTrust.Seed(dir.Path);
        seeded.Outcome.ShouldBe(
            ClaudeProjectTrustOutcome.Seeded,
            "seeder must write the exact-key flag into the real config. "
            + seeded.Outcome + " " + seeded.Error);

        await using var runner = new PtyAgentRunner("modern");
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(), "--dangerously-skip-permissions");
        await runner.StartAsync(app, args, cwd: dir.Path, env: ClSession.HeadedSafeEnv(), cols: 120, rows: 30);

        (await new ClaudeReadyDetector().WaitAsync(runner)).ShouldBeTrue("the TUI must come up");

        ClaudeBlockingPromptDetector.IsBlocked(runner.SnapshotScreen())
            .ShouldBeFalse("a seeded directory shows no modal. Screen:\n" + runner.SnapshotScreen());
    }

    private static async Task<bool> WaitForTrustedAsync(string directory, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (ClaudeProjectTrust.IsTrusted(directory))
                return true;
            await Task.Delay(100);
        }
        return ClaudeProjectTrust.IsTrusted(directory);
    }

    /// <summary>
    /// A throwaway directory Claude has never seen, under <c>C:\logs\antiphon</c> so no git-root
    /// ancestor walk can inherit trust from the repo. Dispose removes the key the canary added.
    /// </summary>
    private sealed class UntrustedDirectory : IDisposable
    {
        public string Path { get; }

        public UntrustedDirectory()
        {
            var root = @"C:\logs\antiphon";
            Directory.CreateDirectory(root);
            Path = System.IO.Path.Combine(
                root, "antiphon-trust-canary-" + Guid.NewGuid().ToString("N")[..12]);
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try { ClaudeProjectTrust.Remove(Path); }
            catch { /* never throw from Dispose */ }
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
