using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// The periodic snapshot canary (the second half of the "both" drift strategy, alongside the functional
/// <see cref="ClaudeSubmitContractTests"/>). It probes the low-level signals our stack reads off a real
/// Claude session and compares the robust one against a committed baseline, so signal drift is caught even
/// if no one writes a new functional test.
///
/// Opt-in headed and intended to run on a schedule (e.g. nightly) — it spawns real Claude:
/// <code>
///   $env:ANTIPHON_HEADED_TESTS = "1"
///   dotnet run --project tests/Antiphon.Agents.Pty.Tests -- --treenode-filter "/*/*/ClaudeSignalCanaryTests/*"
/// </code>
///
/// Hard assertion: Claude still emits the <c>" for Ns"</c> turn-end summary — the ConPTY-independent
/// signal <see cref="ClaudeCrunchedDetector"/> depends on. Everything else (CLI version, whether the idle
/// OSC title survives this ConPTY) is recorded to <c>claude-signals.observed.json</c> in the test output
/// for a human to eyeball, but not asserted: the version churns every release and idle-title visibility is
/// a ConPTY-config quirk, not a Claude behaviour. When the hard signal legitimately changes, re-baseline
/// <c>golden/claude-signals.baseline.json</c> deliberately and say why in the commit.
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[Category("HeadedCanary")]
public class ClaudeSignalCanaryTests
{
    private static readonly Regex DonePattern = new(@" for \d+s", RegexOptions.Compiled);
    private static readonly Regex VersionPattern = new(@"\d+\.\d+\.\d+", RegexOptions.Compiled);

    private sealed record Baseline(bool ClaudeEmitsDonePattern);

    [Test]
    public async Task Real_claude_still_emits_the_turn_end_signal_we_depend_on()
    {
        ClSession.SkipIfNotEligible();

        var version = await CaptureVersionAsync();
        var (emitsDonePattern, emitsIdleTitleInStream) = await CaptureTurnSignalsAsync();

        // Record the full observed snapshot for a human to review (not asserted beyond the hard signal).
        var observed = new Dictionary<string, object?>
        {
            ["capturedVersion"] = version,
            ["claudeEmitsDonePattern"] = emitsDonePattern,
            ["idleTitleSurvivesThisConPty"] = emitsIdleTitleInStream,
        };
        var observedPath = Path.Combine(AppContext.BaseDirectory, "claude-signals.observed.json");
        File.WriteAllText(observedPath, JsonSerializer.Serialize(observed, new JsonSerializerOptions { WriteIndented = true }));

        version.ShouldNotBeNull("could not read a version-shaped token from `claude --version`");

        var baseline = LoadBaseline(observedPath);
        emitsDonePattern.ShouldBe(
            baseline.ClaudeEmitsDonePattern,
            $"Claude's turn-end \" for Ns\" signal changed (baseline={baseline.ClaudeEmitsDonePattern}, " +
            $"observed={emitsDonePattern}). Our turn-end detection depends on it. Review the change, then " +
            $"re-baseline golden/claude-signals.baseline.json deliberately. Observed snapshot: {observedPath}");
    }

    private static Baseline LoadBaseline(string observedPath)
    {
        var baselinePath = Path.Combine(AppContext.BaseDirectory, "golden", "claude-signals.baseline.json");
        if (!File.Exists(baselinePath))
            throw new SkipTestException($"No signal baseline at {baselinePath}. Observed snapshot written to {observedPath} — review it and commit it as the baseline.");

        var baseline = JsonSerializer.Deserialize<Baseline>(
            File.ReadAllText(baselinePath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return baseline ?? throw new InvalidOperationException($"Could not parse baseline at {baselinePath}");
    }

    private static async Task<string?> CaptureVersionAsync()
    {
        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(), "--version");
        await runner.StartAsync(app, args);
        await runner.Exited.WaitAsync(TimeSpan.FromSeconds(60));
        var clean = AnsiStripper.Clean(runner.SnapshotText()) ?? "";
        var m = VersionPattern.Match(clean);
        return m.Success ? m.Value : null;
    }

    private static async Task<(bool emitsDonePattern, bool emitsIdleTitle)> CaptureTurnSignalsAsync()
    {
        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(), "--dangerously-skip-permissions");
        await runner.StartAsync(app, args, cols: 200, rows: 60);

        var ready = await new ClaudeReadyDetector().WaitAsync(runner);
        if (!ready) throw new SkipTestException("real Claude TUI did not reach a ready state");

        runner.ClearLiveBuffer();
        await runner.SendLineAsync("Reply with the single word PONG and nothing else.");
        await runner.WaitForOutputAsync(text => DonePattern.IsMatch(text), TimeSpan.FromMinutes(2));

        var raw = runner.SnapshotText();

        await runner.SendLineAsync("/exit");
        await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(5)));
        await runner.KillAsync(TimeSpan.FromSeconds(2));

        return (DonePattern.IsMatch(raw), raw.Contains("\x1b]0;✳", StringComparison.Ordinal));
    }
}
