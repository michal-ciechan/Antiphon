using System.Text.Json;
using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TestTimingLifecycleTests
{
    [Test]
    [Timeout(180_000)]
    public async Task C1514T_NativeLifecycle(CancellationToken cancellationToken)
    {
        File.Exists(C1514Support.StagedProbe()).ShouldBeTrue("timing probe must be staged by the parent build");
        var offRoot = C1514Support.FreshRoot();
        await C1514Support.LaunchProbe(offRoot, enabled: false, cancellationToken);
        var onRoot = C1514Support.FreshRoot();
        await C1514Support.LaunchProbe(onRoot, enabled: true, cancellationToken);

        var off = TrxReport.Parse(Path.Combine(offRoot, "run.trx"));
        off.Ok.ShouldBeTrue(off.Error ?? "off trx");
        off.Executed.ShouldBe(7, "staged child executes");
        ShardCount(offRoot).ShouldBe(0, "disabled-writes-nothing");

        var on = TrxReport.Parse(Path.Combine(onRoot, "run.trx"));
        on.Ok.ShouldBeTrue(on.Error ?? "on trx");
        on.Executed.ShouldBe(7, "staged child executes");
        on.Passed.ShouldBe(5, "staged child executes");
        on.Failed.ShouldBe(2, "staged child executes");
        on.Skipped.ShouldBe(0, "staged child executes");

        var shard = Directory.GetFiles(Path.Combine(onRoot, "timing"), "cases-*.jsonl");
        shard.Length.ShouldBe(1, "missing complete timing rows");
        var completes = Completes(shard[0]);
        completes.Count.ShouldBe(7, "missing complete timing rows");
        using (var header = JsonDocument.Parse(File.ReadLines(shard[0]).First()))
            header.RootElement.GetProperty("assembly").GetString().ShouldBe("Antiphon.TestTiming.Probe", "stable-assembly-name");
        var bytes = File.ReadAllText(shard[0]);
        bytes.ShouldNotContain("CANARY-secret-sk-probe-should-not-leak");
        bytes.ShouldContain("\"recordKind\":\"health\"", Case.Sensitive);
        bytes.ShouldContain("\"failure\":null", Case.Sensitive);

        foreach (var result in on.Results)
        {
            var ids = C1514Support.NativeIds(result.StdOut);
            ids.Count.ShouldBe(1, "missing complete timing rows");
            ids[0].ShouldNotBe(result.TestId, "missing complete timing rows");
            var match = completes.Single(row => row.GetProperty("testId").GetString() == ids[0]);
            match.GetProperty("outcome").GetString().ShouldBe(result.Outcome, "missing complete timing rows");
        }

        Count(completes, ".Parameterized").ShouldBe(2);
        Count(completes, ".Repeated").ShouldBe(2);
        Count(completes, ".Ordinary.").ShouldBe(1);
        var fails = One(completes, ".Fails.");
        fails.GetProperty("outcome").GetString().ShouldBe("Failed");
        fails.GetRawText().ShouldContain("fixture-setup", Case.Sensitive);
        fails.GetRawText().ShouldContain("fixture-teardown", Case.Sensitive);
        var cancels = One(completes, ".Cancels");
        cancels.GetProperty("outcome").GetString().ShouldBe("Failed", "after-hook-in-envelope");
        cancels.GetRawText().ShouldContain("fixture-teardown", Case.Sensitive, "after-hook-in-envelope");
        One(completes, ".Ordinary.").GetRawText().ShouldContain("our-code", Case.Sensitive);
    }

    private static int ShardCount(string root)
    {
        var count = Directory.GetFiles(root, "cases-*.jsonl", SearchOption.AllDirectories).Length;
        return count;
    }

    private static List<JsonElement> Completes(string path)
    {
        var rows = new List<JsonElement>();
        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0 || !line.Contains("\"recordKind\":\"complete\"", StringComparison.Ordinal))
                continue;
            using var doc = JsonDocument.Parse(line);
            rows.Add(doc.RootElement.Clone());
        }

        return rows;
    }

    private static int Count(List<JsonElement> rows, string token) =>
        rows.Count(row => (row.GetProperty("testId").GetString() ?? "").Contains(token, StringComparison.Ordinal));

    private static JsonElement One(List<JsonElement> rows, string token) =>
        rows.Single(row => (row.GetProperty("testId").GetString() ?? "").Contains(token, StringComparison.Ordinal));
}
