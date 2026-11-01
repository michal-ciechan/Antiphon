using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Agents;

/// <summary>Native activation qualification, deliberately outside the portable checkpoint rows.</summary>
[Category("Integration"), NotInParallel("Headed"), ParallelLimiter<ProcessSpawnLimit>]
public class ClaudeDialogWatchdogCanaryTests
{
    [Test]
    public async Task C680_LiveNudgeRefusesPermissionAndQuestionDialogs()
    {
        if (Environment.GetEnvironmentVariable("ANTIPHON_HEADED_TESTS") != "1")
            throw new SkipTestException("Native watchdog qualification is explicitly opt-in.");
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            throw new SkipTestException("Only modern ConPTY and UnixPty are qualified by this method.");
        var exe = Resolve(OperatingSystem.IsWindows() ? "claude.exe" : "claude");
        Resolve(OperatingSystem.IsWindows() ? "node.exe" : "node");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        var ct = deadline.Token;
        using var versionProcess = Process.Start(new ProcessStartInfo(exe, "--version")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
        var versionText = await versionProcess.StandardOutput.ReadToEndAsync(ct);
        await versionProcess.WaitForExitAsync(ct);
        var version = Regex.Match(versionText, @"\d+\.\d+\.\d+").Value;
        version.ShouldNotBeEmpty();
        var debugRoot = Environment.GetEnvironmentVariable("ANTIPHON_C680_DEBUG_ROOT");
        var model = string.IsNullOrWhiteSpace(debugRoot) ? "haiku"
            : Environment.GetEnvironmentVariable("ANTIPHON_C680_DEBUG_MODEL") ?? "haiku";
        if (model is not ("haiku" or "sonnet" or "opus"))
            throw new InvalidOperationException("C680 diagnostic model must be haiku, sonnet or opus.");

        foreach (var question in new[] { false, true })
        {
            var root = Directory.CreateTempSubdirectory("antiphon-c680-native-").FullName;
            var cwd = Directory.CreateDirectory(Path.Combine(root, "workspace")).FullName;
            var marker = Path.Combine(cwd, "permission-executed.txt");
            var seeded = false;
            try
            {
                await File.WriteAllTextAsync(Path.Combine(cwd, "permission-probe.cjs"),
                    "require('fs').writeFileSync('permission-executed.txt','unexpected permission grant');", ct);
                var settings = Path.Combine(root, "settings.json");
                await File.WriteAllTextAsync(settings, JsonSerializer.Serialize(new
                {
                    remoteControlAtStartup = false, disableAllHooks = true,
                    permissions = new { ask = new[] { "Bash(node permission-probe.cjs)" } }
                }), ct);
                var trust = ClaudeProjectTrust.Seed(cwd);
                seeded = trust.Outcome == ClaudeProjectTrustOutcome.Seeded;
                (trust.Outcome is ClaudeProjectTrustOutcome.Seeded or ClaudeProjectTrustOutcome.AlreadyTrusted).ShouldBeTrue();
                await using var runner = new DirectSessionRunnerClient(Path.Combine(root, "sessions"),
                    ptyBackend: "modern", claudeTranscript: true);
                await using var f = await ExpectationDeliveryFixture.CreateAsync(observable: false, runner: runner);
                f.Harness.Runtime.TryRemove(f.SessionId, out var fake).ShouldBeTrue();
                if (fake is not null) await fake.DisposeAsync();
                var generation = await f.GenerationAsync();
                var debug = new ClaudeDialogCanaryDiagnostics(debugRoot, root, cwd, f.SessionId,
                    version, model, question ? "AskUserQuestion" : "Bash");
                var env = new Dictionary<string, string>
                {
                    ["CLAUDE_CODE_CHILD_SESSION"] = "", ["CLAUDE_CODE_SESSION_ID"] = "",
                    ["CLAUDE_CODE_BRIDGE_SESSION_ID"] = "", ["CLAUDECODE"] = "",
                    ["ANTIPHON_TASK_TOKEN"] = "", ["ANTIPHON_TASK_ID"] = "",
                    ["ANTIPHON_API"] = "", ["ANTIPHON_ORCHESTRATOR_INSTRUCTIONS"] = ""
                };
                await runner.StartAsync(f.SessionId, new AgentLaunchSpec("c680-native", AgentKind.ClaudeCode,
                    exe, ["--session-id", f.SessionId.ToString("D"), "--model", model, "--permission-mode", "default",
                        "--setting-sources", "", "--settings", settings, "--strict-mcp-config", "--tools", "Bash,AskUserQuestion"],
                    env, cwd, 120, 40, SessionId: f.SessionId, AcceptedStartedAt: generation), ct);
                runner.ActualBackend.ShouldBe(OperatingSystem.IsWindows() ? PtyBackend.ModernConPty : PtyBackend.UnixPty);
                string? previous = null;
                await WaitAsync(async () =>
                {
                    var screen = (await runner.GetSnapshotAsync(f.SessionId, ct)).RenderedScreen;
                    var empty = ClaudeScreen.TryReadComposer(screen, out var content) && ClaudeScreen.ComposerContentIsEmpty(content)
                        && ClaudeDialogObservation.Observe(screen).State == ClaudeDialogState.Clear;
                    var settled = empty && previous is not null && ClaudeScreen.IsSettled(previous, screen);
                    debug.Composer(screen, empty, settled);
                    previous = empty ? screen : null;
                    return settled;
                }, TimeSpan.FromSeconds(30), "C680 submission precondition failed: no empty settled composer.", ct);
                var probe = question
                    ? "Use AskUserQuestion now to ask which color I prefer, with Blue and Green as choices. Wait for my answer; do not choose for me."
                    : "Run exactly node permission-probe.cjs using Bash now. Do not edit files or run any other command. Wait for my permission.";
                var tool = question ? "AskUserQuestion" : "Bash";
                await SubmitProbeAsync(runner, f.SessionId, probe, tool, debug, ct);
                await WaitAsync(async () =>
                {
                    var snapshot = await runner.GetSnapshotAsync(f.SessionId, ct);
                    var transcript = await runner.GetTranscriptAsync(f.SessionId, ct);
                    var blocked = snapshot.Dialog?.State == ProviderDialogState.Blocked;
                    var submitted = transcript.Entries.Any(e => e.Kind == TranscriptKinds.UserPrompt
                        && PromptSubmissionMatch.IsCompleteIn(probe, e.Text));
                    var called = transcript.Entries.Any(e => e.Kind == TranscriptKinds.ToolCall && e.ToolName == tool);
                    debug.Observe(snapshot, transcript, blocked, submitted, called);
                    return blocked && submitted && called;
                }, TimeSpan.FromMinutes(2),
                    $"C680 {tool} dialog precondition failed after confirmed prompt submission: the blocked dialog and tool call were not observed.", ct);
                File.Exists(marker).ShouldBeFalse();
                var before = await runner.GetSnapshotAsync(f.SessionId, ct);
                var pendingTool = (await runner.GetTranscriptAsync(f.SessionId, ct)).Entries.Last(e =>
                    e.Kind == TranscriptKinds.ToolCall && e.ToolName == tool);
                var writes = runner.BackendWrites.Count;
                var nudge = await f.NudgeAsync();
                var result = await f.DeliverAsync(nudge.Id);
                result.Outcome.ShouldBe(ExpectationSendOutcome.Refused);
                result.Reason.ShouldContain("dialog");
                runner.BackendWrites.Count.ShouldBe(writes, "the actual nudge service must write zero body/Enter/Esc bytes");
                var after = await runner.GetSnapshotAsync(f.SessionId, ct);
                after.Dialog!.InputRevision.ShouldBe(before.Dialog!.InputRevision);
                after.Dialog.State.ShouldBe(ProviderDialogState.Blocked);
                ClaudeScreen.IsSettled(before.RenderedScreen, after.RenderedScreen).ShouldBeTrue();
                var tail = await runner.GetTranscriptAsync(f.SessionId, ct);
                tail.Entries.ShouldNotContain(e => e.Kind == TranscriptKinds.UserPrompt
                    && e.Text != null && e.Text.Contains(nudge.Body));
                tail.Entries.ShouldNotContain(e => e.Kind == TranscriptKinds.ToolResult && e.ToolUseId == pendingTool.ToolUseId);
                File.Exists(marker).ShouldBeFalse();
                (await f.ReloadAsync(nudge.Id)).AttemptStartedAt.ShouldBeNull();
                Console.WriteLine($"C680 native cli={version} backend={runner.ActualBackend} case={tool} inputRevision={after.Dialog.InputRevision} backendDelta=0 refused=true");
            }
            finally
            {
                if (seeded) ClaudeProjectTrust.Remove(cwd);
                // DirectSessionRunnerClient joins only its recorded owned children before this scope exits.
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }

    private static async Task SubmitProbeAsync(DirectSessionRunnerClient runner, Guid sessionId,
        string probe, string tool, ClaudeDialogCanaryDiagnostics debug, CancellationToken ct)
    {
        // Explicit fixture input to this owned child; watchdog delivery still uses its real guard.
        var initial = await runner.GetSnapshotAsync(sessionId, ct);
        if (ClaudeDialogObservation.Observe(initial.RenderedScreen).State != ClaudeDialogState.Clear)
            throw new InvalidOperationException($"C680 {tool} submission precondition failed: initial screen is not clear.");
        var floor = (await runner.GetTranscriptAsync(sessionId, ct)).LastSequence;
        var body = PtyInputEncoding.NormalizeBody(probe);
        // These single-line probes also need paste framing to avoid Claude's typed-paste heuristic.
        await runner.SendInputAsync(sessionId, PtyInputEncoding.PasteStart + body + PtyInputEncoding.PasteEnd, ct);
        string? previous = null;
        await Task.Delay(500, ct);
        await WaitAsync(async () =>
        {
            var screen = (await runner.GetSnapshotAsync(sessionId, ct)).RenderedScreen;
            // Fixture-only recognition of 2.1.312's footer; no change to product admission.
            var fixtureScreen = screen.Replace("⏸ manual mode on", "? for shortcuts", StringComparison.Ordinal);
            var complete = ClaudeScreen.TryReadComposer(fixtureScreen, out var draft)
                && PromptSubmissionMatch.IsCompleteIn(body, draft);
            var settled = complete && previous is not null && ClaudeScreen.IsSettled(previous, screen);
            debug.Draft(screen, complete, settled);
            previous = complete ? screen : null;
            return settled;
        }, TimeSpan.FromSeconds(10),
            $"C680 {tool} submission precondition failed: no complete settled draft; Enter was not sent.", ct);
        await runner.SendInputAsync(sessionId, "\r", ct);
        await WaitAsync(async () =>
        {
            var transcript = await runner.GetTranscriptAsync(sessionId, ct);
            return transcript.Entries.Any(e => e.Sequence > floor && e.Kind == TranscriptKinds.UserPrompt
                && PromptSubmissionMatch.IsCompleteIn(body, e.Text));
        }, TimeSpan.FromSeconds(30),
            $"C680 {tool} submission precondition failed: Enter was sent but no complete transcript UserPrompt receipt arrived; prompt submission is unconfirmed, watchdog assertions were not reached.", ct);
        Console.WriteLine($"C680 case={tool} submission=confirmed evidence=UserPrompt");
    }

    private static async Task WaitAsync(Func<Task<bool>> predicate, TimeSpan budget, string failure, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < budget)
        {
            if (await predicate()) return;
            await Task.Delay(500, ct);
        }
        throw new System.TimeoutException(failure);
    }

    private static string Resolve(string name) => (Environment.GetEnvironmentVariable("PATH") ?? "")
        .Split(Path.PathSeparator).Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => Path.Combine(d, name))
        .FirstOrDefault(File.Exists) ?? throw new SkipTestException($"Native qualification requires {name}.");
}
