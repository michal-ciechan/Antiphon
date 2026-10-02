using System.Text.Json;
using Antiphon.Checkpoints.Coverage;

namespace Antiphon.Tests.Checkpoints;
internal static class PlanCoverageFixture
{
    public static string Plan(string row = "| V-1 | `Demo.Check` | label `target-label` |", string pc = "") =>
        "## Verification design\n\n| ID | Method | Assertions |\n|---|---|---|\n" + row + "\n" + pc + "\n"
        + "### Checkpoints\n\n| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |\n"
        + "|---|---|---|---|---|---|---|---:|---:|\n"
        + "| CP-1 | S1 | `tests/Sample -> bin-demo/` | demo | `/*/*/Demo*/*` | V-1 | all | 1 | 1 |\n";
    public static PlanCoverageReport Analyze(string source, string? plan = null, string? checklist = null) =>
        new PlanCoverageAnalyzer().Analyze("plan.md", plan ?? Plan(), [new("Demo.cs", source)], checklist);
    public static string Checklist(params object[] items) => JsonSerializer.Serialize(new { version = 1, items });
    public static object Item(string kind, string name, string id = "V-1", string test = "Demo.Check", int line = 5, string? maps = null) =>
        maps is null ? new { id, test, kind, name, planLine = line } : (object)new { id, test, kind, name, planLine = line, maps };
    public static string Raw(string name) => File.ReadAllText(CheckpointFixtures.Fixture("PlanCoverage/" + name));
    public static string Project(string name, string ranges)
    {
        using var doc = JsonDocument.Parse(Raw("provenance.json"));
        var kept = doc.RootElement.GetProperty(ranges).EnumerateArray().Select(r => (r[0].GetInt32(), r[1].GetInt32())).ToArray();
        return string.Join('\n', Raw(name).Split('\n').Select((s, i) => kept.Any(r => i + 1 >= r.Item1 && i + 1 <= r.Item2) ? s : ""));
    }
    public static PlanCoverageReport C866(bool fixedSource, bool mapped) => new PlanCoverageAnalyzer().Analyze("c866-plan.md.txt",
        Project("c866-plan.md.txt", "c866Projection"), [new("c866-tests.cs.txt", Raw(fixedSource ? "c866-fixed-tests.cs.txt" : "c866-tests.cs.txt"))], mapped ? Raw("c866-checklist.json") : null);
    public static (string Plan, string Source) WriteWorld(string root, string source = "class Demo { void Check() { value.ShouldBe(1, \"target-label\"); } }")
    {
        var dir = Path.Combine(root, "tests", "Sample"); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "Demo.cs"); File.WriteAllText(path, source);
        var plan = Path.Combine(root, "plan.md"); File.WriteAllText(plan, Plan());
        return (plan, path);
    }
}
