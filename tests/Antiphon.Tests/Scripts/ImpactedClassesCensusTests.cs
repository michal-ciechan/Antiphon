using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1862. The impacted-classes census reads the tree fresh on every run and keeps no state between runs. These were
/// scripts/test-selector-census.ps1 (deleted 2026-11-22 with the selector script, unit 4 of the remaining-ps1 plan); its
/// owned replay host and pwsh startup probe measured PowerShell only and are gone.
/// </summary>
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ImpactedClassesCensusTests
{
    private const string Table = "{\"schema\":1,\"kinds\":[{\"id\":\"anchor\",\"globs\":[\"server/anchor.cs\"],\"classes\":[\"AnchorTests\"]}]}";

    [Test]
    public void The_runner_names_the_selector_sources()
    {
        ImpactedClassesRun.SelectorSources.ShouldBe(ImpactedClasses.SelectorFiles);
    }

    [Test]
    public void Changed_new_and_removed_declarations_are_read_fresh()
    {
        using var fixture = new Fixture();
        fixture.Put("tests/Antiphon.Tests/Changed.cs", "public sealed class InitialTests {}");
        var initial = fixture.Classes("tests/Antiphon.Tests/Changed.cs");
        initial.ShouldBe(["InitialTests"]);
        fixture.Put("tests/Antiphon.Tests/Changed.cs", "public sealed class ChangedTests {}");
        fixture.Classes("tests/Antiphon.Tests/Changed.cs").ShouldBe(["ChangedTests"]);
        initial.ShouldNotBe(["ChangedTests"], "the earlier run must not see the changed declaration");

        var beforeNew = fixture.Classes("tests/Antiphon.Tests/New.cs");
        fixture.Put("tests/Antiphon.Tests/New.cs", "public partial class NewTests {}");
        var added = fixture.Classes("tests/Antiphon.Tests/New.cs");
        added.ShouldBe(["NewTests"]);
        beforeNew.ShouldBeEmpty("the earlier run must not see the new declaration");

        File.Delete(Path.Combine(fixture.Root, "tests", "Antiphon.Tests", "New.cs"));
        fixture.Classes("tests/Antiphon.Tests/New.cs").ShouldBeEmpty();
        added.ShouldNotBeEmpty("the earlier run keeps the removed declaration");
    }

    [Test]
    public void A_class_declared_in_two_projects_selects_both()
    {
        using var fixture = new Fixture();
        fixture.Put("tests/Antiphon.Tests/Changed.cs", "public sealed class SharedTests {}");
        var before = fixture.Select("tests/Antiphon.Tests/Changed.cs");
        before.GetProperty("filters").GetArrayLength().ShouldBe(1);
        fixture.Put("tests/Antiphon.SessionRunner.Tests/Same.cs", "public sealed class SharedTests {}");
        var selected = fixture.Select("tests/Antiphon.Tests/Changed.cs");
        Strings(selected, "classes").ShouldBe(["SharedTests"]);
        selected.GetProperty("filters").EnumerateArray().Select(row => row.GetProperty("project").GetString()).Order(StringComparer.Ordinal)
            .ShouldBe(["tests/Antiphon.SessionRunner.Tests", "tests/Antiphon.Tests"]);
    }

    [Test]
    public void Os_labels_of_the_index_are_read_fresh()
    {
        using var fixture = new Fixture();
        fixture.Put("tests/Antiphon.Tests/Changed.cs", "public sealed class SharedTests {}");
        fixture.Put("tests/derived-impact-index.json", "{\"schema\":1,\"edges\":[],\"cappedPaths\":[],\"windowsOnly\":[\"SharedTests\"],\"linuxOnly\":[]}");
        var windows = fixture.Select("tests/Antiphon.Tests/Changed.cs");
        Strings(windows, "classes").ShouldBeEmpty();
        windows.GetProperty("windowsFilters").GetRawText().ShouldContain("SharedTests");

        fixture.Put("tests/derived-impact-index.json", "{\"schema\":1,\"edges\":[],\"cappedPaths\":[],\"windowsOnly\":[],\"linuxOnly\":[\"SharedTests\"]}");
        var linux = fixture.Select("tests/Antiphon.Tests/Changed.cs");
        Strings(linux, "classes").ShouldBe(["SharedTests"]);
        linux.GetProperty("windowsFilters").GetArrayLength().ShouldBe(0, "the old Windows label must be gone");
        Strings(windows, "classes").ShouldNotBe(["SharedTests"]);
    }

    [Test]
    [Arguments("{\"schema\":2,\"kinds\":[]}", "root")]
    [Arguments("{\"schema\":1,\"kinds\":[{\"id\":\"bad-row\",\"globs\":[\"x\"],\"classes\":[\"MissingTests\"]}]}", "bad-row")]
    public void A_malformed_or_stale_table_is_refused_and_names_its_row(string table, string row)
    {
        using var fixture = new Fixture();
        fixture.Put("tests/Antiphon.Tests/Changed.cs", "public sealed class ChangedTests {}");
        var good = fixture.Run("tests/Antiphon.Tests/Changed.cs");
        good.Exit.ShouldBe(0, good.Text);
        fixture.Put("tests/impacted-classes.json", table);
        var refused = fixture.Run("tests/Antiphon.Tests/Changed.cs");
        refused.Exit.ShouldBe(1, refused.Text);
        using (var json = JsonDocument.Parse(refused.Stdout))
            json.RootElement.GetProperty("ok").GetBoolean().ShouldBeFalse();
        refused.Text[refused.Stdout.Length..].ShouldContain("impacted-classes table refused: " + row + ":");
    }

    // The census keeps nothing between scans: a second scan in this process sees the changed file, and the first
    // census is unchanged.
    [Test]
    public void Scans_in_one_process_share_no_state()
    {
        using var fixture = new Fixture();
        fixture.Put("tests/Antiphon.Tests/Changed.cs", "public sealed class InitialTests {}");
        var declaration = new Regex(@"class\s+([A-Za-z_][A-Za-z0-9_]*)");
        var derive = new[] { new Regex("DowngradeToken") };
        var before = ImpactedClassesCensus.Scan(fixture.Root, declaration, derive);
        fixture.Put("tests/Antiphon.Tests/Changed.cs", "public sealed class ReentrantTests {} // DowngradeToken");
        var after = ImpactedClassesCensus.Scan(fixture.Root, declaration, derive);
        after.ClassesByFile["tests/Antiphon.Tests/Changed.cs"][0].ShouldBe("ReentrantTests");
        after.DerivedFiles.ShouldContain("tests/Antiphon.Tests/Changed.cs");
        before.DerivedFiles.ShouldNotContain("tests/Antiphon.Tests/Changed.cs", "the earlier census must stay as it was");
        before.ClassesByFile["tests/Antiphon.Tests/Changed.cs"].ShouldBe(["InitialTests"]);
    }

    // The census matches only lines holding "class"; that must be the full-text regex's verdict, CRLF, comments, raw
    // strings and two declarations on one line included.
    [Test]
    public void Declaration_forms_match_the_canonical_regex()
    {
        using var fixture = new Fixture();
        var forms = string.Join("\n",
            "public sealed class PublicTests {}",
            "internal partial class PartialTests {}",
            "partial class PartialTests {}",
            "file class FileTests {}",
            "private class PrivateTests {}",
            "protected abstract class ProtectedTests {}",
            "sealed static abstract partial class LexicalTests {}",
            "[SomeAttribute] public class SameLineAttributeTests {}",
            "private protected class TwoAccessModifiersTests {}",
            "record RecordTests {}",
            "class FirstOnLineTests {} class SecondOnLineTests {}",
            "// class CommentTests {}",
            "const string raw = \"\"\"",
            "public class RawTextTests {}",
            "\"\"\";",
            "class\tTabTests {}",
            "public class BoundaryTestsé {}",
            "").Replace("\n", "\r\n", StringComparison.Ordinal);
        fixture.Put("tests/Antiphon.Tests/Forms.cs", forms);
        var scan = ImpactedClassesCensus.Scan(fixture.Root, ImpactedClassesCensus.Declaration, []);
        var expected = ImpactedClassesCensus.Declaration.Matches(forms).Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();
        expected.ShouldContain("RawTextTests");
        expected.ShouldNotContain("SameLineAttributeTests");
        scan.ClassesByFile["tests/Antiphon.Tests/Forms.cs"].ShouldBe(expected);
    }

    // The real tree: the candidate-line scan finds exactly what the full-text regex finds in every file, and the census
    // stays inside its budget.
    [Test]
    public void Full_census_matches_the_canonical_regex_within_budget()
    {
        var repo = RepoRoot();
        var clock = Stopwatch.StartNew();
        var full = ImpactedClassesCensus.Scan(repo, ImpactedClassesCensus.Declaration, []);
        clock.Stop();
        clock.ElapsedMilliseconds.ShouldBeLessThan(10000, "census of the real tree");
        var expectedFiles = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        var fileCount = 0;
        var stack = new Stack<string>();
        stack.Push(Path.Combine(repo, "tests"));
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            foreach (var child in Directory.EnumerateDirectories(dir))
            {
                var leaf = Path.GetFileName(child);
                if (leaf.Equals("bin", StringComparison.OrdinalIgnoreCase) || leaf.Equals("obj", StringComparison.OrdinalIgnoreCase)) continue;
                stack.Push(child);
            }
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs"))
            {
                var relative = Path.GetRelativePath(repo, file).Replace('\\', '/');
                var names = new List<string>();
                foreach (Match match in ImpactedClassesCensus.Declaration.Matches(File.ReadAllText(file)))
                {
                    var name = match.Groups[1].Value;
                    if (!names.Contains(name)) names.Add(name);
                    if (!expectedFiles.TryGetValue(name, out var set)) expectedFiles[name] = set = new SortedSet<string>(StringComparer.Ordinal);
                    set.Add(relative);
                }
                if (names.Count == 0)
                {
                    full.ClassesByFile.ContainsKey(relative).ShouldBeFalse("no phantom declaration: " + relative);
                    continue;
                }
                fileCount++;
                full.ClassesByFile[relative].ShouldBe(names, "file declarations: " + relative);
            }
        }
        full.ClassesByFile.Count.ShouldBe(fileCount, "no extra census files");
        full.FilesByClass.Count.ShouldBe(expectedFiles.Count, "class count");
        foreach (var (name, files) in expectedFiles)
            full.FilesByClass[name].ShouldBe(files.ToList(), "class ownership: " + name);
        fileCount.ShouldBeGreaterThan(100);
    }

    private static List<string> Strings(JsonElement root, string property) =>
        root.GetProperty(property).EnumerateArray().Select(item => item.GetString() ?? "").ToList();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Antiphon.sln was not found.");
    }

    // A repository-shaped directory under the system temp with the anchor table and its class; no git.
    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Root = Directory.CreateTempSubdirectory("impacted-census-").FullName;
            Put("tests/impacted-classes.json", Table);
            Put("tests/Antiphon.Tests/Anchor.cs", "public sealed class AnchorTests {}");
        }

        public string Root { get; }

        public void Put(string relative, string text)
        {
            var file = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }

        public (int Exit, string Stdout, string Text) Run(string changed) =>
            ImpactedClassesRun.Select(Root, "--path", changed, "--json");

        public JsonElement Select(string changed)
        {
            var ran = Run(changed);
            ran.Exit.ShouldBe(0, ran.Text);
            using var json = JsonDocument.Parse(ran.Stdout);
            return json.RootElement.Clone();
        }

        public List<string> Classes(string changed) => Strings(Select(changed), "classes");

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
