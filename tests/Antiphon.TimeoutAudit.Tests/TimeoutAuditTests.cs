using System.Text.Json;
using Antiphon.TimeoutAudit;
using Shouldly;
using TUnit.Core;

namespace Antiphon.TimeoutAudit.Tests;

[Category("Unit")]
[Timeout(30_000)]
public sealed class TimeoutAuditTests
{
    [Test]
    public void Inventory_covers_attributes_helpers_policy_and_checkpoint_rows()
    {
        using var f = new Fixture();
        var report = f.Run();
        report.Inventory.ShouldContain(x => x.Kind == "test" && x.Test == "Tests.Probe.Forty" && x.Seconds == 300);
        report.Inventory.ShouldContain(x => x.Kind == "call" && x.Expression.Contains("FromSeconds(9)"));
        report.Inventory.ShouldContain(x => x.Kind == "policy" && x.Seconds == 3600);
        report.Inventory.ShouldContain(x => x.Kind == "row" && x.Seconds == 900 && x.Filter == "/*/*/Probe/Forty");
        report.Inventory.ShouldContain(x => x.Kind == "tool" && x.Expression.Contains("120000"));
        report.Inventory.ShouldContain(x => x.Kind == "call" && x.Seconds == null && x.Expression.Contains("Unknown"));
        report.Inventory.ShouldNotContain(x => x.Expression.Contains("comment-only"));
    }

    [Test]
    public void Comparable_samples_use_nearest_rank_p95_and_exclude_duplicates_failures_and_load_mixing()
    {
        using var f = new Fixture();
        f.Measure(Enumerable.Range(1, 20).Select(x => (double)x).ToArray());
        f.Measure([999], "loaded", "Passed", 21);
        f.Measure([999], "normal", "Failed", 22);
        var r = f.Run();
        var budget = r.Budgets.Single(x => x.Subject == "Tests.Probe.Forty" && x.Context.Contains("normal"));
        budget.Samples.ShouldBe(20);
        budget.P50.ShouldBe(10.5);
        budget.P95.ShouldBe(19);
        budget.Max.ShouldBe(20);
        budget.RecommendedSeconds.ShouldBe(38);
        budget.Status.ShouldBe("candidate");
        r.Budgets.Single(x => x.Subject == "Tests.Probe.Forty" && x.Context.Contains("loaded")).Status.ShouldBe("provisional");
        r.Observations.ShouldContain(x => x.Outcome == "Failed");
    }

    [Test]
    public void Check_rejects_excess_without_exact_justification_measurement_and_card()
    {
        using var f = new Fixture();
        f.Measure(Enumerable.Repeat(40d, 20).ToArray());
        var r = f.Run();
        var declaration = r.Inventory.Single(x => x.Kind == "test" && x.Test == "Tests.Probe.Forty");
        r.Violations.ShouldContain(x => x.DeclarationId == declaration.Id && x.Reason == "exceeds-2x-p95");
        f.Exceptions(new { declarationId = declaration.Id, justification = "shared host teardown", measurement = "run-1", card = "" });
        f.Run().Violations.ShouldContain(x => x.DeclarationId == declaration.Id);
        f.Exceptions(new { declarationId = declaration.Id, justification = "shared host teardown", measurement = "run-1", card = "CARD-1563" });
        f.Run().Violations.ShouldNotContain(x => x.DeclarationId == declaration.Id);
        // An exception for one declaration never exempts the checkpoint watchdog.
        f.Run().Violations.ShouldContain(x => x.DeclarationId != declaration.Id);
    }

    [Test]
    public void Row_wall_excludes_slot_wait_and_requires_exact_filter_membership()
    {
        using var f = new Fixture();
        f.Measure(Enumerable.Repeat(40d, 20).ToArray());
        var r = f.Run();
        var row = r.Budgets.Single(x => x.Subject == "/*/*/Probe/Forty");
        row.Samples.ShouldBe(20);
        row.P95.ShouldBe(45);
        row.RecommendedSeconds.ShouldBe(90);
        r.Observations.ShouldContain(x => x.Subject == row.Subject && x.SlotWaitSeconds == 600);
        r.Budgets.ShouldNotContain(x => x.Subject == "/*/*/Probe/Other");
    }

    [Test]
    public void Missing_history_and_cost_model_are_provisional_not_measured_p95()
    {
        using var f = new Fixture();
        var r = f.Run();
        r.Budgets.ShouldContain(x => x.Status == "provisional" && x.Samples == 0 && x.P95 == null && x.RecommendedSeconds == null);
        r.Inventory.ShouldContain(x => x.Kind == "cost" && x.Seconds == 120 && x.Test == "Tests.Probe");
        r.Budgets.Where(x => x.Status == "provisional").All(x => x.CollectionCard == "CARD-1563" && x.CollectionPlan.Contains("20") && x.CollectionPlan.Contains("14 days")).ShouldBeTrue();
    }

    [Test]
    public void Bad_or_unattributed_receipts_are_visible_and_cannot_supply_measured_samples()
    {
        using var f = new Fixture();
        File.WriteAllText(Path.Combine(f.Root, "receipts", "broken.trx"), "<broken");
        f.Measure([40]);
        var r = f.Run();
        r.Warnings.ShouldContain(x => x.Contains("broken.trx"));
        r.Budgets.Where(x => x.Samples > 0).All(x => x.Status == "provisional").ShouldBeTrue();
        // Unknown ownership never becomes a cross-platform/load-factor measurement.
        File.WriteAllText(Path.Combine(f.Root, "receipts", "orphan.trx"), File.ReadAllText(Path.Combine(f.Root, "receipts", "r1", "run.trx")));
        f.Run().Warnings.ShouldContain(x => x.Contains("orphan.trx"));
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "c1563-" + Guid.NewGuid().ToString("N"));
        public Fixture()
        {
            Write("tests/Probe.cs", """
                namespace Tests;
                [Timeout(5 * 60_000)]
                class Probe {
                  [Test] public void Forty() { WaitForCondition(TimeSpan.FromSeconds(9)); WaitForCondition(Unknown); }
                  // comment-only Timeout(999999)
                }
                """);
            Write("tools/deadline.ps1", "$timeoutMilliseconds = 120000");
            Write("tests/test-execution-policy.json", """{"watchdogs":{"antiphon":3600000}}""");
            Write("scripts/lib/nightly-class-costs.json", """{"format":"antiphon-nightly-class-costs-v1","classes":{"Tests.Probe":{"nodes":2,"seconds":120,"basis":"measured"}}}""");
            Write("docs/superpowers/plans/probe.md", """
                ### Checkpoints
                | CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
                |---|---|---|---|---|---|---|---|---|
                | CP-1 | S1 | tests/Probe -> bin-probe/ | probe | `/*/*/Probe/Forty` | V-1 | Forty | 1 | 1 |
                """);
            Directory.CreateDirectory(Path.Combine(Root, "receipts"));
        }
        public void Measure(double[] values, string load = "normal", string outcome = "Passed", int offset = 0)
        {
            for (var i = 0; i < values.Length; i++)
            {
                var dir = "receipts/r" + (offset + i + 1);
                Write(dir + "/run.trx", $"""
                    <TestRun><TestDefinitions><UnitTest id="t"><TestMethod className="Tests.Probe" name="Forty" /></UnitTest></TestDefinitions><Results><UnitTestResult testId="t" testName="Forty" duration="{TimeSpan.FromSeconds(values[i]):c}" outcome="{outcome}" /></Results></TestRun>
                    """);
                Write(dir + "/report.json", JsonSerializer.Serialize(new {
                    commit = new string('a', 40), runId = "run-" + (offset + i + 1), host = new { os = "linux", hostId = "host-a", loadAvg = load },
                    rows = new[] { new { id = "CP-1", filter = "/*/*/Probe/Forty", seconds = 645, state = outcome == "Passed" ? "green" : "red", trx = "run.trx", slot = "granted", waitedSeconds = 600, timings = new { testHostWallSeconds = 45d, rowSlotWaitSeconds = 600d } } }
                }));
            }
        }
        public void Exceptions(object exception) => Write("exceptions.json", JsonSerializer.Serialize(new[] { exception }));
        public AuditReport Run() => Audit.Run(new(Root, [Path.Combine(Root, "receipts")], File.Exists(Path.Combine(Root, "exceptions.json")) ? Path.Combine(Root, "exceptions.json") : null));
        private void Write(string relative, string text)
        {
            var path = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
