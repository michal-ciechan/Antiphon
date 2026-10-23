using Antiphon.Tests.Scripts;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

[Category("Integration"), ParallelLimiter<ProcessSpawnLimit>]
public class ClaudeEffortFixtureTraceTests
{
    [Test]
    public async Task Trace_publishes_only_coherent_semantic_snapshots()
    {
        var failures = new List<Exception>();
        try
        {
            await ScriptHarness.RunHarnessCaseAsync("claude-effort-trace-contract.ps1", "C1516", "trace", 4,
                ["C1516 ordinary", "C1516 enter", "C1516 token", "C1516 clear"],
                ScriptHarnessOptions.Default with { ScriptPath = Path.Combine(AppContext.BaseDirectory, "Agents", "Fixtures", "claude-effort-trace-contract.ps1") },
                CancellationToken.None);
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
}
