using Antiphon.Tests.Layering;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Layering.LayeringGuardEngine;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class C1729LayeringGuardTests
{
    private const string Edges = """
        [edges]
        Host -> Core, Persistence, Git, Runners, Integrations, Application
        Application -> Core, Persistence
        Git -> Core
        Runners -> Core
        Integrations -> Core
        Persistence -> Core
        Core ->
        [rules]
        """;
    private const string SyntheticMap = Edges + "\ncore/** Core\napp/** Application";

    [Test]
    public void Map_covers_every_server_file_exactly_once()
    {
        var paths = RepositoryPaths();
        paths.Length.ShouldBeGreaterThan(0, "nonempty-census");
        var map = MapFiles(Read("layers.txt"), paths);
        map.Errors.ShouldBeEmpty("unmapped-file=0 dead-rule=0: " + string.Join("\n", map.Errors));
        map.Layers.Count.ShouldBe(paths.Length, "mapped=census");
    }

    [Test]
    public void Layer_of_pinned_files_follows_first_match()
    {
        var map = MapFiles(Read("layers.txt"), RepositoryPaths());
        (string Path, string Layer)[] pins =
        [
            ("server/Program.cs", "Host"),
            ("server/Infrastructure/Agents/ZombieCensusJob.cs", "Host"),
            ("server/Infrastructure/Agents/HangfireConfiguration.cs", "Host"),
            ("server/Infrastructure/Agents/AgentExecutor.cs", "Integrations"),
            ("server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs", "Runners"),
            ("server/Infrastructure/Resilience/DatabaseResilienceExecutor.cs", "Persistence"),
            ("server/Infrastructure/Resilience/NpgsqlTransientFailureClassifier.cs", "Persistence"),
            ("server/Infrastructure/Resilience/ResilienceReadClients.cs", "Application"),
            ("server/Application/Services/GitWorkspaceService.cs", "Git"),
            ("server/Application/Services/AgentTaskService.cs", "Application"),
            ("server/Application/Interfaces/ILandingGit.cs", "Core"),
            ("server/Domain/Entities/AgentTask.cs", "Core")
        ];
        foreach (var (path, layer) in pins)
            map.Layers.GetValueOrDefault(path).ShouldBe(layer, $"layer:{path}={layer}");
    }

    [Test]
    public void Allowlist_parses_with_grouped_rows_and_a_mode_header()
    {
        var parsed = ParseAllowlist(Read("allowlist.txt"), RepositoryPaths());
        parsed.Errors.ShouldBeEmpty("header-ok rows-grouped no-duplicates paths-exist");
        string[] paths = ["core/Value.cs", "app/Svc.cs"];
        const string row = "core/Value.cs -> Svc @ app/Svc.cs";
        (string Text, string Error)[] variants =
        [
            ("# slice test\n" + row, "missing-mode-header"),
            ("strict: false\n" + row, "ungrouped-row:" + row),
            ("strict: false\n# slice test\n" + row + "\n" + row, "duplicate-row:" + row),
            ("strict: false\n# slice test\ncore/Unknown.cs -> Svc @ app/Svc.cs", "unknown-path:core/Unknown.cs -> Svc @ app/Svc.cs")
        ];
        foreach (var (text, error) in variants)
        {
            var negative = ParseAllowlist(text, paths);
            negative.Errors.Count.ShouldBe(1, "parse-error-count:" + error);
            negative.Errors.ShouldContain(error, "parse-error:" + error);
        }
    }

    [Test]
    public void Repository_graph_satisfies_the_committed_allowlist()
    {
        var files = RepositoryPaths().Select(p => new SourceFile(p, File.ReadAllText(Path.Combine(RepoRoot, p)))).ToArray();
        var result = Analyze(Read("layers.txt"), Read("allowlist.txt"), files);
        Console.WriteLine(result.Inventory);
        result.Map.Errors.ShouldBeEmpty("map-errors=0: " + string.Join("\n", result.Map.Errors));
        result.ParseErrors.ShouldBeEmpty("parse-errors=0: " + string.Join("\n", result.ParseErrors));
        if (result.Allowlist.Strict)
        {
            result.Unlisted.ShouldBeEmpty("violation:" + string.Join("\nviolation:", result.Unlisted.Select(v => v.ToString())));
            result.Ambiguous.Where(a => a.IsViolation).ShouldBeEmpty("ambiguous-violation:" + result.Inventory);
            result.Stale.ShouldBeEmpty("stale-row:" + string.Join("\nstale-row:", result.Stale.Select(v => v.ToString())));
        }
        result.Passes.ShouldBeTrue("repository-verdict: " + result.Inventory);
    }

    [Test]
    public void Synthetic_core_file_referencing_an_application_type_fails_in_strict_mode()
    {
        var result = Analyze(SyntheticMap, "strict: true\n# slice test\ncore/Value.cs -> Svc @ app/Svc.cs",
        [new("app/Svc.cs", "class Svc { public static void Run() {} } class Other {}"),
         new("core/Value.cs", "class Value { Other field; void Run() { Svc.Run(); } }")]);
        result.ParseErrors.ShouldBeEmpty();
        result.UnlistedCount.ShouldBe(1, "violation-count=1");
        result.Unlisted.ShouldContain(new Violation("core/Value.cs", "Other", "app/Svc.cs"), "violation:core/Value.cs -> Other @ app/Svc.cs");
        result.Listed.Length.ShouldBe(1, "listed=1");
        result.Listed[0].TypeName.ShouldBe("Svc", "body-reference-listed");
        result.Passes.ShouldBeFalse("verdict=fail");
    }

    [Test]
    public void Allowed_edges_listed_rows_comments_and_strings_do_not_fire()
    {
        var map = Edges + "\nhost/** Host\ncore/** Core\napp/** Application\ngit/** Git\nrunner/** Runners\ndata/** Persistence";
        var result = Analyze(map, "strict: true\n# slice test\napp/Listed.cs -> GitType @ git/GitType.cs",
        [new("host/Entry.cs", "class Entry { GitType g; Runner r; Service s; }"),
         new("core/Value.cs", "class Value {}"),
         new("core/Comment.cs", "// Service\n/* Service */\n#region Service\nclass Comment {}\n#endregion"),
         new("core/String.cs", "class Text { string s = \"Service\"; char c = 'S'; }"),
         new("core/Nested.cs", "class Outer { class Nested {} Nested field; }"),
         new("app/Service.cs", "class Service { Value v; Store s; }"),
         new("app/Listed.cs", "class Listed { GitType field; }"),
         new("git/GitType.cs", "class GitType { Value v; }"),
         new("runner/Runner.cs", "class Runner {}"), new("data/Store.cs", "class Store {}")]);
        result.Map.Errors.ShouldBeEmpty();
        result.ParseErrors.ShouldBeEmpty();
        result.Unlisted.Where(v => v.FromFile is "host/Entry.cs" or "app/Service.cs" or "git/GitType.cs").ShouldBeEmpty("findings=0:allowed-edge");
        result.Unlisted.Where(v => v.FromFile == "core/Comment.cs").ShouldBeEmpty("findings=0:comment");
        result.Unlisted.Where(v => v.FromFile == "core/String.cs").ShouldBeEmpty("findings=0:string");
        result.Unlisted.Where(v => v.FromFile == "core/Nested.cs").ShouldBeEmpty("findings=0:own-declaration");
        result.Listed.Length.ShouldBe(1, "listed=1");
        result.UnlistedCount.ShouldBe(0, "unlisted=0");
        result.Passes.ShouldBeTrue("verdict=pass");
    }

    [Test]
    public void Stale_row_and_unmapped_file_fail_in_strict_mode()
    {
        SourceFile[] files = [new("core/Value.cs", "class Value {}"), new("app/Svc.cs", "class Svc {}")];
        const string row = "core/Value.cs -> Svc @ app/Svc.cs";
        var stale = Analyze(SyntheticMap, "strict: true\n# slice test\n" + row, files);
        stale.Stale.ShouldContain(new Violation("core/Value.cs", "Svc", "app/Svc.cs"), "stale-row:" + row);
        stale.Passes.ShouldBeFalse("stale-verdict=fail");
        var warn = Analyze(SyntheticMap, "strict: false\n# slice test\n" + row, files);
        warn.Inventory.ShouldContain("stale-row:" + row);
        warn.Passes.ShouldBeTrue("warn-stale-verdict=pass");
        foreach (var mode in new[] { "true", "false" })
        {
            var unmapped = Analyze(SyntheticMap, "strict: " + mode, [.. files, new("other/Unknown.cs", "class Unknown {}")]);
            unmapped.Map.Errors.ShouldContain("unmapped-file:other/Unknown.cs", "unmapped-file:other/Unknown.cs");
            unmapped.Passes.ShouldBeFalse("unmapped-verdict=fail:" + mode);
        }
    }

    [Test]
    public void Ambiguous_simple_names_resolve_by_using_or_are_listed()
    {
        SourceFile[] declarations = [new("core/A.cs", "namespace X.Core; class Reader {}"), new("app/B.cs", "namespace X.App; class Reader {}")];
        var usingResult = Analyze(SyntheticMap, "strict: true", [.. declarations,
            new("core/C.cs", "using X.App; namespace X.Client; class C { Reader field; }")]);
        usingResult.UnlistedCount.ShouldBe(1, "violation-count=1:using");
        usingResult.Unlisted.ShouldContain(new Violation("core/C.cs", "Reader", "app/B.cs"));
        usingResult.Ambiguous.ShouldBeEmpty("using-resolved");
        var unknown = Analyze(SyntheticMap, "strict: true", [.. declarations,
            new("core/D.cs", "namespace X.Client; class D { Reader field; }")]);
        unknown.Ambiguous.Length.ShouldBe(1, "ambiguous=1:mixed");
        unknown.UnlistedCount.ShouldBe(0, "unlisted=0:mixed");
        unknown.Passes.ShouldBeTrue("mixed-verdict=pass");
        var disallowed = Analyze(Edges + "\ncore/** Core\napp/** Application\ngit/** Git", "strict: true",
        [new("app/A.cs", "namespace X.App; class Reader {}"), new("git/B.cs", "namespace X.Git; class Reader {}"),
         new("core/D.cs", "namespace X.Client; class D { Reader field; }")]);
        disallowed.Ambiguous.Length.ShouldBe(1, "ambiguous=1:disallowed");
        disallowed.UnlistedCount.ShouldBe(1, "unlisted=1:disallowed");
        disallowed.Passes.ShouldBeFalse("disallowed-verdict=fail");
    }

    [Test]
    public void Warn_mode_prints_the_inventory_and_fails_only_on_map_or_parse_errors()
    {
        SourceFile[] files = [new("core/Value.cs", "class Value { Svc s; Other o; }"), new("app/Svc.cs", "class Svc {} class Other {} class Unused {}")];
        var warn = Analyze(SyntheticMap, "strict: false\n# slice test\ncore/Value.cs -> Unused @ app/Svc.cs", files);
        warn.UnlistedCount.ShouldBe(2, "warn-unlisted=2");
        warn.Stale.Length.ShouldBe(1, "warn-stale=1");
        warn.Inventory.ShouldContain("core/Value.cs -> Svc @ app/Svc.cs");
        warn.Inventory.ShouldContain("core/Value.cs -> Other @ app/Svc.cs");
        warn.Inventory.ShouldContain("stale-row:core/Value.cs -> Unused @ app/Svc.cs");
        warn.Passes.ShouldBeTrue("warn-verdict=pass");
        var malformed = Analyze(SyntheticMap, "strict: false\nnot a row", files);
        malformed.ParseErrors.ShouldContain("malformed-row:not a row");
        malformed.Passes.ShouldBeFalse("parse-error-fails-in-warn-mode");
        var dead = Analyze(SyntheticMap + "\nmissing/** Git", "strict: false", files);
        dead.Map.Errors.ShouldContain("dead-rule:missing/**");
        dead.Passes.ShouldBeFalse("dead-rule-fails-in-warn-mode");
    }

    private static string Read(string name) => File.ReadAllText(Path.Combine(RepoRoot, "tests/Antiphon.Tests/Layering", name));
    private static string[] RepositoryPaths() => Directory.EnumerateFiles(Path.Combine(RepoRoot, "server"), "*.cs", SearchOption.AllDirectories)
        .Select(p => Path.GetRelativePath(RepoRoot, p).Replace('\\', '/'))
        .Where(p => !p.StartsWith("server/Migrations/", StringComparison.Ordinal)
            && !p.Split('/').Any(s => s is "bin" or "obj" || s.StartsWith("bin-", StringComparison.Ordinal)))
        .Order(StringComparer.Ordinal).ToArray();
    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Antiphon.sln"))) directory = directory.Parent;
            return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repo root (Antiphon.sln).");
        }
    }
}
