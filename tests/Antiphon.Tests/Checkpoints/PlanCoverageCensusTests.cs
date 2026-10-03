using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.Checkpoints.Coverage;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class PlanCoverageCensusTests : CheckpointTestBase
{
    private const string Two = "namespace One;\nclass A {\n[Test] void First() {}\n[Test] void Second() {}\nvoid Helper() {}\n}\n";
    private sealed record World(string Root, string Plan, string Source, string Checklist);
    private World Make(string source = Two, string filter = "/*/*/A/*", string[]? roster = null, string? flag = "true", bool inline = false, string? rows = null)
    {
        var root = TempDir(); Directory.CreateDirectory(Path.Combine(root, "tests/Sample"));
        var world = new World(root, Path.Combine(root, "plan.md"), Path.Combine(root, "tests/Sample/A.cs"), Path.Combine(root, "checklist.json"));
        File.WriteAllText(world.Source, source);
        File.WriteAllText(world.Plan, Plan(filter, rows));
        SetRoster(world, roster ?? ["A.First", "A.Second"], flag, inline);
        return world;
    }
    private static string Plan(string filter, string? rows = null) =>
        "## Verification design\n\n| ID | Test | Assertions |\n|---|---|---|\n| V-1 | Coverage | |\n| R-1 | Regression | |\n| PC-1 | Control | |\n\n### Checkpoints\n\n| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |\n|---|---|---|---|---|---|---|---:|---:|\n"
        + (rows ?? $"| CP-1 | S1 | `tests/Sample -> bin-demo/` | demo | `{filter.Replace("|", "\\|")}` | V-1 | all | 1 | 1 |") + "\n";
    private static object Item(string test, string id = "V-1", string kind = "method", string? name = null) =>
        new { id, test, kind, name = name ?? test, planLine = id == "R-1" ? 6 : id == "PC-1" ? 7 : 5 };
    private static string Checklist(IEnumerable<object> items, string? flag = "true") =>
        "{\"version\":1," + (flag is null ? "" : "\"selectedClassCensus\":" + flag + ",") + "\"items\":" + JsonSerializer.Serialize(items) + "}";
    private static void SetRoster(World w, string[] roster, string? flag = "true", bool inline = false)
    {
        var json = Checklist(roster.Select(s => Item(s)), flag);
        if (inline) File.AppendAllText(w.Plan, "\n```plan-coverage-v1\n" + json + "\n```\n");
        else File.WriteAllText(w.Checklist, json);
    }
    private static PlanCoverageReport Run(World w, bool inline = false, params string[] extra)
    {
        var output = new StringWriter();
        new CoverageCommand().Run(w.Root, w.Plan, new[] { w.Source }.Concat(extra).ToArray(), "json", inline ? null : w.Checklist, output);
        return JsonSerializer.Deserialize<PlanCoverageReport>(output.ToString(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
    private static string[] Census(PlanCoverageReport r) => r.Diagnostics.Where(d => d.Code.StartsWith("CLASS_CENSUS", StringComparison.Ordinal)).Select(d => d.Test + ":" + d.Detail).ToArray();
    private static string Add(World w, string path, string source)
    {
        var full = Path.Combine(w.Root, path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); File.WriteAllText(full, source); return full;
    }
    private static string[] Snapshot(string root) => Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
        .Select(p => Path.GetRelativePath(root, p) + (Directory.Exists(p) ? ":directory" : ":file:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))))).ToArray();

    [Test]
    public void checklist_flag_accepts_true_false_and_absence()
    {
        foreach (var inline in new[] { false, true })
        {
            foreach (var filter in new[] { "/*/*/A/*", "/*/*/A/First", "/*/*/*/*[Category=Unit]" })
            {
                Census(Run(Make(filter: filter, roster: ["A.First"], flag: null, inline: inline), inline)).ShouldBeEmpty("c1005-pc-1");
                Census(Run(Make(filter: filter, roster: ["A.First"], flag: "false", inline: inline), inline)).ShouldBeEmpty("c1005-pc-2");
            }
        }
        Census(Run(Make(roster: ["A.First"], inline: true), true)).ShouldBe(["One.A.Second:selected method absent from checklist"], "c1005-pc-3");
        Census(Run(Make(roster: ["A.First"]))).ShouldBe(["One.A.Second:selected method absent from checklist"], "c1005-pc-4");
        var prose = Make(roster: ["A.First"], flag: null);
        File.WriteAllText(prose.Plan, File.ReadAllText(prose.Plan).Replace("Coverage", "All 1 class-qualified methods in the checklist are required."));
        Census(Run(prose)).ShouldBeEmpty("c1005-flag-count-prose");
    }

    [Test]
    public void checklist_flag_rejects_invalid_or_conflicting_input()
    {
        foreach (var inline in new[] { false, true })
        foreach (var value in new[] { "null", "\"true\"", "1", "[]", "{}" })
        {
            var r = Run(Make(flag: value, inline: inline), inline);
            (r.Invalid, r.ExitCode, r.Diagnostics.Any(d => d.Code == "CHECKLIST_INVALID")).ShouldBe((true, 2, true), "c1005-pc-5");
        }
        foreach (var tail in new[] { "true,\"selectedClassCensus\":false", "false,\"selectedClassCensus\":true", "true,\"selectedClassCensus\":true" })
        foreach (var inline in new[] { false, true })
        {
            var r = Run(Make(flag: tail, inline: inline), inline);
            (r.Invalid, r.ExitCode, r.Diagnostics.Any(d => d.Code == "CHECKLIST_INVALID")).ShouldBe((true, 2, true), "c1005-pc-6");
        }
        foreach (var inline in new[] { false, true })
        {
            var unknown = Make(flag: "true,\"selectedClassCensusExtra\":true", inline: inline);
            var u = Run(unknown, inline);
            (u.Invalid, u.ExitCode, u.Diagnostics.Any(d => d.Code == "CHECKLIST_INVALID")).ShouldBe((true, 2, true), "c1005-pc-7");
        }
        foreach (var external in new[] { "true", "false" })
        {
            var w = Make(inline: true); File.WriteAllText(w.Checklist, Checklist([Item("A.First"), Item("A.Second")], external));
            var conflict = Run(w);
            (conflict.Invalid, conflict.ExitCode, conflict.Diagnostics.Any(d => d.Code == "CHECKLIST_INVALID")).ShouldBe((true, 2, true), "c1005-pc-8");
        }
        var twice = Make(inline: true); File.AppendAllText(twice.Plan, "\n```plan-coverage-v1\n" + Checklist([Item("A.First"), Item("A.Second")]) + "\n```\n");
        var two = Run(twice, true);
        (two.Invalid, two.ExitCode, two.Diagnostics.Any(d => d.Code == "CHECKLIST_INVALID")).ShouldBe((true, 2, true), "c1005-pc-9");
        foreach (var inline in new[] { false, true })
        {
            var version = Make(inline: inline); var path = inline ? version.Plan : version.Checklist;
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"version\":1", "\"version\":2"));
            var v = Run(version, inline);
            (v.Invalid, v.ExitCode, v.Diagnostics.Any(d => d.Code == "CHECKLIST_INVALID")).ShouldBe((true, 2, true), "c1005-pc-10");
        }
        Run(Make()).ExitCode.ShouldBe(0, "c1005-valid-boolean-control");
    }

    [Test]
    public void census_matches_exact_checklist_roster()
    {
        var matched = Run(Make());
        matched.Obligations.Select(o => (o.Id, o.Kind, o.Name)).ShouldBe([("V-1", "method", "A.First"), ("V-1", "method", "A.Second")], "c1005-pc-72");
        matched.Diagnostics.ShouldBeEmpty("c1005-pc-98");
        matched.ExitCode.ShouldBe(0, "c1005-exact-roster");
        Census(Run(Make(roster: []))).ShouldBe(["One.A.First:selected method absent from checklist", "One.A.Second:selected method absent from checklist"], "c1005-pc-13");
        Run(Make(source: "namespace One; class A {}", roster: [])).ExitCode.ShouldBe(0, "c1005-empty-both");
    }

    [Test]
    public void census_reports_new_or_omitted_selected_method()
    {
        var w = Make(source: Two.Replace("void Helper() {}", "[Test] void Third() {}")); var r = Run(w);
        Census(r).ShouldBe(["One.A.Third:selected method absent from checklist"], "c1005-pc-11");
        var d = r.Diagnostics.Single(x => x.Code == "CLASS_CENSUS_MISMATCH");
        (r.ExitCode, d.Test, d.TestPath, d.TestLine, d.Id, d.PlanLine, d.PlanColumn, d.Detail)
            .ShouldBe((1, "One.A.Third", "tests/Sample/A.cs", 5, "CP-1", 13, 3, "selected method absent from checklist"), "c1005-pc-59");
        SetRoster(w, ["A.First", "A.Second", "A.Third"]); Run(w).ExitCode.ShouldBe(0, "c1005-third-restored");
        var omitted = Make(roster: ["A.First"]);
        Census(Run(omitted)).ShouldBe(["One.A.Second:selected method absent from checklist"], "c1005-item-deletion");
        SetRoster(omitted, ["A.First", "A.Second"]); Run(omitted).ExitCode.ShouldBe(0, "c1005-item-restored");
    }

    [Test]
    public void census_rejects_unselected_or_non_test_roster_methods()
    {
        var w = Make(); var extra = Add(w, "tests/Other/Extra.cs", "namespace Other; class B { [Test] void Extra() {} }");
        File.WriteAllText(w.Checklist, Checklist([Item("A.First"), Item("A.Second"), Item("Other.B.Extra", "R-1")]));
        var r = Run(w, false, extra);
        Census(r).ShouldBe(["Other.B.Extra:checklist method not selected"], "c1005-pc-12");
        var d = r.Diagnostics.Single();
        (r.ExitCode, d.Id, d.PlanLine, d.PlanColumn, d.Test, d.TestPath, d.TestLine, d.Detail)
            .ShouldBe((1, "R-1", 6, 1, "Other.B.Extra", "tests/Other/Extra.cs", 1, "checklist method not selected"), "c1005-pc-61");
        Census(Run(Make(roster: ["A.First", "A.Second", "A.Helper"]))).ShouldBe(["One.A.Helper:checklist method not selected"], "c1005-extra-helper");
        var missing = Run(Make(roster: ["A.First", "A.Second", "A.Missing"]));
        (missing.Invalid, missing.ExitCode, missing.Diagnostics.Any(x => x.Code == "MISSING_METHOD")).ShouldBe((true, 2, true), "c1005-pc-21");
        var ambiguous = Run(Make(source: Two + "namespace Two { class A { [Test] void First() {} } }", roster: ["A.First", "One.A.Second"]));
        (ambiguous.Invalid, ambiguous.ExitCode, ambiguous.Diagnostics.Any(x => x.Code == "METHOD_UNMAPPED")).ShouldBe((true, 2, true), "c1005-pc-22");
        Census(Run(Make(source: "namespace One; class A { void Helper() {} }", roster: ["A.Helper"]))).ShouldBe(["One.A.Helper:checklist method not selected"], "c1005-extra-empty-selected");
    }

    [Test]
    public void census_compares_resolved_identities_not_counts()
    {
        Census(Run(Make(roster: ["A.First", "A.Helper"]))).ShouldBe(["One.A.Helper:checklist method not selected", "One.A.Second:selected method absent from checklist"], "c1005-pc-14");
        var prose = Make(roster: ["A.First"]); File.WriteAllText(prose.Plan, File.ReadAllText(prose.Plan).Replace("Coverage", "`A.Second`"));
        Census(Run(prose)).ShouldBe(["One.A.Second:selected method absent from checklist"], "c1005-pc-15");
        var labels = Make(source: Two.Replace("void Second() {}", "void Second() { x.ShouldBe(1, \"witness\"); x.ShouldNotContain(\"canary\", \"exclude\"); }"), roster: ["A.First"]);
        File.WriteAllText(labels.Checklist, Checklist([Item("A.First"), Item("A.Second", kind: "label", name: "witness"), Item("A.Second", kind: "canary", name: "canary")]));
        Census(Run(labels)).ShouldBe(["One.A.Second:selected method absent from checklist"], "c1005-pc-16");
        var regression = Make(); File.WriteAllText(regression.Checklist, Checklist([Item("A.First"), Item("A.Second", "R-1")]));
        Run(regression).ExitCode.ShouldBe(0, "c1005-pc-17");
        var aliases = Make(roster: ["A.First", "One.A.First", "A.Second"]);
        Run(aliases).ExitCode.ShouldBe(0, "c1005-pc-18");
        var wrong = Make(source: "namespace One { class A { [Test] void Same() {} } } namespace Two { class A { [Test] void Same() {} } }", filter: "/*/One/A/*", roster: ["Two.A.Same"]);
        Census(Run(wrong)).ShouldBe(["Two.A.Same:checklist method not selected", "One.A.Same:selected method absent from checklist"], "c1005-pc-19");
        var pc = Make(); File.WriteAllText(pc.Checklist, Checklist([Item("A.First"), Item("A.Second", "PC-1")]));
        Run(pc).ExitCode.ShouldBe(0, "c1005-pc-92");
    }

    [Test]
    public void census_tracks_filter_class_namespace_and_project()
    {
        var source = "namespace One { class A { [Test] void First() {} } class AB { [Test] void Decoy() {} } class B { [Test] void Second() {} } class AExtra { [Test] void Second() {} } class Other { [Test] void Decoy() {} } class Outer { [Test] void First() {} class Inner { [Test] void Decoy() {} } } } namespace One.Child { class A { [Test] void ChildOnly() {} } } namespace Two { class A { [Test] void First() {} } }";
        var project = Make(source: source, filter: "/*/One/A/*", roster: ["One.A.First"]);
        var explicitDecoy = Add(project, "tests/Other/Unselected.cs", "namespace Other; class A { [Test] void ProjectDecoy() {} }");
        Census(Run(project, false, explicitDecoy)).ShouldBeEmpty("c1005-pc-23");
        Census(Run(Make(source: source, filter: "/*/One/A/*", roster: ["One.A.First"]))).ShouldBeEmpty("c1005-pc-24");
        Census(Run(Make(source: source, roster: []))).ShouldBe(["One.A.First:selected method absent from checklist", "One.Child.A.ChildOnly:selected method absent from checklist", "Two.A.First:selected method absent from checklist"], "c1005-pc-25");
        Census(Run(Make(source: source, filter: "/*/One/A/*", roster: ["One.A.First"]))).ShouldBeEmpty("c1005-pc-26");
        Census(Run(Make(source: source, filter: "/*/*/One.A/*", roster: ["One.A.First"]))).ShouldBeEmpty("c1005-pc-27");
        Census(Run(Make(source: source, filter: "/*/One/(A)|(B)/*", roster: ["One.A.First"]))).ShouldBe(["One.B.Second:selected method absent from checklist"], "c1005-pc-28");
        Census(Run(Make(source: source, filter: "/*/One/A*/*", roster: ["One.A.First", "One.AB.Decoy"]))).ShouldBe(["One.AExtra.Second:selected method absent from checklist"], "c1005-pc-29");
        Census(Run(Make(source: source, filter: "/*/One/A/*", roster: ["One.A.First"]))).ShouldBeEmpty("c1005-pc-30");
        Census(Run(Make(source: source, filter: "/*/*/Outer/*", roster: ["One.Outer.First"]))).ShouldBeEmpty("c1005-pc-31");
        Census(Run(project, false, explicitDecoy)).ShouldBeEmpty("c1005-pc-32");
        Run(Make(source: source, filter: "/*/*/One.Outer.Inner/*", roster: ["One.Outer.Inner.Decoy"])).ExitCode.ShouldBe(0, "c1005-nested-selected");
        var twoProjects = Make(source: Two); var other = Add(twoProjects, "tests/Other/B.cs", "namespace Other; class B { [Test] void Third() {} }");
        File.AppendAllText(twoProjects.Plan, "| CP-2 | S1 | `tests/Other -> bin-other/` | other | `/*/*/B/*` | V-1 | all | 1 | 1 |\n");
        Census(Run(twoProjects, false, other)).ShouldBe(["Other.B.Third:selected method absent from checklist"], "c1005-second-project");
        var linked = Make(); var linkedSource = Add(linked, "tests/Other/Linked.cs", "namespace Linked; class B { [Test] void Third() {} }");
        Add(linked, "tests/Sample/Sample.csproj", "<Project><ItemGroup><Compile Include=\"../Other/Linked.cs\" /></ItemGroup></Project>");
        Census(Run(linked, false, linkedSource)).ShouldBeEmpty("c1005-linked-not-selected");
        File.AppendAllText(linked.Plan, "| CP-2 | S1 | CP-1 | linked | `/*/*/Linked.B/*` | V-1 | all | 1 | 1 |\n");
        Census(Run(linked, false, linkedSource)).ShouldBe(["Linked.B.Third:selected method absent from checklist"], "c1005-linked-selected");
    }

    [Test]
    public void census_unions_overlapping_filters_and_partial_declarations()
    {
        var rows = "| CP-2 | S1 | `tests/Sample -> bin-demo/` | first | `/*/*/A/*` | V-1 | all | 1 | 1 |\n| CP-1 | S1 | CP-2 | second | `/*/*/One.A/*` | V-1 | all | 1 | 1 |\n| CP-3 | S1 | CP-2 | third | `/*/*/A*/*` | V-1 | all | 1 | 1 |";
        var w = Make(source: "namespace One; partial class A { [Test] void First() {} }", roster: ["A.First"], rows: rows);
        Add(w, "tests/Sample/Part.cs", "namespace One;\npartial class A {\n[Test] void Second() {}\n}");
        var r = Run(w); Census(r).ShouldBe(["One.A.Second:selected method absent from checklist"], "c1005-pc-34");
        r.Diagnostics.Count(x => x.Code == "CLASS_CENSUS_MISMATCH").ShouldBe(1, "c1005-pc-62");
        foreach (var crlf in new[] { false, true })
        {
            if (crlf) File.WriteAllText(w.Plan, File.ReadAllText(w.Plan).Replace("\n", "\r\n"));
            var d = Run(w).Diagnostics.Single(x => x.Code == "CLASS_CENSUS_MISMATCH");
            (d.Id, d.PlanLine, d.PlanColumn, d.TestPath, d.TestLine).ShouldBe(("CP-2", 13, 3, "tests/Sample/Part.cs", 3), "c1005-pc-60");
        }
        File.WriteAllText(w.Checklist, Checklist([Item("A.First"), Item("A.Second"), Item("One.A.Second", "R-1"), Item("A.Second", "PC-1")]));
        Census(Run(w)).ShouldBeEmpty("c1005-pc-20");
        var disjoint = Make(source: "namespace One; class A { [Test] void First() {} } class B { [Test] void Second() {} }", roster: []);
        File.AppendAllText(disjoint.Plan, "| CP-2 | S1 | CP-1 | other | `/*/*/B/*` | V-1 | all | 1 | 1 |\n");
        Census(Run(disjoint)).ShouldBe(["One.A.First:selected method absent from checklist", "One.B.Second:selected method absent from checklist"], "c1005-pc-33");
    }

    [Test]
    public void census_excludes_helpers_and_counts_parameterized_method_once()
    {
        var source = "namespace One; class A { [Test] void BareTest() {} [TestAttribute] void SuffixTest() {} [TUnit.Core.Test] void Qualified() {} [TUnit.Core.TestAttribute] void QualifiedSuffix() {} [global::TUnit.Core.Test] void Global() {} [global::TUnit.Core.TestAttribute] void GlobalSuffix() {} [Test, Arguments(1), Arguments(2)] void Args(int x) {} [Test, MethodDataSource(nameof(Cases))] void Data(int x) {} [Test, Repeat(2)] void Repeated() {} void Helper() {} [Before(Test)] void Before() {} [After(Test)] void After() {} }";
        var roster = new[] { "BareTest", "SuffixTest", "Qualified", "QualifiedSuffix", "Global", "GlobalSuffix", "Args", "Data", "Repeated" }.Select(s => "A." + s).ToArray();
        Census(Run(Make(source: source, roster: roster.Where(s => s != "A.BareTest").ToArray()))).ShouldBe(["One.A.BareTest:selected method absent from checklist"], "c1005-pc-35");
        Census(Run(Make(source: source, roster: roster.Where(s => s != "A.SuffixTest").ToArray()))).ShouldBe(["One.A.SuffixTest:selected method absent from checklist"], "c1005-pc-36");
        Census(Run(Make(source: source, roster: roster.Where(s => !s.Contains("Qualified")).ToArray()))).ShouldBe(["One.A.Qualified:selected method absent from checklist", "One.A.QualifiedSuffix:selected method absent from checklist"], "c1005-pc-37");
        Census(Run(Make(source: source, roster: roster))).ShouldBeEmpty("c1005-pc-38");
        Census(Run(Make(source: source, roster: roster))).ShouldBeEmpty("c1005-pc-39");
        Run(Make(source: source, roster: roster)).ExitCode.ShouldBe(0, "c1005-pc-40");
        Census(Run(Make(source: source, roster: roster.Where(s => s != "A.Data").ToArray()))).ShouldBe(["One.A.Data:selected method absent from checklist"], "c1005-pc-41");
        Census(Run(Make(source: source, roster: roster.Where(s => s != "A.Repeated").ToArray()))).ShouldBe(["One.A.Repeated:selected method absent from checklist"], "c1005-pc-42");
        Census(Run(Make(source: source, roster: roster.Where(s => !s.Contains("Global")).ToArray()))).ShouldBe(["One.A.Global:selected method absent from checklist", "One.A.GlobalSuffix:selected method absent from checklist"], "c1005-pc-93");
        var dynamic = Make(source: source.Replace("class A", "class ATests"), filter: "/*/*/ATests/*", roster: roster.Select(s => s.Replace("A.", "ATests.")).ToArray()); File.WriteAllText(dynamic.Plan, File.ReadAllText(dynamic.Plan).Replace("Coverage", "`ATests` (9 results)"));
        Run(dynamic).Diagnostics.ShouldContain(d => d.Code == "CHECKLIST_COUNT_UNMAPPED", "c1005-pc-77");
        foreach (var attrs in new[] { "[ClassDataSource(typeof(Cases))]", "" })
        {
            var s = "namespace One; " + attrs + " class A { [Test] void First([MethodDataSource(nameof(Cases))] int x) {} }";
            Census(Run(Make(source: s, roster: []))).ShouldBe(["One.A.First:selected method absent from checklist"], "c1005-data-shapes");
        }
    }

    [Test]
    public void census_reports_unmapped_selection_without_dynamic_discovery()
    {
        var command = Make(); File.AppendAllText(command.Plan, "| CP-2 | S1 | n/a | command | `echo inert` | V-1 | all | n/a | 1 |\n");
        Run(command).Diagnostics.ShouldContain(d => d.Code == "CLASS_CENSUS_UNMAPPED" && d.Id == "CP-2", "c1005-pc-43");
        Run(Make(filter: "/*/*/*/*[Category=Unit]")).Diagnostics.ShouldContain(d => d.Code == "CLASS_CENSUS_UNMAPPED", "c1005-pc-44");
        Run(Make(filter: "/*/*/A/First")).Diagnostics.ShouldContain(d => d.Code == "CLASS_CENSUS_UNMAPPED", "c1005-pc-45");
        Run(Make(filter: "/Named/*/A/*")).Diagnostics.ShouldContain(d => d.Code == "CLASS_CENSUS_UNMAPPED", "c1005-pc-46");
        Run(Make(filter: "/*/One*/A/*")).Diagnostics.ShouldContain(d => d.Code == "CLASS_CENSUS_UNMAPPED", "c1005-pc-47");
        foreach (var filter in new[] { "/*/*/A?/*", "/*/*/A*B/*" })
            Run(Make(filter: filter)).Diagnostics.ShouldContain(d => d.Code == "CLASS_CENSUS_UNMAPPED", "c1005-pc-48");
        var noContext = new PlanCoverageAnalyzer().Analyze("p", Plan("/*/*/A/*"), [new("A.cs", Two)], Checklist([Item("A.First"), Item("A.Second")]));
        (noContext.ExitCode, noContext.Diagnostics.Any(d => d.Code == "CLASS_CENSUS_UNMAPPED")).ShouldBe((1, true), "c1005-pc-49");
        Run(Make(source: "using Test = Other.Attribute; " + Two)).Diagnostics.ShouldContain(d => d.Code == "CLASS_CENSUS_UNMAPPED", "c1005-pc-50");
        Run(Make(source: "using Base = Other.B; namespace One; class A : Base { [Test] void First() {} }", roster: ["A.First"])).Diagnostics.ShouldContain(d => d.Code == "CLASS_CENSUS_UNMAPPED", "c1005-pc-51");
        Run(Make(source: Two.Replace("class A", "class A : B") + "class B { [Test] void Inherited() {} }")).Diagnostics.ShouldContain(d => d.Code == "CLASS_CENSUS_UNMAPPED", "c1005-pc-52");
        Run(Make(source: Two.Replace("class A", "class A : Missing"))).Diagnostics.ShouldContain(d => d.Code == "CLASS_CENSUS_UNMAPPED", "c1005-pc-53");
        Run(Make(source: "// <auto-generated>\n" + Two)).Diagnostics.ShouldContain(d => d.Code == "CLASS_CENSUS_UNMAPPED", "c1005-pc-54");
        var generated = Make(); var generatedPath = Path.Combine(generated.Root, "tests/Sample/A.g.cs");
        File.Move(generated.Source, generatedPath);
        Run(generated with { Source = generatedPath }).Diagnostics.ShouldContain(d => d.Code == "CLASS_CENSUS_UNMAPPED", "c1005-pc-54");
        foreach (var symbol in new[] { "true", "false" })
            Run(Make(source: Two.Replace("[Test] void Second() {}", "#if " + symbol + "\n[Test] void Conditional() {}\n#endif"), roster: ["A.First"])).Diagnostics.ShouldContain(d => d.Code == "CLASS_CENSUS_UNMAPPED", "c1005-pc-55");
        Run(Make(source: Two.Replace("[Test] void First", "[Test, Skip(nameof(Reason))] void First"))).Diagnostics.ShouldContain(d => d.Code == "CLASS_CENSUS_UNMAPPED", "c1005-pc-56");
        Run(Make(source: Two.Replace("[Test] void First", "[Test, Maybe] void First") + "class MaybeAttribute : SkipAttribute {}" )).Diagnostics.ShouldContain(d => d.Code == "CLASS_CENSUS_UNMAPPED", "c1005-pc-56");
        Run(Make(source: Two.Replace("class A", "[Explicit] class A"))).Diagnostics.ShouldContain(d => d.Code == "CLASS_CENSUS_UNMAPPED", "c1005-pc-57");
        var known = Make(source: Two.Replace("class A", "class A : B")); Add(known, "tests/Sample/Bases.cs", "namespace One; class B : C { void Helper() {} } class C { [Before(Test)] void Setup() {} }");
        var admitted = Run(known);
        admitted.ExitCode.ShouldBe(0, "c1005-pc-58");
        admitted.Sources.Select(s => s.Path).ShouldBe(["tests/Sample/A.cs"], "c1005-base-sources");
        Run(Make(source: Two + "class Unsafe : Missing { [Test, Explicit] void Decoy() {} }")).ExitCode.ShouldBe(0, "c1005-pc-84");
        Run(Make(source: Two + "class TestAttribute {}" )).Diagnostics.ShouldContain(d => d.Code == "CLASS_CENSUS_UNMAPPED", "c1005-pc-94");
        Run(Make(source: Two.Replace("class A", "[System.CodeDom.Compiler.GeneratedCode(\"g\", \"1\")] class A"))).Diagnostics.ShouldContain(d => d.Code == "CLASS_CENSUS_UNMAPPED", "c1005-pc-95");
        var aliasBase = Make(source: "using Base = Other.B; namespace One; class A : Base { [Test] void First() {} }", roster: ["A.First"]);
        Add(aliasBase, "tests/Sample/Base.cs", "namespace Other { class B {} } namespace Another { class B {} }");
        Run(aliasBase).ExitCode.ShouldBe(1, "c1005-ambiguous-base-verdict");
        foreach (var shape in new[] { "[Explicit]", "[Skip(nameof(Reason))]", "[GeneratedCode(\"g\",\"1\")]", "[Maybe]" })
        {
            var selected = Make(source: Two.Replace("class A", shape + " class A") + "class MaybeAttribute : SkipAttribute {}" );
            var unselected = Make(source: Two + shape + " class Unsafe : Missing { [Test] void Decoy() {} } class MaybeAttribute : SkipAttribute {}" );
            (Run(selected).ExitCode, Run(unselected).ExitCode).ShouldBe((1, 0), "c1005-shape-ownership");
        }
        var conditionalSibling = Make(source: Two + "#if false\nclass Unsafe { [Test] void Decoy() {} }\n#endif\n");
        var aliasSibling = Make(source: "namespace One { class A { [Test] void First() {} } } namespace Two { using Test = Other.Attribute; class Decoy { [Test] void Ignored() {} } }", roster: ["A.First"]);
        Run(aliasSibling).ExitCode.ShouldBe(0, "c1005-alias-sibling");
        Run(conditionalSibling).ExitCode.ShouldBe(0, "c1005-conditional-sibling");
        foreach (var filter in new[] { "/*/*/A/First", "/Named/*/A/*", "/*/One*/A/*", "/*/*/A?/*", "/*/*/*/*[Category=Unit]" })
            (Run(Make(filter: filter)).ExitCode, Run(Make(filter: filter)).Invalid).ShouldBe((1, false), "c1005-unmapped-verdicts");
        Run(Make(filter: "/*/*/Missing/*")).ExitCode.ShouldBe(2, "c1005-unresolved-class-invalid");
    }

    [Test]
    public async Task census_public_cli_is_deterministic_and_read_only()
    {
        var inert = Two.Replace("class A {", "class A { static A() { throw new System.Exception(\"must never execute\"); }")
            .Replace("void Second() {}", "void Second() { System.IO.File.WriteAllText(\"sentinel\", \"must never execute\"); }");
        var w = Make(source: inert, roster: []); var before = Snapshot(w.Root);
        var driver = new FakeDriver();
        var json = new StringWriter(); var runtime = new CheckpointApp.Runtime { Driver = driver, Output = json,
            Launch = _ => throw new InvalidOperationException("coverage launched executor"), Wait = _ => throw new InvalidOperationException("coverage waited for executor") };
        var args = new[] { "coverage", "--repo-root", w.Root, "--plan", w.Plan, "--tests", w.Source, "--checklist", w.Checklist, "--format", "json" };
        var exit = await Antiphon.Checkpoints.Program.RunAsync(args, runtime);
        var r = JsonSerializer.Deserialize<PlanCoverageReport>(json.ToString(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        r.Diagnostics.Select(d => (d.Test, d.TestLine, d.PlanLine, d.PlanColumn, d.Detail)).ShouldBe([("One.A.First", 3, 13, 3, "selected method absent from checklist"), ("One.A.Second", 4, 13, 3, "selected method absent from checklist")], "c1005-pc-63");
        (exit, r.Summary.Result).ShouldBe((1, "findings"), "c1005-pc-64");
        r.Summary.Missing.ShouldBe(0, "c1005-pc-66");
        r.Summary.Reachability.ShouldBe("unproven", "c1005-pc-67");
        Snapshot(w.Root).ShouldBe(before, "c1005-pc-79");
        driver.Count(_ => true).ShouldBe(0, "c1005-pc-78");
        var again = new StringWriter(); await Antiphon.Checkpoints.Program.RunAsync(args, new CheckpointApp.Runtime { Driver = driver, Output = again });
        again.ToString().ShouldBe(json.ToString(), "c1005-public-repeat");
        var text = new StringWriter(); await Antiphon.Checkpoints.Program.RunAsync(args[..^1].Append("text").ToArray(), new CheckpointApp.Runtime { Driver = driver, Output = text });
        text.ToString().ShouldContain("COVERAGE code=CLASS_CENSUS_MISMATCH planLine=13 planColumn=3 id=CP-1 test=\"One.A.First\"", Case.Sensitive, "c1005-public-text");
        var unsupported = Make(filter: "/*/*/A/First"); var u = Run(unsupported);
        (u.ExitCode, u.Summary.Result).ShouldBe((1, "findings"), "c1005-pc-65");
        var badManifest = Make(); File.WriteAllText(badManifest.Plan, "invalid"); Run(badManifest).ExitCode.ShouldBe(2, "c1005-pc-81");
        Run(Make(source: "class A { void Bad( {")).ExitCode.ShouldBe(2, "c1005-pc-82");
        SetRoster(w, ["A.First", "A.Second"]); Run(w).ExitCode.ShouldBe(0, "c1005-public-clean");
        var pc = Make(source: Two.Replace("void First() {}", "void First() { x.ShouldBe(1, \"control-target\"); }"));
        File.WriteAllText(pc.Checklist, Checklist([Item("A.First"), Item("A.Second"), Item("A.First", "PC-1", "label", "control-target")]));
        var pcReport = Run(pc);
        pcReport.Pcs.Select(p => (p.Status, p.Reachability)).ShouldBe([("static-labeled", "unproven")], "c1005-pc-67");
        foreach (var scenario in new[] { "clean", "mismatch", "unmapped", "manifest-invalid", "syntax-invalid" })
        {
            var sample = Make(roster: scenario == "mismatch" ? [] : ["A.First", "A.Second"], filter: scenario == "unmapped" ? "/*/*/A/First" : "/*/*/A/*");
            if (scenario == "manifest-invalid") File.WriteAllText(sample.Plan, "invalid manifest");
            if (scenario == "syntax-invalid") File.WriteAllText(sample.Source, "class A { void Bad( {");
            var sampleBefore = Snapshot(sample.Root);
            foreach (var format in new[] { "json", "text" })
            {
                var output = new StringWriter(); var repeated = new StringWriter();
                var cliArgs = new[] { "coverage", "--repo-root", sample.Root, "--plan", sample.Plan, "--tests", sample.Source, "--checklist", sample.Checklist, "--format", format };
                var expectedExit = scenario == "clean" ? 0 : scenario.EndsWith("invalid", StringComparison.Ordinal) ? 2 : 1;
                var actualExit = await Antiphon.Checkpoints.Program.RunAsync(cliArgs, new CheckpointApp.Runtime { Output = output, Driver = driver, Launch = _ => throw new InvalidOperationException("launch"), Wait = _ => throw new InvalidOperationException("wait") });
                actualExit.ShouldBe(expectedExit, "c1005-public-verdict-matrix");
                await Antiphon.Checkpoints.Program.RunAsync(cliArgs, new CheckpointApp.Runtime { Output = repeated, Driver = driver });
                repeated.ToString().ShouldBe(output.ToString(), "c1005-public-format-determinism");
                Snapshot(sample.Root).ShouldBe(sampleBefore, "c1005-public-read-only-matrix");
            }
        }
        driver.Count(_ => true).ShouldBe(0, "c1005-public-all-driver-zero");
    }

    [Test]
    public void opt_out_preserves_legacy_reports_and_declared_counts()
    {
        foreach (var flag in new string?[] { null, "false" })
        {
            var legacySource = Two.Replace("class A", "class A : Base");
            var w = Make(source: legacySource, flag: flag);
            Add(w, "tests/Sample/Base.cs", "namespace One; class Base { void Helper() {} }");
            var r = Run(w);
            using var json = JsonDocument.Parse(r.Json());
            json.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["schemaVersion", "mode", "plan", "planSha256", "checklistSha256", "inputsSha256", "sources", "obligations", "exclusions", "diagnostics", "pcs", "invalid", "summary", "exitCode"], "c1005-pc-68");
            r.SchemaVersion.ShouldBe(1, "c1005-pc-69");
            r.Sources.Select(s => (s.Path, s.Sha256, string.Join(',', s.Classes))).ShouldBe([("tests/Sample/A.cs", PlanCoverageReport.Hash(legacySource), "One.A")], "c1005-pc-70");
            var sourceHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(legacySource)));
            var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(r.PlanSha256 + "\n" + r.ChecklistSha256 + "\n" + "tests/Sample/A.cs\0" + sourceHash)));
            r.InputsSha256.ShouldBe(digest, "c1005-pc-71");
            File.WriteAllText(w.Source, Two.Replace("class A", "class ATests"));
            File.WriteAllText(w.Plan, File.ReadAllText(w.Plan).Replace("/A/", "/ATests/").Replace("Coverage", "`ATests` (3 results)"));
            File.WriteAllText(w.Checklist, Checklist([Item("ATests.First"), Item("ATests.Second")], flag));
            Run(w).Diagnostics.ShouldContain(d => d.Code == "CHECKLIST_COUNT_MISMATCH" && d.Detail == "results expected=3 actual=2", "c1005-pc-73");
            File.WriteAllText(w.Checklist, Checklist([], flag));
            Run(w).Diagnostics.ShouldNotContain(d => d.Code == "CHECKLIST_COUNT_MISMATCH", "c1005-pc-74");
            File.WriteAllText(w.Plan, File.ReadAllText(w.Plan).Replace("Regression", "All 1 class-qualified methods in the checklist are required."));
            Run(w).Diagnostics.ShouldContain(d => d.Code == "CHECKLIST_COUNT_MISMATCH" && d.Id == "R-1", "c1005-pc-75");
            var scoped = Make(source: Two.Replace("class A", "class ATests"), filter: "/*/*/ATests/*", roster: ["ATests.First", "ATests.Second"], flag: flag); File.WriteAllText(scoped.Plan, File.ReadAllText(scoped.Plan).Replace("Coverage", "`ATests` (2 results)"));
            File.WriteAllText(scoped.Checklist, Checklist([Item("ATests.First"), Item("ATests.Second", "R-1")], flag));
            Run(scoped).Diagnostics.ShouldContain(d => d.Code == "CHECKLIST_COUNT_MISMATCH" && d.Detail == "results expected=2 actual=1", "c1005-pc-76");
            var helper = Make(roster: ["A.Helper"], flag: flag);
            Run(helper).Obligations.Single().Matches.Count.ShouldBe(1, "c1005-pc-83");
            var classes = Make(source: Two.Replace("class A", "class ATests") + "class Other { void Second() {} }", filter: "/*/*/ATests/*", flag: flag);
            File.WriteAllText(classes.Plan, File.ReadAllText(classes.Plan).Replace("Coverage", "`ATests` (3 results)"));
            File.WriteAllText(classes.Checklist, Checklist([Item("ATests.First"), Item("ATests.Second"), Item("Other.Second")], flag));
            Run(classes).Diagnostics.ShouldContain(d => d.Code == "CHECKLIST_COUNT_MISMATCH" && d.Detail == "results expected=3 actual=2", "c1005-pc-96");
            File.WriteAllText(scoped.Checklist, Checklist([Item("ATests.First"), Item("One.ATests.First"), Item("ATests.Second")], flag));
            Run(scoped).Diagnostics.ShouldBeEmpty("c1005-pc-97");
        }
        Run(Make()).ExitCode.ShouldBe(0, "c1005-opt-in-control");
    }
}
