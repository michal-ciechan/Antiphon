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
/// Also pins the mechanism choice: the affirmative DIGIT, not Enter. Enter accepts whatever is
/// highlighted, which is not necessarily option 1 once anything has touched the selection.
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

    [Test]
    public async Task An_untrusted_directory_blocks_the_tui_and_is_detected_within_seconds()
    {
        ClSession.SkipIfNotEligible();
        using var dir = new UntrustedDirectory();

        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(), "--dangerously-skip-permissions");
        await runner.StartAsync(app, args, cwd: dir.Path, env: ClSession.HeadedSafeEnv(), cols: 120, rows: 30);

        // The dialog is part of startup, so allow the process to boot — then the DETECTION itself
        // must be fast. Those are different budgets and only the second one is under test.
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
        prompt.AffirmativeKey.ShouldBe("1", "numbered menus take the digit, not Enter");
        Console.WriteLine($"Detected in {stopwatch.ElapsedMilliseconds}ms: [{prompt.Kind}] {prompt.Title}");

        // ...and answering it must verifiably clear the modal.
        (await ClaudeBlockingPromptDetector.TryAnswerAsync(runner, prompt))
            .ShouldBeTrue("answering must clear the dialog. Screen:\n" + runner.SnapshotScreen());

        ClaudeBlockingPromptDetector.IsBlocked(runner.SnapshotScreen())
            .ShouldBeFalse("the TUI must be usable once the dialog is answered");
    }

    [Test]
    public async Task A_trusted_directory_never_reads_as_blocked()
    {
        // The other half: a detector that fires on a healthy session is worse than none, because it
        // would have callers answering dialogs that aren't there — keystrokes into a live prompt.
        ClSession.SkipIfNotEligible();
        using var dir = new UntrustedDirectory();
        dir.Trust();

        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(), "--dangerously-skip-permissions");
        await runner.StartAsync(app, args, cwd: dir.Path, env: ClSession.HeadedSafeEnv(), cols: 120, rows: 30);

        (await new ClaudeReadyDetector().WaitAsync(runner)).ShouldBeTrue("the TUI must come up");

        ClaudeBlockingPromptDetector.IsBlocked(runner.SnapshotScreen())
            .ShouldBeFalse("a trusted directory shows no modal. Screen:\n" + runner.SnapshotScreen());
    }

    /// <summary>
    /// A throwaway directory Claude has never seen, with a helper to pre-accept its trust dialog the
    /// way Claude itself records it.
    /// </summary>
    private sealed class UntrustedDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-trust-canary").FullName;

        /// <summary>
        /// Write Claude's own trust flag for this directory. Both key spellings are written:
        /// a real ~/.claude.json contains project keys with backslashes AND with forward slashes,
        /// and writing only one form silently misses — the dialog then appears anyway.
        /// </summary>
        public void Trust()
        {
            var configPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");
            if (!File.Exists(configPath))
                return;

            var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(configPath))?.AsObject();
            if (node is null)
                return;

            if (node["projects"] is not System.Text.Json.Nodes.JsonObject projects)
            {
                projects = new System.Text.Json.Nodes.JsonObject();
                node["projects"] = projects;
            }

            foreach (var key in new[] { Path, Path.Replace('\\', '/') })
            {
                if (projects[key] is not System.Text.Json.Nodes.JsonObject project)
                {
                    project = new System.Text.Json.Nodes.JsonObject();
                    projects[key] = project;
                }
                project["hasTrustDialogAccepted"] = true;
                project["hasCompletedProjectOnboarding"] = true;
                project["projectOnboardingSeenCount"] = 1;
            }

            File.WriteAllText(configPath, node.ToJsonString(
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
