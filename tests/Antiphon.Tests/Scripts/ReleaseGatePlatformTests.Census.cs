using System.Collections;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Scripts.Nightly.NightlyPlanCases;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The Linux-only census cases (CARD-1613, CARD-1686), in process since unit 18 of 2026-11-22-remaining-ps1-to-csharp.md
/// (they ran scripts/test-rc-platform.ps1). The census is LinuxOnlyCensus; the rows keep their C1323 names. The fixture
/// sources spell the marker attribute's name as NLO, replaced at run time (Fx), so the census never reads this file as
/// a marker.
/// </summary>
public sealed partial class ReleaseGatePlatformTests
{
    /// <summary>
    /// Windows rc exclusions for skips that are Linux-only by construction; gap classes stay required. CARD-1613: compiled
    /// NativeLinuxPolicyWitnessTests owns the reflection proof in each suite. CARD-1686: the census is derived from the
    /// shipped rows and the source markers; there is no second list.
    /// </summary>
    [Test]
    public void LinuxOnlyWindowsExclusionPolicy()
    {
        AssertExpectedSkipRosterIsCurrent(Repo);

        var rows = new NightlyCaseRows();
        string[] decisionClasses = ["ClaudeDialogWatchdogCanaryTests", "CheckpointOwnedOutputTests", "CheckpointLargeStampTests", "CheckpointShapeConflictTests",
            "CheckpointTrackedBaselineTests", "CheckpointOutputConfinementTests", "RunnerWorkspaceBindingTests"];
        string[] decisionMethods =
        [
            "OutputCleanupTests/parent_symlink_does_not_delete_a_tracked_stamp",
            "CompletedCardWorkspaceRemovalTests/C1032_InspectionHoldShapes",
            "PlanCoverageCommandTests/existing_outside_project_is_refused_by_root_boundary",
            "RunnerWorkspaceBindingTests/C1032_StoreIdentityRefused",
        ];
        var shipped = Policy();
        var catalog = TestSelectionCatalog.Read(Path.Combine(Repo, "tests", "test-selection-policy.json"), shipped.Object.SelectionCatalog.Hash);
        var all = shipped.Object.Profiles["rc"].Exclusions;
        var inventory = all.Where(r => (r.Reason ?? "").StartsWith(LinuxOnlyCensus.Prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
        var census = LinuxOnlyCensus.Run(Repo, shipped.Object, catalog);
        var rowProblems = census.Problems.Where(p => p.StartsWith("row-platform:", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("row-owner:", StringComparison.OrdinalIgnoreCase) || p.StartsWith("row-suite:", StringComparison.OrdinalIgnoreCase)).ToArray();
        rows.Check(inventory.Length > 0 && rowProblems.Length == 0, "C1323 every-new-row-has-linux-gate", string.Join("; ", rowProblems.Append("rows=" + inventory.Length)));
        var markerProblems = census.Problems.Where(p => !rowProblems.Contains(p, StringComparer.Ordinal)).ToArray();
        var decisionMarked = decisionClasses.Where(name => census.MarkerKeys.Any(k => k.Split('|')[1] == name)).ToArray();
        rows.Check(markerProblems.Length == 0 && decisionMarked.Length == 0 && census.MarkerKeys.Length > 0, "C1323 markers-and-rows-agree",
            string.Join("; ", markerProblems.Append("decision-marked=" + string.Join(",", decisionMarked)).Append("markers=" + census.MarkerKeys.Length)));
        var decisionHits = decisionClasses.Where(name => all.Any(r => r.Class == name)).ToList();
        foreach (var pair in decisionMethods)
        {
            var (cls, method) = (pair.Split('/')[0], pair.Split('/')[1]);
            foreach (var row in all.Where(r => r.Class == cls))
                if (row.Methods.Length == 0 || row.Methods.Contains(method, StringComparer.Ordinal)) decisionHits.Add(pair);
        }
        rows.Check(decisionHits.Count == 0, "C1323 decision-classes-are-not-class-excluded", string.Join(",", decisionHits));

        var nodes = new List<(string Suite, string Class, string Method, string Uid, bool Gap)>();
        foreach (var row in inventory)
            foreach (var m in row.Methods.Length == 0 ? new[] { "LinuxOnlyCase" } : row.Methods)
                nodes.Add((row.Suite, row.Class, m, row.Class + "." + m, false));
        (string Suite, string Class, string Method)[] gaps =
        [
            ("antiphon", "ClaudeDialogWatchdogCanaryTests", "C680_LiveNudgeRefusesPermissionAndQuestionDialogs"),
            ("antiphon", "OutputCleanupTests", "parent_symlink_does_not_delete_a_tracked_stamp"),
            ("antiphon", "CheckpointOwnedOutputTests", "changed_inventory_is_refused"),
            ("antiphon", "CheckpointLargeStampTests", "C1363_unprovable_entry_is_unreadable_apart_from_tampered"),
            ("session-runner", "CompletedCardWorkspaceRemovalTests", "C1032_InspectionHoldShapes"),
            ("antiphon", "CheckpointShapeConflictTests", "shape_directory_on_file"),
            ("antiphon", "CheckpointTrackedBaselineTests", "local_output_property_tampering_is_reported"),
            ("antiphon", "CheckpointOutputConfinementTests", "imported_props_build_preserves_the_tracked_dependency"),
            ("session-runner", "RunnerWorkspaceBindingTests", "C1032_StoreIdentityRefused"),
        ];
        foreach (var g in gaps) nodes.Add((g.Suite, g.Class, g.Method, g.Class + "." + g.Method, true));
        nodes.Add(("antiphon", "PortableCensusSiblingTests", "Runs", "PortableCensusSiblingTests.Runs", false));
        var cache = new Dictionary<string, object[]>(StringComparer.Ordinal);
        bool Excluded((string Suite, string Class, string Method, string Uid, bool Gap) node, string platform)
        {
            var key = node.Suite + "|" + platform;
            if (!cache.TryGetValue(key, out var exclusions)) cache[key] = exclusions = Exclusions(shipped, "rc", node.Suite, platform);
            return NightlyPolicy.NodeExcluded(new PlainObject(("ClassName", node.Class), ("Type", node.Class), ("Method", node.Method)), exclusions);
        }
        var linuxOnly = nodes.Where(n => !n.Gap && n.Class != "PortableCensusSiblingTests").ToArray();
        var windowsLaunched = linuxOnly.Where(n => !Excluded(n, "windows")).ToArray();
        var siblingLaunched = nodes.Where(n => n.Class == "PortableCensusSiblingTests" && !Excluded(n, "windows")).ToArray();
        var complete = CoverageControl(siblingLaunched.Select(n => (n.Uid, "Passed")), shipped)[0];
        var completeReason = string.Join(",", Strings(complete, "reasons"));
        rows.Check(windowsLaunched.Length == 0 && siblingLaunched.Length == 1 && PsValue.IsTrue(PsValue.Prop(complete, "coverageComplete"))
            && !completeReason.Contains("unexecuted-outcome", StringComparison.OrdinalIgnoreCase),
            "C1323 linux-only-rows-leave-census-complete", "launched=" + windowsLaunched.Length + " " + completeReason);
        var gapNodes = nodes.Where(n => n.Gap).ToArray();
        var gapLaunched = gapNodes.Where(n => !Excluded(n, "windows")).ToArray();
        var gapTerminal = siblingLaunched.Select(n => (n.Uid, "Passed")).Concat(gapLaunched.Select(n => (n.Uid, "NotExecuted")));
        var incomplete = CoverageControl(gapTerminal, shipped)[0];
        var incompleteReason = string.Join(",", Strings(incomplete, "reasons"));
        var gapsNamed = gapLaunched.All(g => incompleteReason.Contains(g.Uid, StringComparison.OrdinalIgnoreCase));
        var inventoryNamed = linuxOnly.Any(n => incompleteReason.Contains(n.Uid, StringComparison.OrdinalIgnoreCase));
        rows.Check(gapLaunched.Length == gapNodes.Length && !PsValue.IsTrue(PsValue.Prop(incomplete, "coverageComplete")) && gapsNamed && !inventoryNamed
            && incompleteReason.Contains("unexecuted-outcome", StringComparison.OrdinalIgnoreCase), "C1323 decision-gaps-stay-incomplete", incompleteReason);
        var linuxLaunched = linuxOnly.Where(n => !Excluded(n, "linux")).ToArray();
        rows.Check(linuxLaunched.Length == linuxOnly.Length && linuxOnly.Length > 0, "C1323 linux-still-requires-the-rows",
            "linux=" + linuxLaunched.Length + " inventory=" + linuxOnly.Length);
        rows.ShouldAllPass(6, "C1323 every-new-row-has-linux-gate", "C1323 markers-and-rows-agree", "C1323 decision-classes-are-not-class-excluded",
            "C1323 linux-only-rows-leave-census-complete", "C1323 decision-gaps-stay-incomplete", "C1323 linux-still-requires-the-rows");
    }

    [Test]
    public void LinuxOnlyWindowsExclusionPolicy_uses_CLR_nested_and_generic_roster_identities()
    {
        using var fixture = NightlyFixture.Create("eps-nested");
        var repo = CensusRoot(fixture, "nested-roster",
        [
            ("tests/Outer.cs", """
                namespace Demo;
                public partial class Outer<T> { public partial class Inner<TFirst, TSecond> { } }
                """),
            ("tests/Outer.Tests.cs", """
                namespace Demo
                {
                    public partial class Outer<T>
                    {
                        public partial class Inner<TFirst, TSecond>
                        {
                            [Test, Arguments(42)] public void CanRun(int number) { }
                        }
                    }
                }
                """),
            ("tests/Nested.cs", "namespace Demo; public class NonGeneric { public class Nested { [Test] public void CanRun() { } } }"),
            ("tests/Homonym.cs", "namespace Demo; public class Another { [Test] public void CanRun() { } }"),
        ]);
        var path = Path.Combine(repo, "tests", "expected-platform-skips.json");
        void WriteRoster(params string[] identities) => File.WriteAllText(path,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                entries = identities.Select(test => new { platform = "windows", test, reasonPrefix = "fixture" }),
            }));

        const string generic = "Demo.Outer`1+Inner`2";
        const string valid = generic + "[[System.Int32, System.Private.CoreLib],[System.String, System.Private.CoreLib],[System.Boolean, System.Private.CoreLib]].CanRun";
        const string wrongArgumentCount = generic + "[[System.Int32, System.Private.CoreLib],[System.String, System.Private.CoreLib]].CanRun";
        const string invalidArgument = generic + "[[not a CLR type, System.Private.CoreLib],[System.String, System.Private.CoreLib],[System.Boolean, System.Private.CoreLib]].CanRun";
        var nestedArgument = typeof(Dictionary<string, int[,]>).AssemblyQualifiedName;
        var validNestedArgument = generic + $"[[{nestedArgument}],[System.String, System.Private.CoreLib],[System.Boolean, System.Private.CoreLib]].CanRun";

        WriteRoster(generic + ".CanRun", "Demo.NonGeneric+Nested.CanRun", "Demo.Another.CanRun", valid, validNestedArgument);
        AssertExpectedSkipRosterIsCurrent(repo);

        WriteRoster(valid, wrongArgumentCount, invalidArgument, validNestedArgument);
        var staleArguments = Should.Throw<ShouldAssertException>(() => AssertExpectedSkipRosterIsCurrent(repo));
        staleArguments.Message.ShouldContain(wrongArgumentCount);
        staleArguments.Message.ShouldContain(invalidArgument);
        staleArguments.Message.ShouldNotContain(valid);
        staleArguments.Message.ShouldNotContain(validNestedArgument);

        WriteRoster("Demo.NonGeneric.Nested.CanRun", "Demo.Outer`1+Inner`1.CanRun", "Demo.Unrelated.CanRun");
        var stale = Should.Throw<ShouldAssertException>(() => AssertExpectedSkipRosterIsCurrent(repo));
        stale.Message.ShouldContain("Demo.NonGeneric.Nested.CanRun, Demo.Outer`1+Inner`1.CanRun, Demo.Unrelated.CanRun");
    }

    private static void AssertExpectedSkipRosterIsCurrent(string repo)
    {
        var skipRoster = NightlyChunkVerdict.SkipRoster(Path.Combine(repo, "tests", "expected-platform-skips.json"));
        foreach (var platform in new[] { "linux", "windows" })
        {
            var staleEntries = NightlyExpectedSkips.SourceGaps(repo, platform, skipRoster, null);
            staleEntries.ShouldBeEmpty($"Stale expected-platform skip entries for {platform}: {string.Join(", ", staleEntries)}");
        }
    }

    /// <summary>
    /// CARD-1686: fixture trees prove the derived census fails for a marker without a row, a row without a marker and any
    /// use of the name outside a canonical marker line, a comment line or an exempt file's reference forms.
    /// </summary>
    [Test]
    public void C1686_LinuxOnlyCensus()
    {
        using var fixture = NightlyFixture.Create("c1686");
        var rows = new NightlyCaseRows();
        const string prefix = "Linux-only by construction: fixture.";
        var policyRows = new[]
        {
            CensusRow("antiphon", "AlphaTests", null, "windows", prefix, "CARD-1686"),
            CensusRow("session-runner", "BetaTests", ["Second", "First"], "windows", prefix, "CARD-1686"),
        };
        var policy = CensusPolicy(policyRows);
        var catalog = CensusCatalog(CensusRule("R-CENSUS", ["tests/test-execution-policy.json", LinuxOnlyCensus.SourcePath], [LinuxOnlyCensus.CensusSet]));
        LinuxOnlyCensusResult Census(string name, IEnumerable<(string Path, string Text)> files, ExecutionPolicy? other = null, IDictionary? routes = null) =>
            LinuxOnlyCensus.Run(CensusRoot(fixture, name, files), other ?? policy, routes ?? catalog);
        string Problems(LinuxOnlyCensusResult r) => string.Join("; ", r.Problems);

        var good = Census("good", Files);
        rows.Check(good.Ok && SameSet(good.MarkerKeys, ["antiphon|AlphaTests|", "session-runner|BetaTests|First,Second"])
            && SameSet(good.MarkerFiles, ["tests/Antiphon.SessionRunner.Tests/BetaTests.cs", "tests/Antiphon.Tests/Scripts/AlphaTests.cs"]),
            "C1323 c1686-consistent-census-passes", string.Join("; ", good.Problems.Concat(good.MarkerKeys)));

        var r = Census("class-marker", With(Files, "tests/Antiphon.Tests/Checkpoints/GammaTests.cs", Gamma));
        rows.Check(SameSet(r.Problems, ["marker-without-row:antiphon|GammaTests|"]), "C1323 c1686-class-marker-without-row-fails", Problems(r));

        r = Census("method-marker", With(Files, "tests/Antiphon.SessionRunner.Tests/BetaTests.cs", BetaThird));
        rows.Check(SameSet(r.Problems, ["marker-without-row:session-runner|BetaTests|First,Second,Third", "row-without-marker:session-runner|BetaTests|First,Second"]),
            "C1323 c1686-method-marker-without-row-fails", Problems(r));

        var more = policyRows.Concat([CensusRow("pty-host", "DeltaTests", null, "windows", prefix, "CARD-1686"),
            CensusRow("antiphon", "PortableTests", ["Runs"], "windows", prefix, "CARD-1686")]).ToArray();
        r = Census("row", Files, CensusPolicy(more));
        rows.Check(SameSet(r.Problems, ["row-without-marker:pty-host|DeltaTests|", "row-without-marker:antiphon|PortableTests|Runs"]), "C1323 c1686-row-without-marker-fails", Problems(r));

        var bad = new[]
        {
            CensusRow("antiphon", "AlphaTests", null, "linux", prefix, ""),
            CensusRow("session-runner", "BetaTests", ["First", "Second"], "windows", prefix, "CARD-1686"),
        };
        r = Census("attribution", Files, CensusPolicy(bad));
        rows.Check(SameSet(r.Problems, ["row-platform:antiphon|AlphaTests|", "row-owner:antiphon|AlphaTests|"]), "C1323 c1686-row-attribution-fails", Problems(r));

        r = Census("outside", With(Files, "tests/New.Fixture/EpsilonTests.cs", Epsilon));
        rows.Check(SameSet(r.Problems, ["marker-outside-suite:tests/New.Fixture/EpsilonTests.cs"]), "C1323 c1686-marker-outside-suite-fails", Problems(r));

        var route = CensusRoot(fixture, "route", Files);
        var narrowed = LinuxOnlyCensus.Run(route, policy, CensusCatalog(CensusRule("R-CENSUS", ["tests/test-execution-policy.json", LinuxOnlyCensus.SourcePath], [LinuxOnlyCensus.CensusSet]),
            CensusRule("R-ALPHA", ["tests/Antiphon.Tests/Scripts/AlphaTests.cs"], ["main.remainder"])));
        var routed = LinuxOnlyCensus.Run(route, policy, CensusCatalog(CensusRule("R-CENSUS", ["tests/test-execution-policy.json", LinuxOnlyCensus.SourcePath], [LinuxOnlyCensus.CensusSet]),
            CensusRule("R-ALPHA", ["tests/Antiphon.Tests/Scripts/AlphaTests.cs"], ["main.remainder", LinuxOnlyCensus.CensusSet])));
        var policyRoute = LinuxOnlyCensus.Run(route, policy, CensusCatalog(CensusRule("R-CENSUS", [LinuxOnlyCensus.SourcePath], [LinuxOnlyCensus.CensusSet]),
            CensusRule("R-AUTHORITY", [], [], full: true, pattern: @"^tests/test-(execution|selection)-policy\.json$")));
        rows.Check(SameSet(narrowed.Problems, ["marker-file-unrouted:tests/Antiphon.Tests/Scripts/AlphaTests.cs"]) && routed.Ok
            && SameSet(policyRoute.Problems, ["marker-file-unrouted:tests/test-execution-policy.json"]), "C1323 c1686-routes-name-the-census-set",
            string.Join("; ", narrowed.Problems.Concat(routed.Problems).Concat(policyRoute.Problems)));

        // A marker spelling the census does not parse fails closed; it is never skipped or accepted silently.
        foreach (var (name, path, text, line) in Spellings)
        {
            r = Census("noncanonical-" + name, With(Files, path, text));
            rows.Check(SameSet(r.Problems, ["marker-noncanonical:" + Fx(path) + ":" + line]), "C1323 c1686-noncanonical-" + name + "-fails", Problems(r));
        }

        // Every use of the token outside a canonical marker line, a comment line and an exempt file's reference forms fails
        // closed, even where C# would ignore it. CARD-1707 and CARD-1711: the census carries the block-comment and string
        // state across lines, lexes interpolation holes and reads every C# line end; a line whose state it lost fails closed.
        foreach (var (name, path, text, expected) in FailClosed)
        {
            r = Census("fail-closed-" + name, With(Files, path, text));
            rows.Check(SameSet(r.Problems, expected.Select(e => Fx(e.Replace("{Z}", Z, StringComparison.Ordinal)))), "C1323 c1686-fail-closed-" + name, Problems(r));
        }

        // CARD-1711 A-3: a long run of $ outside a string is walked in linear time. A 64k run took 12.9 s when every position
        // retried the run (measured at base on server2); the fixed walk takes milliseconds, so 5 s is generous for 128k.
        var dollarRoot = CensusRoot(fixture, "dollar-run", With(Files, Z,
            "namespace Fixture;\n\npublic sealed class ZetaTests\n{\n    " + new string('$', 131072) + "\n    [NLO(\"x\")]\n    public void M() { }\n}\n"));
        var clock = Stopwatch.StartNew();
        r = LinuxOnlyCensus.Run(dollarRoot, policy, catalog);
        clock.Stop();
        rows.Check(SameSet(r.Problems, ["marker-without-row:antiphon|ZetaTests|M"]) && clock.Elapsed.TotalSeconds < 5, "C1323 c1686-dollar-run-is-linear",
            "seconds=" + Math.Round(clock.Elapsed.TotalSeconds, 2) + "; " + Problems(r));

        r = Census("comments", With(Files, "tests/Antiphon.Tests/PortableTests.cs", Comments));
        rows.Check(r.Ok && SameSet(r.MarkerKeys, good.MarkerKeys), "C1323 c1686-comments-are-not-markers", string.Join("; ", r.Problems.Concat(r.MarkerKeys)));

        // CARD-1694 D-2: the reference forms an exempt file needs (declaration, constructor, typeof, new, a plain string) pass.
        r = Census("exempt-references", With(Files, "tests/Shared/NativeLinuxPolicyWitness.cs", ExemptReferences));
        rows.Check(r.Ok && SameSet(r.MarkerKeys, good.MarkerKeys), "C1323 c1686-exempt-file-references-pass", string.Join("; ", r.Problems.Concat(r.MarkerKeys)));
        rows.ShouldAllPass(62, "C1323 c1686-consistent-census-passes", "C1323 c1686-routes-name-the-census-set", "C1323 c1686-fail-closed-line-separator-opens-string",
            "C1323 c1686-dollar-run-is-linear", "C1323 c1686-exempt-file-references-pass");
    }

    /// <summary>A fixture source's text with NLO replaced by the marker attribute's name.</summary>
    private static string Fx(string text) => text.Replace("NLO", LinuxOnlyCensus.Name, StringComparison.Ordinal);

    /// <summary>New-C1686Root: the files (paths and texts through Fx) under a fresh root of the fixture.</summary>
    private static string CensusRoot(NightlyFixture fixture, string name, IEnumerable<(string Path, string Text)> files)
    {
        var root = fixture.PathOf("c1686-" + name);
        foreach (var (path, text) in files)
        {
            var file = Path.Combine(root, Fx(path));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, Fx(text));
        }
        return root;
    }

    /// <summary>The files with one path's text replaced (or added).</summary>
    private static IEnumerable<(string Path, string Text)> With(IEnumerable<(string Path, string Text)> files, string path, string text) =>
        files.Where(f => f.Path != path).Append((path, text));

    /// <summary>A policy exclusion row as the JSON states it (no methods key when methods is null).</summary>
    private static JsonObject CensusRow(string suite, string cls, string[]? methods, string platform, string reason, string owner)
    {
        var row = new JsonObject { ["suite"] = suite, ["class"] = cls };
        if (methods != null) row["methods"] = new JsonArray(methods.Select(m => (JsonNode?)m).ToArray());
        row["platform"] = platform;
        row["reason"] = reason;
        row["owner"] = owner;
        return row;
    }

    /// <summary>New-C1686Policy: the shipped policy whose rc exclusions are a legacy row and these rows.</summary>
    private static ExecutionPolicy CensusPolicy(IEnumerable<JsonObject> rows)
    {
        var policy = JsonNode.Parse(File.ReadAllText(Path.Combine(Repo, "tests", "test-execution-policy.json")))!.AsObject();
        var exclusions = new JsonArray(new JsonObject { ["suite"] = "antiphon", ["class"] = "LegacyTests", ["reason"] = "legacy row", ["owner"] = "CARD-1313" });
        foreach (var row in rows) exclusions.Add(row.DeepClone());
        policy["profiles"]!["rc"]!["exclusions"] = exclusions;
        return ExecutionPolicy.Parse(policy.ToJsonString());
    }

    /// <summary>A selection rule of a fixture catalog.</summary>
    private static Hashtable CensusRule(string id, string[] paths, string[] sets, bool full = false, string? pattern = null)
    {
        var rule = new Hashtable { ["id"] = id, ["paths"] = paths.Cast<object>().ToArray(), ["sets"] = sets.Cast<object>().ToArray(), ["full"] = full };
        if (pattern != null) rule["pattern"] = pattern;
        return rule;
    }

    /// <summary>A fixture catalog of these rules and no edges.</summary>
    private static Hashtable CensusCatalog(params Hashtable[] rules) => new() { ["rules"] = rules.Cast<object>().ToArray(), ["edges"] = Array.Empty<object>() };

    private const string Z = "tests/Antiphon.Tests/ZetaTests.cs";

    // The fixture sources (generated from the driver's literals; NLO is the marker attribute's name).
    private static readonly string Alpha = "namespace Fixture;\n\n[NLO(\"needs a FIFO (see [man 7 fifo])\")]\n[Category(\"Unit\")]\npublic sealed class AlphaTests\n{\n    [Test]\n    public void Runs() { }\n}\n";
    private static readonly string Beta = "namespace Fixture;\n\npublic sealed partial class BetaTests\n{\n    private sealed class Rig\n    {\n    }\n\n    [NLO(\"procfs, see https://man7.org/linux/man-pages/man5/proc.5.html\")]\n    [Test]\n    [Arguments(\"a\", Categories = [\"c-a\"])]\n    public async Task First(string mode) { }\n\n    // [Test, NLO(\"a comment is not a marker\")]\n    [Test]\n    public void Portable() { }\n\n    [NLO(\"setsid\")] [Test] public Task Second() => Task.CompletedTask;\n}\n";
    private static readonly string Probe = "[NLO(\"probe fixture\")]\npublic sealed class DirectClassProbe { }\n";
    private static readonly string Gamma = "public sealed class Holder\n{\n}\n\n[NLO(\"statx\")]\ninternal sealed class GammaTests { }\n";
    private static readonly string BetaThird = "namespace Fixture;\n\npublic sealed partial class BetaTests\n{\n    private sealed class Rig\n    {\n    }\n\n    [NLO(\"procfs, see https://man7.org/linux/man-pages/man5/proc.5.html\")]\n    [Test]\n    [Arguments(\"a\", Categories = [\"c-a\"])]\n    public async Task First(string mode) { }\n\n    // [Test, NLO(\"a comment is not a marker\")]\n    [Test]\n    public void Portable() { }\n\n    [NLO(\"unshare\")]\n    [Test]\n    public static async Task<int> Third() => 0;\n\n    [NLO(\"setsid\")] [Test] public Task Second() => Task.CompletedTask;\n}\n";
    private static readonly string Epsilon = "[NLO(\"fifo\")]\npublic sealed class EpsilonTests { }\n";
    private static readonly string Comments = "public sealed class PortableTests\n{\n    /*\n     * [NLO(\"block comment\")]\n    /* [Test, NLO(\"block comment\")] */\n    // [ NLO(\"line comment\")]\n    /// <see cref=\"NLOAttribute\"/> is not a marker.\n    [Test]\n    public void Runs() { }\n}\n";
    private static readonly string ExemptReferences = "internal static class NativeLinuxPolicyWitness\n{\n    public const string AttributeResourceName = \"Antiphon.TestSupport.NLOAttribute.cs\";\n    static Type A => typeof(NLOAttribute);\n    static bool B(Type t) => t.GetCustomAttributes(typeof(Antiphon.TestSupport.NLOAttribute), inherit: false).Length > 0;\n    static object C() => new NLOAttribute(\"pin\");\n    static void D() => throw new ArgumentException(\"A NLO reason must be nonempty.\", nameof(D));\n}\n";
    private static readonly (string Path, string Text)[] Files =
    [
        ("tests/Antiphon.Tests/Scripts/AlphaTests.cs", Alpha),
        ("tests/Antiphon.SessionRunner.Tests/BetaTests.cs", Beta),
        ("tests/Antiphon.NativeLinuxPolicy.Probe/ProbeTests.cs", Probe),
        ("tests/Antiphon.Tests/bin/Debug/Copied.cs", "[NLO(\"build output\")]\npublic sealed class CopiedTests { }\n"),
        ("tests/Antiphon.Tests/PortableTests.cs", "public sealed class PortableTests { }\n"),
        ("tests/Shared/NLOAttribute.cs", "public sealed class NLOAttribute : SkipAttribute\n{\n    public NLOAttribute(string reason) : base(reason) { }\n    internal static object Pin() => new Antiphon.TestSupport.NLOAttribute(\"pin\");\n}\n"),
    ];
    private static readonly (string Name, string Path, string Text, int Line)[] Spellings =
    [
        ("stacked-method", "tests/Antiphon.SessionRunner.Tests/BetaTests.cs",
            "namespace Fixture;\n\npublic sealed partial class BetaTests\n{\n    private sealed class Rig\n    {\n    }\n\n    [NLO(\"procfs, see https://man7.org/linux/man-pages/man5/proc.5.html\")]\n    [Test]\n    [Arguments(\"a\", Categories = [\"c-a\"])]\n    public async Task First(string mode) { }\n\n    // [Test, NLO(\"a comment is not a marker\")]\n    [Test, NLO(\"stacked\")]\n    public void Portable() { }\n\n    [NLO(\"setsid\")] [Test] public Task Second() => Task.CompletedTask;\n}\n", 15),
        ("stacked-class", "tests/Antiphon.Tests/ZetaTests.cs",
            "namespace Fixture;\n\n[Category(\"Unit\"), NLO(\"stacked\")]\npublic sealed class ZetaTests { }\n", 3),
        ("suffix", "tests/Antiphon.Tests/ZetaTests.cs",
            "namespace Fixture;\n\n[NLOAttribute(\"suffix\")]\npublic sealed class ZetaTests { }\n", 3),
        ("qualified", "tests/Antiphon.Tests/ZetaTests.cs",
            "namespace Fixture;\n\n[Antiphon.TestSupport.NLO(\"qualified\")]\npublic sealed class ZetaTests { }\n", 3),
        ("global", "tests/Antiphon.Tests/ZetaTests.cs",
            "namespace Fixture;\n\n[global::Antiphon.TestSupport.NLO(\"global\")]\npublic sealed class ZetaTests { }\n", 3),
        ("leading-space", "tests/Antiphon.Tests/ZetaTests.cs",
            "namespace Fixture;\n\n[ NLO(\"spaced\")]\npublic sealed class ZetaTests { }\n", 3),
        ("paren-space", "tests/Antiphon.Tests/ZetaTests.cs",
            "namespace Fixture;\n\n[NLO (\"spaced\")]\npublic sealed class ZetaTests { }\n", 3),
    ];
    private static readonly (string Name, string Path, string Text, string[] Expected)[] FailClosed =
    [
        ("verbatim-interpolated-glob", Z,
            "namespace Fixture;\n\npublic sealed class ZTests\n{\n    static string P(string r) => @$\"{r}\\\" + \"**/*.cs\";\n    [NLO(\"x\")]\n    public void M() { }\n    const string H = \"a/**/b\";\n}\n",
            ["marker-without-row:antiphon|ZTests|M"]),
        ("split-line", Z,
            "namespace Fixture;\n\n[NLO\n    (\"x\")]\npublic sealed class ZetaTests { }\n",
            ["marker-noncanonical:{Z}:3"]),
        ("split-line-comment", Z,
            "namespace Fixture;\n\n[NLO // why\n(\"x\")]\npublic sealed class ZetaTests { }\n",
            ["marker-noncanonical:{Z}:3"]),
        ("using-alias", Z,
            "using Lin = Antiphon.TestSupport.NLOAttribute;\n\nnamespace Fixture;\n\n[Lin(\"x\")]\npublic sealed class ZetaTests { }\n",
            ["marker-noncanonical:{Z}:1"]),
        ("string-literal", Z,
            "namespace Fixture;\n\npublic sealed class ZetaTests\n{\n    const string Name = \"NLO\";\n}\n",
            ["marker-noncanonical:{Z}:5"]),
        ("trailing-comment", Z,
            "namespace Fixture;\n\npublic sealed class ZetaTests\n{\n    [Test] // [NLOAttribute(\"trailing comment\")]\n    public void Runs() { }\n}\n",
            ["marker-noncanonical:{Z}:5"]),
        ("second-use-on-marker-line", Z,
            "namespace Fixture;\n\n[NLO(\"x\")] [Category(\"Unit\"), NLO(\"y\")]\npublic sealed class ZetaTests { }\n",
            ["marker-noncanonical:{Z}:3", "marker-without-row:antiphon|ZetaTests|"]),
        ("block-comment-interior", Z,
            "namespace Fixture;\n\npublic sealed class ZetaTests\n{\n    /*\n    [NLO(\"block comment\")]\n    public void Old() { }\n    [Test, NLO(\"block comment\")] */\n}\n",
            ["marker-noncanonical:{Z}:6", "marker-noncanonical:{Z}:8"]),
        ("block-comment-star-interior", Z,
            "namespace Fixture;\n\npublic sealed class ZetaTests\n{\n    /*\n     * [NLO(\"block comment\")]\n     */\n    [NLO(\"x\")]\n    public void M() { }\n}\n",
            ["marker-without-row:antiphon|ZetaTests|M"]),
        ("block-comment-star-close", Z,
            "namespace Fixture;\n\npublic sealed class ZetaTests\n{\n    /*\n     * [NLO(\"old\")] */\n    public void M() { }\n}\n",
            []),
        ("block-comment-canonical-close", Z,
            "namespace Fixture;\n\npublic sealed class ZetaTests\n{\n    /*\n    [NLO(\"old\")] */\n    public void M() { }\n}\n",
            ["marker-noncanonical:{Z}:6"]),
        ("block-comment-mid-line-open", Z,
            "namespace Fixture;\n\npublic sealed class ZetaTests\n{\n    int a = 1; /* note\n     * [NLO(\"y\")]\n     */\n}\n",
            ["marker-noncanonical:{Z}:6"]),
        ("split-multiplication", Z,
            "namespace Fixture;\n\npublic sealed class ZetaTests\n{\n    int x = 2\n        * 3; [NLO(\"y\")] public void M() { }\n}\n",
            ["marker-noncanonical:{Z}:6"]),
        ("nested-block-comment", Z,
            "namespace Fixture;\n\npublic sealed class ZetaTests\n{\n    /* outer /* inner */\n     * [NLO(\"y\")] public void M() { }\n}\n",
            ["marker-noncanonical:{Z}:6"]),
        ("string-literal-opener", Z,
            "namespace Fixture;\n\npublic sealed class ZetaTests\n{\n    static int Y = \"/*\".Length\n        * 2; [NLO(\"y\")] public void N() { }\n    [NLO(\"x\")]\n    public void M() { }\n}\n",
            ["marker-noncanonical:{Z}:6", "marker-without-row:antiphon|ZetaTests|M"]),
        ("char-literal-quote", Z,
            "namespace Fixture;\n\npublic sealed class ZetaTests\n{\n    static int Y = '\"' + \"/*\".Length\n        * 2; [NLO(\"y\")] public void N() { }\n    [NLO(\"x\")]\n    public void M() { }\n}\n",
            ["marker-noncanonical:{Z}:6", "marker-without-row:antiphon|ZetaTests|M"]),
        ("char-literal-quote-before-string", Z,
            "namespace Fixture;\n\npublic sealed class ZetaTests\n{\n    static readonly char Q = '\"'; static readonly string S = \"/*\";\n    [NLO(\"x\")]\n    public void M() { }\n}\n",
            ["marker-without-row:antiphon|ZetaTests|M"]),
        ("verbatim-multi-line-opener", Z,
            "namespace Fixture;\n\npublic sealed class ZetaTests\n{\n    static int V = @\"a \"\" b\n/* c\".Length\n        * 2; [NLO(\"y\")] public void M() { }\n}\n",
            ["marker-noncanonical:{Z}:7"]),
        ("verbatim-string-marker-line", Z,
            "namespace Fixture;\n\npublic sealed class ZetaTests\n{\n    const string V = @\"\n    [NLO(\"\"x\"\")]\n    public void M() { }\";\n}\n",
            ["marker-noncanonical:{Z}:6"]),
        ("raw-string-opener", Z,
            "namespace Fixture;\n\npublic sealed class ZetaTests\n{\n    const string R = \"\"\"\n        /* not a comment\n        \"\"\";\n    [NLO(\"x\")]\n    public void M() { }\n}\n",
            ["marker-without-row:antiphon|ZetaTests|M"]),
        ("interpolation-hole-opener", Z,
            "namespace Fixture;\n\npublic sealed class ZetaTests\n{\n    static string S(string x) => $\"{x ?? \"/*\"}\";\n    [NLO(\"x\")]\n    public void M() { }\n}\n",
            ["marker-without-row:antiphon|ZetaTests|M"]),
        ("verbatim-interpolation-hole-quote", Z,
            "namespace Fixture;\n\npublic sealed class ZetaTests\n{\n    static int V = $@\"{D[\"k\"]}\n/* c\".Length\n        * 2; [NLO(\"y\")] public void M() { }\n}\n",
            ["marker-noncanonical:{Z}:7"]),
        ("interpolation-hole-unlexable", Z,
            "namespace Fixture;\n\npublic sealed class ZetaTests\n{\n    static int V = $@\"{D[@\"k\"]}\n/* c\".Length\n        * 2; [NLO(\"y\")] public void M() { }\n    [NLO(\"x\")]\n    public void N() { }\n}\n",
            ["marker-noncanonical:{Z}:7", "marker-noncanonical:{Z}:8"]),
        ("raw-interpolation-hole-close", Z,
            "namespace Fixture;\n\npublic sealed class ZetaTests\n{\n    static int R = $\"\"\"{G(\"\"\"\n/* c\n\"\"\")}\"\"\".Length\n        * 2; [NLO(\"y\")] public void M() { }\n}\n",
            ["marker-noncanonical:{Z}:8"]),
        ("interpolation-hole-quotes-in-sync", Z,
            "namespace Fixture;\n\npublic sealed class ZetaTests\n{\n    static string S(bool b) => $\"{(b ? \"a\" : \"b\")}\" + $@\"{(b ? \"c\" : \"d\")}\";\n    static string T(string x) => $$\"\"\"{{string.Join(\" \", x)}} {\"\"\";\n    [NLO(\"x\")]\n    public void M() { }\n}\n",
            ["marker-without-row:antiphon|ZetaTests|M"]),
        ("interpolation-format-escaped-quote", Z,
            "namespace Fixture;\n\npublic sealed class ZetaTests\n{\n    static string S(int x) { return $\"{x:\\\"}\"; } static int T = @\"\n/* c\".Length\n        * 2; [NLO(\"y\")] public void M() { }\n}\n",
            ["marker-noncanonical:{Z}:7"]),
        ("interpolation-format-escaped-quote-verbatim-interpolated", Z,
            "namespace Fixture;\n\npublic sealed class ZetaTests\n{\n    static string S(int x) { return $\"{x:\\\"}\"; } static int T = $@\"\n/* c\".Length\n        * 2; [NLO(\"y\")] public void M() { }\n}\n",
            ["marker-noncanonical:{Z}:7"]),
        ("interpolation-format-escaped-quote-pair", Z,
            "namespace Fixture;\n\npublic sealed class ZetaTests\n{\n    static string S(int x) { return $\"{x:\\\"\\\"}\"; } static int T = @\"\n/* c\".Length\n        * 2; [NLO(\"y\")] public void M() { }\n}\n",
            ["marker-noncanonical:{Z}:7"]),
        ("line-comment-ends-at-u2028", Z,
            "namespace Fixture;\n\n// note\u2028[NLO(\"x\")]\npublic sealed class ZetaTests { }\n",
            ["marker-without-row:antiphon|ZetaTests|"]),
        ("line-comment-ends-at-u2029", Z,
            "namespace Fixture;\n\n// note\u2029[NLO(\"x\")]\npublic sealed class ZetaTests { }\n",
            ["marker-without-row:antiphon|ZetaTests|"]),
        ("line-comment-ends-at-u0085", Z,
            "namespace Fixture;\n\n// note\u0085[NLO(\"x\")]\npublic sealed class ZetaTests { }\n",
            ["marker-without-row:antiphon|ZetaTests|"]),
        ("line-comment-ends-at-lone-cr", Z,
            "namespace Fixture;\n\n// note\r[NLO(\"x\")]\npublic sealed class ZetaTests { }\n",
            ["marker-without-row:antiphon|ZetaTests|"]),
        ("line-separator-opens-string", Z,
            "namespace Fixture;\n\npublic sealed class ZetaTests\n{\n    // note\u2028const string S = @\"\n/* in string\n\";\n    static int Y = 2\n        * 3; [NLO(\"y\")] public void M() { }\n}\n",
            ["marker-noncanonical:{Z}:10"]),
        ("inactive-if", Z,
            "namespace Fixture;\n\n#if false\n[NLO(\"inactive\")]\npublic sealed class ZetaTests { }\n#endif\n",
            ["marker-without-row:antiphon|ZetaTests|"]),
        ("block-comment-prefix", Z,
            "namespace Fixture;\n\n/* x */ [NLO(\"y\")]\npublic sealed class ZetaTests { }\n",
            ["marker-noncanonical:{Z}:3"]),
        ("empty-block-comment-prefix", Z,
            "namespace Fixture;\n\n/**/[NLO(\"y\")]\npublic sealed class ZetaTests { }\n",
            ["marker-noncanonical:{Z}:3"]),
        ("block-comment-close-prefix", Z,
            "namespace Fixture;\n\n/*\n * old\n */ [NLO(\"y\")]\npublic sealed class ZetaTests { }\n",
            ["marker-noncanonical:{Z}:5"]),
        ("indented-block-comment-prefix", Z,
            "namespace Fixture;\n\npublic sealed class ZetaTests\n{\n    /* c */ [NLO(\"y\")]\n    [Test]\n    public void Runs() { }\n}\n",
            ["marker-noncanonical:{Z}:5"]),
        ("exempt-file-stacked-marker", "tests/Antiphon.Tests/Scripts/WindowsUnitPlatformAdmissionTests.cs",
            "namespace Fixture;\n\npublic sealed class WindowsUnitPlatformAdmissionTests\n{\n    [Test, NLO(\"y\")]\n    public void Runs() { }\n}\n",
            ["marker-noncanonical:tests/Antiphon.Tests/Scripts/WindowsUnitPlatformAdmissionTests.cs:5"]),
        ("exempt-file-attribute-list-line", "tests/Shared/NativeLinuxPolicyWitness.cs",
            "namespace Fixture;\n\npublic sealed class WitnessTests\n{\n    [\n        Test,\n        NLO(\"y\")\n    ]\n    public void Runs() { }\n}\n",
            ["marker-noncanonical:tests/Shared/NativeLinuxPolicyWitness.cs:7"]),
        ("exempt-file-string-before-marker", "tests/Shared/NLOAttribute.cs",
            "namespace Fixture;\n\npublic sealed class WitnessTests\n{\n    [Test(Description = \"a\"), NLO(\"y\")]\n    public void Runs() { }\n}\n",
            ["marker-noncanonical:tests/Shared/NLOAttribute.cs:5"]),
        ("exempt-file-block-comment-prefix", "tests/Shared/NativeLinuxPolicyWitnessTests.cs",
            "namespace Fixture;\n\npublic sealed class WitnessTests\n{\n    /* c */ [Test, NLO(\"y\")]\n    public void Runs() { }\n}\n",
            ["marker-noncanonical:tests/Shared/NativeLinuxPolicyWitnessTests.cs:5"]),
        ("exempt-file-block-close-reference", "tests/Shared/NativeLinuxPolicyWitness.cs",
            "internal static class NativeLinuxPolicyWitness\n{\n    /*\n     * doc\n     */ static Type A => typeof(NLOAttribute);\n}\n",
            []),
        ("exempt-file-multi-line-string-reference", "tests/Shared/NativeLinuxPolicyWitness.cs",
            "internal static class NativeLinuxPolicyWitness\n{\n    const string V = @\"first\n    typeof(NLOAttribute)\n    last\";\n}\n",
            []),
        ("reflection-outside-exempt-file", "tests/Shared/OtherWitness.cs",
            "internal static class OtherWitness\n{\n    internal static object Pin() => new Antiphon.TestSupport.NLOAttribute(\"pin\");\n    internal static Type Kind => typeof(NLOAttribute);\n}\n",
            ["marker-noncanonical:tests/Shared/OtherWitness.cs:3", "marker-noncanonical:tests/Shared/OtherWitness.cs:4"]),
    ];
}
