using System.Diagnostics;
using Antiphon.Tests.Scripts;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

// CARD-1516 D-5: the effort dialog peer publishes only coherent semantic snapshots. The scripted-key driver is the staged
// script harness host's claude-effort-trace-contract (ClaudeEffortDialog), a dotnet child; until unit 35 of the 2026-11-22
// plan it was Agents/Fixtures/claude-effort-trace-contract.ps1 under the script harness, with the same four rows.
[Category("Integration"), ParallelLimiter<ProcessSpawnLimit>]
public class ClaudeEffortFixtureTraceTests
{
    [Test]
    public async Task Trace_publishes_only_coherent_semantic_snapshots()
    {
        var failures = new List<Exception>();
        try
        {
            var (exit, output) = await RunContractAsync();
            exit.ShouldBe(0, output);
            var lines = output.ReplaceLineEndings("\n").Split('\n');
            lines.Where(line => line.StartsWith("PASS C1516 ", StringComparison.Ordinal)).ShouldBe(
                ["PASS C1516 ordinary", "PASS C1516 enter", "PASS C1516 token", "PASS C1516 clear"], output);
            lines.ShouldNotContain(line => line.StartsWith("FAIL ", StringComparison.Ordinal), output);
            lines.ShouldContain("C487: 4 passed, 0 failed, 4 rows", output);
            lines.ShouldContain("C487 HARNESS EXIT CODE: 0", output);
        }
        catch (Exception ex) { failures.Add(ex); }
        try
        {
            var elapsed = TimeSpan.Zero;
            var reads = 0;
            var clear = "{\"composer\":\"\",\"phase\":\"clear\"}";
            var result = await EffortFixtureCompletion.WaitAsync(
                () => elapsed >= TimeSpan.FromMilliseconds(75) ? "> \n? for shortcuts" : "> token\n? for shortcuts",
                () => { reads++; return Task.FromResult(elapsed >= TimeSpan.FromMilliseconds(50) ? clear : "{\"composer\":\"token\",\"phase\":\"enter\"}"); },
                () => elapsed, (span, _) => { elapsed += span; return Task.CompletedTask; }, CancellationToken.None);
            result.ShouldBe(clear); reads.ShouldBe(4, "poll until both independent observations agree");
            elapsed.ShouldBe(TimeSpan.FromMilliseconds(75));
            elapsed = TimeSpan.FromMilliseconds(15975);
            await Should.ThrowAsync<TimeoutException>(() => EffortFixtureCompletion.WaitAsync(() => "> token", () => Task.FromResult(clear),
                () => elapsed, (span, _) => { elapsed += span; return Task.CompletedTask; }, CancellationToken.None));
            elapsed.ShouldBe(TimeSpan.FromSeconds(16), "original watchdog remainder is never reset");
        }
        catch (Exception ex) { failures.Add(ex); }
        if (failures.Count != 0) throw new AggregateException(failures);
    }

    // dotnet <host> claude-effort-trace-contract, both streams read, 60 s bound (the tree is killed past it).
    private static async Task<(int Exit, string Output)> RunContractAsync()
    {
        File.Exists(ScriptHarnessProcessFixture.HelperPath).ShouldBeTrue("the script harness host must be staged by the build");
        var start = new ProcessStartInfo(ScriptHarnessProcessFixture.ResolveDotnet())
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        foreach (var argument in new[] { ScriptHarnessProcessFixture.HelperPath, "claude-effort-trace-contract" }) start.ArgumentList.Add(argument);
        using var child = Process.Start(start) ?? throw new InvalidOperationException("The trace contract child did not start.");
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await child.WaitForExitAsync(bound.Token); }
        catch (OperationCanceledException) { child.Kill(true); await child.WaitForExitAsync(); throw new TimeoutException("trace contract child exceeded 60 s"); }
        return (child.ExitCode, await stdout + await stderr);
    }
}
