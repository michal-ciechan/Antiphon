using Antiphon.Tests.Infrastructure;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Invariants;

// CARD-2071: unit 20 (3937ffdb7) deleted RunCheckpointScriptTests and CheckpointNamespaceCensusUsageTests while the
// CARD-1035 and CARD-1005 checkpoint rows still selected them. The class names in a plan are Markdown data, so the
// build accepted the deletion and only the Windows Full run (PlanCoverageBindingTests, group 113 of b8cbfb24) noticed.
// This guard runs in the standing guard batch: every class operand of every plan checkpoint row must name a class that
// is declared under tests/, or the row must be listed in tests/checkpoint-retired-selections.json. A ledger entry that
// no longer matches an unresolved operand fails too, so a repaired row also leaves the ledger.
[Category("RepositoryInvariant")]
[Category("Unit")]
public sealed class PlanCheckpointClassSelectionGuardTests
{
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C2082_Repository_readers_belong_to_the_invariant_namespace()
    {
        var root = DockerStackDocuments.RepoRoot;
        var readers = RepositoryInvariantAudit.Scan(root);
        readers.ShouldContain(reader => reader.Method == "EveryLiveGateHasDisposition"
            && reader.Input.Contains("LiveLaneGateScan.Matrix", StringComparison.Ordinal),
            "the live checkout must expose the live-gate source inventory");
        var unclassified = readers.Where(reader => !reader.Designated).Select(reader => reader.ToString()).ToArray();
        unclassified.ShouldBeEmpty("Repository readers need both invariant designations:\n" + string.Join('\n', unclassified));

        var historical = PlanTableImporter.ImportFile(Path.Combine(root,
            "docs/superpowers/plans/2026-10-29-card-1610-failed-task-live-session-plan.md"));
        historical.ExitCode.ShouldBe(0, historical.Error);
        var rows = historical.Manifest.ShouldNotBeNull().Checkpoints;
        foreach (var (row, type, floor) in new[]
        {
            ("CP-45", typeof(LandDeliveryWaitRoutingTests), 44),
            ("CP-81", typeof(BuildOutputNestingTests), 249),
        })
        {
            var selected = rows.Single(checkpoint => checkpoint.Id == row);
            selected.MinExecuted.ShouldBe(floor, row);
            selected.Filter.ShouldNotBeNull().Split('/')[3].Contains(type.Name, StringComparison.Ordinal).ShouldBeTrue(row);
            type.Namespace.ShouldBe("Antiphon.Tests.Invariants");
        }
        var stagedHelpers = typeof(BuildOutputNestingTests).GetMethod(nameof(BuildOutputNestingTests.Own_output_stages_helper_program));
        stagedHelpers.ShouldNotBeNull().GetCustomAttributesData().Count(a => a.AttributeType.Name == "ArgumentsAttribute").ShouldBe(11);
        foreach (var project in new[] { "Antiphon.Tests", "Antiphon.SessionRunner.Tests" })
        {
            var projectFile = XDocument.Load(Path.Combine(root, "tests", project, project + ".csproj"));
            projectFile.Descendants("Compile").Select(item => ((string?)item.Attribute("Include"))?.Replace('\\', '/'))
                .ShouldContain("../Shared/TestClassificationGuardTests.cs", project);
        }

        var classAudit = PlanCheckpointClassAudit.Run(root);
        classAudit.Unresolved.ShouldBeEmpty(string.Join('\n', classAudit.Unresolved));
        classAudit.UnusedRetirements.ShouldBeEmpty(string.Join('\n', classAudit.UnusedRetirements));

        var cases = CompiledCheckoutCases.Main;
        var rosterFailures = RepositoryInvariantRosterAudit.Check(root, "Antiphon.Tests", cases);
        rosterFailures.ShouldBeEmpty(string.Join('\n', rosterFailures));
        foreach (var (type, count) in new[]
        {
            (typeof(C0824RefOnlySnapshotMigrationInvariantTests), 4),
            (typeof(C1514HostLoadMigrationInvariantTests), 4),
            (typeof(LandNonCodeSkipInvariantTests), 1),
            (typeof(SessionTranscriptSummaryMigrationInvariantTests), 2),
        })
        {
            var migrated = cases.Where(test => test.Namespace + "." + test.TypeOrClass == type.FullName).ToArray();
            migrated.Length.ShouldBe(count, type.FullName);
            foreach (var test in migrated)
                test.Categories.Order(StringComparer.Ordinal).ShouldBe(["Integration", "RepositoryInvariant", "Slow"], test.Uid);
        }
    }

    [Test]
    public void C2082_Owned_fixture_reads_remain_in_the_product_lane()
    {
        var root = OwnedTempRoot.CreateOwned();
        var privateRoot = OwnedTempRoot.CreateOwned();
        try
        {
            Write(root, "Antiphon.sln", "");
            const string plan = "docs/superpowers/plans/fixture.md";
            Write(root, plan, """
                # A tracked plan

                ### Checkpoints

                | CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
                |---|---|---|---|---|---|---|---|---|
                | CP-1 | S1 | `tests/Fixture.Tests -> bin-fixture/` | source | `/*/*/Readers/NewCheckoutReader` | V-1 | 1 passed | 1 | 1 |
                """);
            Write(privateRoot, plan, "# Golden input without a checkpoint table");
            LibGit2Sharp.Repository.Init(privateRoot);
            using var privateCheckout = new LibGit2Sharp.Repository(privateRoot);
            LibGit2Sharp.Commands.Stage(privateCheckout, plan);
            PlanTableImporter.ImportFile(Path.Combine(root, plan)).ExitCode.ShouldBe(0);
            PlanTableImporter.ImportFile(Path.Combine(privateRoot, plan)).ExitCode.ShouldBe(2);

            Write(root, "tests/Fixture.Tests/Readers.cs", """
                namespace Fixture.Invariants;

                [Category("Unit")]
                partial class Readers
                {
                    [Test]
                    void NewCheckoutReader() => SubjectPlan().ExitCode.ShouldBe(0);

                    [Test]
                    void OwnedReader() => PrivatePlan().ExitCode.ShouldBe(2);
                }
                """);
            Write(root, "tests/Fixture.Tests/ReaderInputs.cs", """
                namespace Fixture.Invariants;

                partial class Readers
                {
                    private static ImportResult SubjectPlan() => PlanTableImporter.ImportFile(
                        Path.Combine(DockerStackDocuments.RepoRoot, "docs/superpowers/plans/fixture.md"));

                    // PlanTableImporter.ImportFile(Path.Combine(DockerStackDocuments.RepoRoot, "docs/superpowers/plans/fixture.md"))
                    private static ImportResult PrivatePlan() => PlanTableImporter.ImportFile(
                        Path.Combine(OwnedTempRoot.CreateOwned(), "docs/superpowers/plans/fixture.md"));
                }
                """);
            Write(root, "tests/Fixture.Tests/CategoryOnly.cs", """
                namespace Fixture.Product;

                [Category("RepositoryInvariant")]
                class CategoryOnly
                {
                    [Test]
                    void MissingNamespace() => File.ReadAllText(Path.Combine(DockerStackDocuments.RepoRoot,
                        "docs/superpowers/plans/fixture.md")).ShouldBeEmpty();
                }
                """);

            LibGit2Sharp.Repository.Init(root);
            using var repo = new LibGit2Sharp.Repository(root);
            LibGit2Sharp.Commands.Stage(repo, new[] { "Antiphon.sln", plan, "tests/Fixture.Tests/Readers.cs",
                "tests/Fixture.Tests/ReaderInputs.cs", "tests/Fixture.Tests/CategoryOnly.cs" });
            var offenders = RepositoryInvariantAudit.Scan(root).Where(reader => !reader.Designated).ToArray();

            offenders.Select(reader => reader.Method).ShouldBe(["MissingNamespace", "NewCheckoutReader"], ignoreOrder: false);
            offenders[0].InNamespace.ShouldBeFalse();
            offenders[0].HasCategory.ShouldBeTrue();
            offenders[1].InNamespace.ShouldBeTrue();
            offenders[1].HasCategory.ShouldBeFalse();
            offenders[1].Input.ShouldContain("SubjectPlan");
            offenders[1].Input.ShouldContain(plan);
            Should.Throw<DirectoryNotFoundException>(() => RepositoryInvariantAudit.Scan(Path.Combine(root, "missing")));
            const string broken = "tests/Fixture.Tests/Broken.cs";
            Write(root, broken, "namespace Fixture; public sealed class Broken {");
            LibGit2Sharp.Commands.Stage(repo, broken);
            Should.Throw<InvalidDataException>(() => RepositoryInvariantAudit.Scan(root)).Message.ShouldContain(broken);
        }
        finally
        {
            OwnedTempRoot.Release(privateRoot);
            OwnedTempRoot.Release(root);
        }
    }

    [Test]
    public void Plan_checkpoint_rows_select_declared_test_classes_or_are_retired()
    {
        var root = DockerStackDocuments.RepoRoot;
        var audit = PlanCheckpointClassAudit.Run(root);
        Console.WriteLine(audit.Summary);
        if (Directory.Exists(Path.Combine(root, PlanCheckpointClassAudit.PlansPath)))
        {
            audit.Plans.ShouldBeGreaterThan(100, "the plan scan read no checkpoint tables: " + audit.Summary);
            audit.Checked.ShouldBeGreaterThan(1000, "the plan scan checked no class operands: " + audit.Summary);
        }
        audit.Unresolved.ShouldBeEmpty(
            "plan checkpoint rows select classes that are not declared under tests/. Repair the row's filter, or list the row in "
            + PlanCheckpointClassAudit.LedgerPath + " (plan path -> CP id -> operand) when the row is historical:\n"
            + string.Join("\n", audit.Unresolved));
        audit.UnusedRetirements.ShouldBeEmpty(
            "retired selections in " + PlanCheckpointClassAudit.LedgerPath + " now resolve or no longer exist in their plan; remove them:\n"
            + string.Join("\n", audit.UnusedRetirements));
    }

    [Test]
    public void Audit_reports_a_deleted_class_until_its_row_is_repaired_or_retired()
    {
        var root = OwnedTempRoot.CreateOwned();
        try
        {
            Write(root, "tests/Sample.Tests/LiveTests.cs",
                "namespace Sample.Live;\n\n[Category(\"Unit\")]\npublic sealed partial class LiveTests\n{\n}\n\ninternal record struct OtherRecordTests;\n");
            Write(root, "tests/Sample.Tests/Fixtures/GoneTests.cs.txt", "namespace Sample.Live;\npublic sealed class GoneTests { }\n");
            Write(root, "tests/Sample.Tests/bin/Debug/GoneTests.cs", "namespace Sample.Live;\npublic sealed class GoneTests { }\n");
            const string plan = "docs/superpowers/plans/2026-01-01-card-9999-sample-plan.md";
            Write(root, plan, string.Join("\r\n",
                "# Sample", "", "### Checkpoints", "",
                "| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |",
                "|---|---|---|---|---|---|---|---:|---:|",
                "| CP-1 | S1 | `tests/Sample.Tests -> bin-a/` | g | `/*/*/LiveTests/*` | V-1 | ok | 1 | 1 |",
                "| CP-2 | S1 | CP-1 | g | `/*/*/(LiveTests*)\\|(GoneTests*)\\|(Other*)\\|(L?ve*s)/*` | V-2 | ok | 1 | 1 |",
                "| CP-3 | S1 | CP-1 | g | `/*/Sample.Moved/LiveTests*/(A*)` | V-3 | ok | 1 | 1 |",
                "| CP-4 | S1 | CP-1 | g | `/*/*/*/*[Category=Unit]` | V-4 | ok | 1 | 1 |",
                "| CP-5 | S1 | n/a | g | `pwsh -NoProfile -File scripts/x.ps1` | V-5 | ok | 0 | 1 |",
                "", "### Later", "",
                "| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |",
                "|---|---|---|---|---|---|---|---:|---:|",
                "| CP-9 | S1 | CP-1 | g | `/*/*/OutsideTheSectionTests/*` | V-9 | ok | 1 | 1 |",
                ""));

            var bare = PlanCheckpointClassAudit.Run(root);
            bare.Plans.ShouldBe(1);
            bare.Checked.ShouldBe(6, bare.Summary);
            bare.Unresolved.ShouldBe(
                [plan + " CP-2 GoneTests* (namespace *)", plan + " CP-3 LiveTests* (namespace Sample.Moved)"], ignoreOrder: false);
            bare.UnusedRetirements.ShouldBeEmpty();

            WriteLedger(root, new() { [plan] = new() { ["CP-2"] = ["GoneTests*"] } });
            var retired = PlanCheckpointClassAudit.Run(root);
            retired.Unresolved.ShouldBe([plan + " CP-3 LiveTests* (namespace Sample.Moved)"]);
            retired.Retired.ShouldBe(1);
            retired.UnusedRetirements.ShouldBeEmpty();

            WriteLedger(root, new()
            {
                [plan] = new() { ["CP-1"] = ["LiveTests"], ["CP-2"] = ["GoneTests*"], ["CP-3"] = ["LiveTests*"] },
                ["docs/superpowers/plans/not-on-this-line.md"] = new() { ["CP-1"] = ["AbsentTests"] },
            });
            var stale = PlanCheckpointClassAudit.Run(root);
            stale.Unresolved.ShouldBeEmpty();
            stale.UnusedRetirements.ShouldBe([plan + " CP-1 LiveTests"]);

            File.WriteAllText(Path.Combine(root, PlanCheckpointClassAudit.LedgerPath), """{"schema":1,"plans":{},"extra":1}""");
            Should.Throw<InvalidDataException>(() => PlanCheckpointClassAudit.Run(root)).Message.ShouldContain("extra");
        }
        finally
        {
            OwnedTempRoot.Release(root);
        }
    }

    private static void Write(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static void WriteLedger(string root, Dictionary<string, Dictionary<string, string[]>> plans) =>
        Write(root, PlanCheckpointClassAudit.LedgerPath, JsonSerializer.Serialize(new { schema = 1, card = "CARD-2071", plans }));
}

/// <summary>
/// The CARD-2071 scan. A plan's checkpoint rows are the first table of its <c>### Checkpoints</c> section, read the way
/// <see cref="PlanTableImporter"/> reads them (escaped pipes, the first backticked treenode filter in the Filter column),
/// but without the importer's row rules, so a legacy table is still checked. Only treenode filters are read; the class
/// segment's OR operands are matched as globs against the simple names of classes, records and structs declared in
/// tests/**/*.cs (build output, fixtures and dot directories excluded), and a namespace segment other than * must match
/// a namespace declared in the same file.
/// </summary>
internal static partial class PlanCheckpointClassAudit
{
    internal const string PlansPath = "docs/superpowers/plans";
    internal const string LedgerPath = "tests/checkpoint-retired-selections.json";

    private static readonly Regex Declaration = new(
        @"^[ \t]*(?:(?:public|internal|private|protected|sealed|static|abstract|partial|file|readonly|unsafe|new|ref)\s+)*"
        + @"(?:class|struct|record(?:\s+(?:class|struct))?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex NamespaceDeclaration = new(
        @"^[ \t]*namespace\s+(?<name>[A-Za-z_][A-Za-z0-9_.]*)", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex Operand = new(@"^[A-Za-z0-9_*?]*[A-Za-z_][A-Za-z0-9_*?]*$", RegexOptions.CultureInvariant);

    internal sealed record Result(int Plans, int Rows, int Checked, int Retired, int Skipped, List<string> Unresolved, List<string> UnusedRetirements)
    {
        public string Summary =>
            $"plans={Plans} rows={Rows} checked={Checked} retired={Retired} skipped={Skipped} unresolved={Unresolved.Count} unusedRetirements={UnusedRetirements.Count}";
    }

    public static Result Run(string root)
    {
        var classes = DeclaredClasses(root);
        var resolved = new Dictionary<(string Operand, string Namespace), bool>();
        var ledger = ReadLedger(root);
        var used = new HashSet<(string Plan, string Row, string Operand)>();
        var unresolved = new List<string>();
        int plans = 0, rows = 0, checkedOperands = 0, retired = 0, skipped = 0;
        var seenPlans = new HashSet<string>(StringComparer.Ordinal);
        var plansDirectory = Path.Combine(root, PlansPath);
        var planFiles = Directory.Exists(plansDirectory)
            ? Directory.EnumerateFiles(plansDirectory, "*.md", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).Order(StringComparer.Ordinal).ToList()
            : [];
        foreach (var plan in planFiles)
        {
            seenPlans.Add(plan);
            var table = CheckpointRows(File.ReadAllText(Path.Combine(root, plan)));
            if (table.Count == 0)
                continue;
            plans++;
            foreach (var (row, filter) in table)
            {
                rows++;
                var segments = filter.Split('/');
                if (segments.Length < 4 || segments[3] == "*" || segments[3].Contains('[') || segments[2].Contains('['))
                {
                    skipped++;
                    continue;
                }
                foreach (var operand in segments[3].Split('|').Select(part => part.Trim().Trim('(', ')').Trim()))
                {
                    if (!Operand.IsMatch(operand))
                    {
                        skipped++;
                        continue;
                    }
                    checkedOperands++;
                    if (!resolved.TryGetValue((operand, segments[2]), out var hit))
                        resolved[(operand, segments[2])] = hit = Resolves(classes, operand, segments[2]);
                    if (hit)
                        continue;
                    if (ledger.TryGetValue(plan, out var planRows) && planRows.TryGetValue(row, out var operands) && operands.Contains(operand))
                    {
                        used.Add((plan, row, operand));
                        retired++;
                        continue;
                    }
                    unresolved.Add($"{plan} {row} {operand} (namespace {segments[2]})");
                }
            }
        }
        // An entry for a plan this line does not carry (an rc line, a pruned tree) is not stale; one for a present plan is.
        var unused = ledger
            .Where(plan => seenPlans.Contains(plan.Key))
            .SelectMany(plan => plan.Value.SelectMany(row => row.Value.Select(operand => (plan.Key, row.Key, operand))))
            .Where(entry => !used.Contains(entry))
            .Select(entry => $"{entry.Item1} {entry.Item2} {entry.operand}")
            .Order(StringComparer.Ordinal)
            .ToList();
        return new Result(plans, rows, checkedOperands, retired, skipped, unresolved, unused);
    }

    /// <summary>(CP id, treenode filter) for each row of the first table in the first ### Checkpoints section.</summary>
    internal static List<(string Row, string Filter)> CheckpointRows(string markdown)
    {
        var result = new List<(string, string)>();
        var inside = false;
        List<string>? header = null;
        var filterColumn = -1;
        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (!inside)
            {
                inside = line.Trim().Equals("### Checkpoints", StringComparison.Ordinal);
                continue;
            }
            if (line.StartsWith("### ", StringComparison.Ordinal) || line.StartsWith("## ", StringComparison.Ordinal))
                break;
            if (!line.TrimStart().StartsWith('|'))
            {
                if (header is not null)
                    break;
                continue;
            }
            var cells = PlanTableImporter.SplitRow(line);
            if (cells.All(cell => cell.Length == 0 || cell.Trim('-', ':', ' ').Length == 0))
                continue;
            if (header is null)
            {
                header = cells;
                filterColumn = cells.IndexOf("Filter");
                if (filterColumn < 0)
                    break;
                continue;
            }
            if (cells.Count <= filterColumn)
                continue;
            var filter = Payload(cells[filterColumn]);
            if (filter.StartsWith('/'))
                result.Add((cells[0].Trim(), filter));
        }
        return result;
    }

    private static string Payload(string cell)
    {
        var trimmed = cell.Trim();
        var spans = Regex.Matches(trimmed, "`([^`]*)`");
        if (spans.Count >= 1 && spans[0].Groups[1].Value.Trim().StartsWith('/'))
            return spans[0].Groups[1].Value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '`' && trimmed[^1] == '`')
            trimmed = trimmed[1..^1].Trim();
        return trimmed;
    }

    private static bool Resolves(Dictionary<string, HashSet<string>> classes, string operand, string namespaceSegment)
    {
        var space = namespaceSegment == "*" ? null : Glob(namespaceSegment);
        if (operand.IndexOfAny(['*', '?']) < 0)
            return classes.TryGetValue(operand, out var exact) && (space is null || exact.Any(space.IsMatch));
        var prefix = operand.IndexOfAny(['*', '?']) == operand.Length - 1 && operand[^1] == '*' ? operand[..^1] : null;
        var name = prefix is null ? Glob(operand) : null;
        foreach (var (simple, namespaces) in classes)
        {
            if (prefix is not null ? !simple.StartsWith(prefix, StringComparison.Ordinal) : !name!.IsMatch(simple))
                continue;
            if (space is null || namespaces.Any(space.IsMatch))
                return true;
        }
        return false;
    }

    private static Regex Glob(string pattern) =>
        new("^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$", RegexOptions.CultureInvariant);

    private static Dictionary<string, HashSet<string>> DeclaredClasses(string root)
    {
        var classes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var tests = Path.Combine(root, "tests");
        if (!Directory.Exists(tests))
            return classes;
        var pending = new Stack<string>([tests]);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                var leaf = Path.GetFileName(child);
                if (leaf.StartsWith('.') || leaf is "bin" or "obj" or "node_modules" or "TestOutput"
                    || leaf.StartsWith("bin-", StringComparison.Ordinal) || leaf.StartsWith("obj-", StringComparison.Ordinal)
                    || new FileInfo(child).LinkTarget is not null)
                    continue;
                pending.Push(child);
            }
            foreach (var file in Directory.EnumerateFiles(directory, "*.cs"))
            {
                var text = File.ReadAllText(file);
                var namespaces = NamespaceDeclaration.Matches(text).Select(match => match.Groups["name"].Value).DefaultIfEmpty("").ToArray();
                foreach (Match match in Declaration.Matches(text))
                {
                    if (!classes.TryGetValue(match.Groups["name"].Value, out var set))
                        classes.Add(match.Groups["name"].Value, set = new HashSet<string>(StringComparer.Ordinal));
                    set.UnionWith(namespaces);
                }
            }
        }
        return classes;
    }

    private static Dictionary<string, Dictionary<string, HashSet<string>>> ReadLedger(string root)
    {
        var result = new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.Ordinal);
        var path = Path.Combine(root, LedgerPath);
        if (!File.Exists(path))
            return result;
        using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var top = document.RootElement;
        if (top.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(LedgerPath + ": the root must be an object");
        foreach (var property in top.EnumerateObject())
        {
            if (property.Name is not ("schema" or "card" or "note" or "plans"))
                throw new InvalidDataException(LedgerPath + ": unknown property " + property.Name);
        }
        if (!top.TryGetProperty("schema", out var schema) || schema.ValueKind != JsonValueKind.Number || schema.GetInt32() != 1)
            throw new InvalidDataException(LedgerPath + ": schema must be 1");
        if (!top.TryGetProperty("plans", out var plans) || plans.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(LedgerPath + ": plans must be an object");
        foreach (var plan in plans.EnumerateObject())
        {
            if (plan.Value.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException(LedgerPath + ": " + plan.Name + " must map CP ids to operand arrays");
            var rows = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var row in plan.Value.EnumerateObject())
            {
                if (row.Value.ValueKind != JsonValueKind.Array || row.Value.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
                    throw new InvalidDataException(LedgerPath + ": " + plan.Name + " " + row.Name + " must be an array of operands");
                rows[row.Name] = row.Value.EnumerateArray().Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal);
            }
            result[plan.Name] = rows;
        }
        return result;
    }
}
