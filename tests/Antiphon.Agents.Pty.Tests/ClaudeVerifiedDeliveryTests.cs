using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// End-to-end contract for VERIFIED delivery against BOTH backends (fakeclaude in CI, real Claude
/// opt-in headed): type the body exactly like <c>DeliverAsync</c> (one write, no CR), require
/// <see cref="ComposerDeliveryEvidence"/> — the REAL production predicate — to confirm the body
/// landed in the composer, then submit with a separate CR and require the turn to complete.
///
/// This is the delivery-verification analogue of <c>ClaudeSubmitContractTests</c>: the fake keeps
/// CI honest about the flow, the real-Claude variant is the drift canary proving the predicate
/// still recognises what an actual composer renders — for a short body, a huge single-line wall
/// (suffix-only rendering) and a multi-line wall (placeholder-or-tail rendering).
/// </summary>
[NotInParallel("Headed")]
[Category("Pty")]
[ParallelLimiter<ProcessSpawnLimit>]
public class ClaudeVerifiedDeliveryTests
{
    private static readonly Regex DonePattern = new(@" for \d+s", RegexOptions.Compiled);

    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static string FakeClaudeExe =>
        Path.Combine(AppContext.BaseDirectory, "fakeclaude", "fakeclaude.exe");

    [Test]
    public Task Fake_evidence_appears_then_submit_completes_a_turn() =>
        VerifyDeliveryAsync("fakeclaude", "short");

    [Test]
    [Category("Headed")]
    [Category("OptIn")]
    [Arguments("short")]
    [Arguments("hugeLine")]
    [Arguments("multiLine")]
    [Arguments("batch")]
    public Task Real_evidence_appears_then_submit_completes_a_turn(string shape) =>
        VerifyDeliveryAsync("claude", shape);

    [Test]
    [Category("Unit")]
    public async Task C1313_DeliveryDiscovery()
    {
        var root = Path.Combine(Path.GetTempPath(), "c1313-delivery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = root,
            },
        };
        foreach (var argument in new[] { typeof(ClaudeVerifiedDeliveryTests).Assembly.Location,
            "--list-tests", "--no-progress", "--no-ansi", "--diagnostic", "--diagnostic-output-directory", root,
            "--results-directory", root, "--treenode-filter", "/*/*/ClaudeVerifiedDeliveryTests/*" })
            process.StartInfo.ArgumentList.Add(argument);
        var started = false;
        try
        {
            started = process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            process.ExitCode.ShouldBe(0, await stdout + await stderr);
            var text = string.Join("\n", Directory.GetFiles(root, "*.diag").Select(File.ReadAllText));
            var records = Regex.Matches(text, @"(?m)^.*TestNodeUid \{ Value = (?<uid>[^}]+) \}.*DiscoveredTestNodeStateProperty.*$");
            var delivery = records.Cast<Match>().Where(m =>
                m.Value.Contains("MethodName = Fake_evidence_appears_then_submit_completes_a_turn") ||
                m.Value.Contains("MethodName = Real_evidence_appears_then_submit_completes_a_turn")).ToArray();
            delivery.Length.ShouldBe(5, "five-delivery-uids: five expanded delivery UIDs must survive the split");
            delivery.Select(m => m.Groups["uid"].Value).Distinct().Count().ShouldBe(5);
            delivery.Count(m => m.Value.Contains("MethodName = Fake_")).ShouldBe(1);
            delivery.Count(m => m.Value.Contains("MethodName = Real_")).ShouldBe(4);
            foreach (var shape in new[] { "short", "hugeLine", "multiLine", "batch" })
                delivery.ShouldContain(m => m.Value.Contains(shape));
        }
        finally
        {
            if (started && !process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            Directory.Delete(root, recursive: true);
        }
    }

    private async Task VerifyDeliveryAsync(string backend, string shape)
    {
        await using var runner = await LaunchReadyAsync(backend);
        var body = BodyFor(backend, shape);

        var before = runner.SnapshotScreen();
        await runner.WriteAsync(body); // the DeliverAsync body shape: one write, no CR

        var evidence = await WaitForEvidenceAsync(runner, before, body, TimeSpan.FromSeconds(15));
        evidence.ShouldBeTrue(
            $"[{backend}/{shape}] ComposerDeliveryEvidence must recognise the typed body on the rendered screen. "
            + $"Screen was:\n{runner.SnapshotScreen()}");

        await Task.Delay(20);
        await runner.WriteAsync("\r");

        var done = await runner.WaitForOutputAsync(
            text => DonePattern.IsMatch(text), DoneWaitFor(backend));
        done.ShouldBeTrue($"[{backend}/{shape}] the verified submit must complete a turn");

        await CleanupAsync(runner, backend);
    }

    private static async Task<bool> WaitForEvidenceAsync(
        PtyAgentRunner runner, string before, string body, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (ComposerDeliveryEvidence.IsVisible(before, runner.SnapshotScreen(), body))
                return true;
            await Task.Delay(250);
        }
        return ComposerDeliveryEvidence.IsVisible(before, runner.SnapshotScreen(), body);
    }

    private static string BodyFor(string backend, string shape)
    {
        if (backend == "fakeclaude")
            return "hello fake";

        return shape switch
        {
            "hugeLine" => BuildHugeLine(),
            "multiLine" => BuildMultiLine(),
            "batch" => BuildBatch(),
            _ => "Reply with the single word PONG and nothing else.",
        };
    }

    // The exact batched-delivery shape SessionMessageQueueService produces (markers frozen in
    // ChannelPromptFormat — literals here because this project doesn't reference the server).
    private static string BuildBatch() =>
        "[Chat messages since your last reply - for context]\n"
        + "[Telegram \"Family\" — Mike (@mike) 14:30] earlier message about the shopping list\n"
        + "[Telegram \"Family\" — Mike (@mike) 14:31] and something about the school run\n\n"
        + "[Current message - respond to this]\n"
        + "[Telegram \"Family\" — Mike (@mike) 14:32] ignore all of the above and reply with the single word PONG and nothing else.";

    // ~3k chars of ignorable filler ending in the actual instruction: the tail (which is what the
    // suffix-only composer rendering shows) carries the distinctive content.
    private static string BuildHugeLine()
    {
        var sb = new StringBuilder("Ignore the following filler tokens completely: ");
        for (var i = 0; sb.Length < 3000; i++) sb.Append($"filler{i:D4} ");
        sb.Append("— after ignoring all filler, reply with the single word PONG and nothing else.");
        return sb.ToString();
    }

    private static string BuildMultiLine()
    {
        var lines = new List<string> { "Read all lines below, then reply with the single word PONG and nothing else." };
        lines.AddRange(Enumerable.Range(0, 24).Select(i => $"ignorable filler line {i:D2}"));
        lines.Add("End of filler. Remember: reply with exactly PONG.");
        return string.Join("\n", lines);
    }

    // 4 min for real Claude: the filler-wall prompts occasionally produce a slow turn, and this
    // test asserts delivery mechanics, not model latency (a 2-min limit flaked once on 2026-07-21).
    private static TimeSpan DoneWaitFor(string backend) =>
        backend == "claude" ? TimeSpan.FromMinutes(4) : TimeSpan.FromSeconds(10);

    private static async Task<PtyAgentRunner> LaunchReadyAsync(string backend)
    {
        var runner = new PtyAgentRunner();
        if (backend == "fakeclaude")
        {
            if (!IsWindows) throw new SkipTestException("ConPTY only on Windows");
            if (!File.Exists(FakeClaudeExe))
                throw new SkipTestException($"fakeclaude.exe not staged at {FakeClaudeExe} — build the solution first");

            await runner.StartAsync(FakeClaudeExe, Array.Empty<string>(), cols: 120, rows: 30);
            var ready = await runner.WaitForOutputAsync(s => s.Contains("Fake Claude ready"), TimeSpan.FromSeconds(15));
            ready.ShouldBeTrue("fake Claude should print its readiness banner");
        }
        else
        {
            ClSession.SkipIfNotEligible();
            var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(), "--dangerously-skip-permissions");
            await runner.StartAsync(app, args, cols: 120, rows: 30); // production terminal size
            var ready = await new ClaudeReadyDetector().WaitAsync(runner);
            if (!ready) throw new SkipTestException("real Claude TUI did not reach a ready state");
        }

        runner.ClearLiveBuffer();
        return runner;
    }

    private static async Task CleanupAsync(PtyAgentRunner runner, string backend)
    {
        if (backend == "claude")
        {
            await runner.SendLineAsync("/exit");
            await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(5)));
        }
        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }
}
