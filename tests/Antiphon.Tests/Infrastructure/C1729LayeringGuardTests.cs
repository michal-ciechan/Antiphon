using Antiphon.Tests.Layering;
using Antiphon.Tests.TestHelpers;
using System.Diagnostics;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Layering.LayeringGuardEngine;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
[ClassDataSource<C1729LayeringRepository>(Shared = SharedType.PerClass)]
public sealed class C1729LayeringGuardTests
{
    private readonly C1729LayeringRepository _repository;

    public C1729LayeringGuardTests(C1729LayeringRepository repository) => _repository = repository;

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
        var paths = _repository.Paths;
        paths.Length.ShouldBeGreaterThan(0, "nonempty-census");
        var map = _repository.Analysis.Map;
        map.Errors.ShouldBeEmpty("unmapped-file=0 dead-rule=0: " + string.Join("\n", map.Errors));
        map.Layers.Count.ShouldBe(paths.Length, "mapped=census");
    }

    [Test]
    public void Layer_of_pinned_files_follows_first_match()
    {
        var map = _repository.Analysis.Map;
        (string Path, string Layer)[] pins =
        [
            ("server/Program.cs", "Host"),
            ("server/Infrastructure/Agents/ZombieCensusJob.cs", "Host"),
            ("server/Infrastructure/Agents/HangfireConfiguration.cs", "Host"),
            ("server/Infrastructure/Agents/AgentExecutor.cs", "Integrations"),
            ("server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs", "Runners"),
            ("server/Infrastructure/Resilience/DatabaseResilienceExecutor.cs", "Persistence"),
            ("server/Infrastructure/Resilience/NpgsqlTransientFailureClassifier.cs", "Persistence"),
            ("server/Application/Services/ResilienceReadClients.cs", "Application"),
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
        var parsed = _repository.Analysis.Allowlist;
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
        var result = _repository.Analysis;
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
    public void Runtime_action_claim_query_has_no_application_dependency_even_in_warn_mode()
    {
        const string query = "server/Infrastructure/Data/SessionRuntimeActionClaimQuery.cs";
        var result = _repository.Analysis;
        _repository.Paths.ShouldContain(query, "the real claim query must be in the analyzed census");
        result.Unlisted.Concat(result.Listed)
            .Where(v => v.FromFile == query && v.ToFile.StartsWith("server/Application/", StringComparison.Ordinal))
            .ShouldBeEmpty("claim query parameters must come from its caller, including in warn mode");
        result.Ambiguous.Where(a => a.FromFile == query && a.IsViolation)
            .ShouldBeEmpty("ambiguous application references must not bypass the claim query guard");
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
        var attribute = Analyze(SyntheticMap, "strict: true",
        [new("app/Svc.cs", "namespace X; class MarkerAttribute : System.Attribute {} class Svc {}"),
         new("core/Value.cs", "using X; [X.Marker] class Value { string Name() => nameof(Svc); }")]);
        attribute.Unlisted.ShouldContain(new Violation("core/Value.cs", "MarkerAttribute", "app/Svc.cs"), "qualified-attribute-suffix");
        attribute.Unlisted.ShouldContain(new Violation("core/Value.cs", "Svc", "app/Svc.cs"), "nameof-counts");
        attribute.Passes.ShouldBeFalse("attribute-and-nameof-verdict");

        (string Label, string Text)[] shadowing =
        [
            ("color-color-fires", "class Value { Svc Svc { get; } }"),
            ("parameter-color-color-fires", "class Value { void M(Svc Svc) {} }"),
            ("local-color-color-fires", "class Value { void M() { Svc Svc = null; } }"),
            ("creation-with-shadowing-member-fires", "class Value { object Svc; object M() => new Svc(); }"),
            ("qualified-with-shadowing-member-fires", "class Value { object Svc; X.App.Svc Other; }"),
            ("qualified-expression-with-shadowing-member-fires", "class Value { object Svc; void M() { X.App.Svc.Run(); } }"),
            ("global-qualified-with-shadowing-member-fires", "class Value { object Svc; global::X.App.Svc Other; }"),
            ("qualified-with-own-nested-type-fires", "class Value { class Svc {} X.App.Svc Other; }"),
            ("static-call-shadow-elsewhere-fires", "class Value { void M(int Svc) {} void N() => Svc.Run(); }"),
            ("property-of-other-class-fires", "class Other { int Svc { get; } } class Value { void M() => Svc.Run(); }"),
            ("enum-member-elsewhere-fires", "enum Other { Svc } class Value { int M() => Svc.K; }"),
            ("nameof-shadow-elsewhere-fires", "class Other { int Svc; } class Value { string M() => nameof(Svc); }"),
            ("not-pattern-shadow-elsewhere-fires", "class Other { int Svc; } class Value { bool M(object o) => o is not Svc; }"),
            ("or-pattern-shadow-elsewhere-fires", "class Other { int Svc; } class Value { bool M(object o) => o is Svc or string; }"),
            ("switch-pattern-shadow-elsewhere-fires", "class Other { int Svc; } class Value { int M(object o) => o switch { Svc => 1, _ => 0 }; }"),
            ("positional-parameter-of-other-record-fires", "class Value { static int M(dynamic h) => h.Kind == Svc.K ? 1 : 0; } record Dto(string Svc);"),
            ("local-of-other-member-fires", "class Value { void M() { int Svc = 0; } void N() => Svc.Run(); }"),
            ("local-of-sibling-block-fires", "class Value { void M() { { int Svc = 0; } Svc.Run(); } }"),
            ("pattern-of-other-member-fires", "class Value { bool M(object o) => o is int Svc; void N() => Svc.Run(); }"),
            ("lambda-parameter-elsewhere-fires", "class Value { object M() => (int Svc) => Svc; void N() => Svc.Run(); }"),
            ("type-parameter-elsewhere-fires", "class Other<Svc> {} class Value { Svc field; }")
        ];
        var missed = new List<string>();
        foreach (var (label, text) in shadowing)
        {
            var shadowed = Analyze(SyntheticMap, "strict: true",
            [new("app/Svc.cs", "namespace X.App; class Svc { public static void Run() {} }"),
             new("core/Value.cs", "using X.App; " + text)]);
            shadowed.Map.Errors.ShouldBeEmpty(label);
            shadowed.ParseErrors.ShouldBeEmpty(label);
            if (shadowed.Unlisted.Length != 1
                || !shadowed.Unlisted.Contains(new Violation("core/Value.cs", "Svc", "app/Svc.cs"))
                || shadowed.Passes)
                missed.Add(label + ": " + shadowed.Inventory + " passes=" + shadowed.Passes);
        }
        missed.ShouldBeEmpty("every shadowed type reference must fail strict mode");
    }

    [Test]
    public void Allowed_edges_listed_rows_comments_and_strings_do_not_fire()
    {
        var map = Edges + "\nhost/** Host\ncore/** Core\napp/** Application\ngit/** Git\nrunner/** Runners\ndata/** Persistence";
        var result = Analyze(map, "strict: true\n# slice test\napp/Listed.cs -> GitType @ git/GitType.cs",
        [new("host/Entry.cs", "class Entry { GitType g; Runner r; Service s; }"),
         new("core/Value.cs", "class Value {} enum Status { Pending }"),
         new("core/Member.cs", "class Member { Status status = Status.Pending; dynamic value; object Read() { object Outcome() => value.Outcome; return Outcome(); } }"),
         new("core/Comment.cs", "// Service\n/* Service */\n#region Service\nclass Comment {}\n#endregion"),
         new("core/String.cs", "class Text { string s = \"Service\"; char c = 'S'; }"),
         new("core/Nested.cs", "class Outer { class Nested {} Nested field; }"),
         new("app/Service.cs", "class Service { Value v; Store s; } class Pending {} class Outcome {} class Nested {}"),
         new("app/Listed.cs", "class Listed { GitType field; }"),
         new("git/GitType.cs", "class GitType { Value v; }"),
         new("runner/Runner.cs", "class Runner {}"), new("data/Store.cs", "class Store {}")]);
        result.Map.Errors.ShouldBeEmpty();
        result.ParseErrors.ShouldBeEmpty();
        result.Unlisted.Where(v => v.FromFile is "host/Entry.cs" or "app/Service.cs" or "git/GitType.cs").ShouldBeEmpty("findings=0:allowed-edge");
        result.Unlisted.Where(v => v.FromFile == "core/Comment.cs").ShouldBeEmpty("findings=0:comment");
        result.Unlisted.Where(v => v.FromFile == "core/String.cs").ShouldBeEmpty("findings=0:string");
        result.Unlisted.Where(v => v.FromFile == "core/Nested.cs").ShouldBeEmpty("findings=0:own-declaration");
        result.Ambiguous.Where(v => v.FromFile == "core/Nested.cs").ShouldBeEmpty("own-declaration-not-ambiguous");
        result.Unlisted.Where(v => v.FromFile == "core/Member.cs").ShouldBeEmpty("findings=0:value-members");
        result.Listed.Length.ShouldBe(1, "listed=1");
        result.UnlistedCount.ShouldBe(0, "unlisted=0");
        result.Passes.ShouldBeTrue("verdict=pass");

        (string Label, string Text)[] memberNames =
        [
            ("initializer-member", "class Value { object M() => new Holder { Kind = 0 }; }"),
            ("with-member", "class Value { object M(Holder value) => value with { Status = true }; }"),
            ("named-argument", "class Value { void M() { F(Active: true); } }"),
            ("property-pattern", "class Value { bool M(object value) => value is { Status: true }; }"),
            ("attribute-member", "[Marker(Kind = 0)] class Value {}")
        ];
        var falseReferences = new List<string>();
        foreach (var (label, text) in memberNames)
        {
            var members = Analyze(SyntheticMap, "strict: true",
            [new("app/Members.cs", "class Kind {} class Active {} class Status {}"),
             new("core/Holder.cs", "record Holder { public int Kind { get; init; } public bool Status { get; init; } } class MarkerAttribute : System.Attribute { public int Kind { get; set; } }"),
             new("core/Value.cs", text)]);
            members.Map.Errors.ShouldBeEmpty(label);
            members.ParseErrors.ShouldBeEmpty(label);
            if (members.UnlistedCount != 0 || members.Ambiguous.Length != 0 || !members.Passes)
                falseReferences.Add("findings=0:" + label + ": " + members.Inventory);
        }
        falseReferences.ShouldBeEmpty("member labels are not type references");

        (string Label, string Text)[] scopedValues =
        [
            ("same-scope-value-member", "class Value { dynamic Svc; object M() => Svc.Run(); }"),
            ("same-scope-property", "class Value { dynamic Svc { get; } object M() => Svc.Run(); }"),
            ("same-scope-parameter", "class Value { object M(dynamic Svc) => Svc.Run(); }"),
            ("same-scope-local", "class Value { object M() { dynamic Svc = null; return Svc.Run(); } }"),
            ("same-scope-local-function", "class Value { object M() { int Svc() => 1; return Svc(); } }"),
            ("same-scope-lambda-parameter", "class Value { object M() => (dynamic Svc) => Svc.Run(); }"),
            ("same-scope-pattern-variable", "class Value { bool M(object o) => o is int Svc && Svc > 0; }"),
            ("same-scope-switch-variable", "class Value { int M(object o) => o switch { int Svc => Svc, _ => 0 }; }"),
            ("same-scope-primary-parameter", "class Value(int Svc) { int M() => Svc; }"),
            ("same-scope-enum-member", "enum Value { Svc = 1, Other = Svc }"),
            ("same-scope-foreach-variable", "class Value { void M(int[] xs) { foreach (var Svc in xs) { F(Svc); } } }"),
            ("same-scope-catch-variable", "class Value { void M() { try {} catch (System.Exception Svc) { F(Svc); } } }"),
            ("same-scope-generic-type-parameter", "class Value<Svc> { Svc field; object M() => default(Svc); }"),
            ("same-scope-generic-method-parameter", "class Value { Svc M<Svc>(Svc s) => s; }")
        ];
        var falseScopedReferences = new List<string>();
        foreach (var (label, text) in scopedValues)
        {
            var scoped = Analyze(SyntheticMap, "strict: true",
            [new("app/Svc.cs", "class Svc {}"), new("core/Value.cs", text)]);
            scoped.Map.Errors.ShouldBeEmpty(label);
            scoped.ParseErrors.ShouldBeEmpty(label);
            if (scoped.UnlistedCount != 0 || scoped.Ambiguous.Length != 0 || !scoped.Passes)
                falseScopedReferences.Add("findings=0:" + label + ": " + scoped.Inventory);
        }
        falseScopedReferences.ShouldBeEmpty("only enclosing declarations can name values or type parameters");
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
        var partial = Analyze(SyntheticMap, "strict: true",
        [new("app/Reader.cs", "namespace X.App; partial class Reader {}"),
         new("app/Reader.Part.cs", "namespace X.App; partial class Reader {}"),
         new("core/C.cs", "using X.App; class C { Reader field; }")]);
        partial.Ambiguous.ShouldBeEmpty("partial-is-one-type");
        partial.Unlisted.ShouldContain(new Violation("core/C.cs", "Reader", "app/Reader.cs"), "partial-primary-file");
        partial.Passes.ShouldBeFalse("partial-forbidden-edge");
        var globalUsing = Analyze(SyntheticMap, "strict: true", [.. declarations,
            new("app/Usings.cs", "global using X.App;"),
            new("core/C.cs", "namespace X.Client; class C { Reader field; }")]);
        globalUsing.Ambiguous.ShouldBeEmpty("global-using-resolved");
        globalUsing.Unlisted.ShouldContain(new Violation("core/C.cs", "Reader", "app/B.cs"), "global-using-target");
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
        var split = Analyze(SyntheticMap, "strict: false",
        [new("core/Reader.cs", "namespace X; partial class Reader {}"),
         new("app/Reader.cs", "namespace X; partial class Reader {}")]);
        split.ParseErrors.ShouldContain("split-type:X.Reader", "partial-split-refused");
        split.Passes.ShouldBeFalse("split-type-fails-in-warn-mode");
        var syntax = Analyze(SyntheticMap, "strict: false",
        [new("core/Value.cs", "class Value {"), new("app/Svc.cs", "class Svc {}")]);
        syntax.ParseErrors.ShouldNotBeEmpty("syntax-error-detected");
        syntax.Passes.ShouldBeFalse("syntax-error-fails-in-warn-mode");
    }

}

/// <summary>One tracked census and syntax analysis shared by all real-tree assertions.</summary>
public sealed class C1729LayeringRepository
{
    internal string[] Paths { get; }
    internal Result Analysis { get; }

    public C1729LayeringRepository()
    {
        var clock = Stopwatch.StartNew();
        Paths = RepositoryPaths();
        var census = clock.Elapsed;
        var files = Paths.Select(p => new SourceFile(p, File.ReadAllText(Path.Combine(RepoRoot, p)))).ToArray();
        var map = Read("layers.txt");
        var allowlist = Read("allowlist.txt");
        var read = clock.Elapsed - census;
        Analysis = Analyze(map, allowlist, files);
        Console.WriteLine($"LAYERING files={Paths.Length} census={census.TotalSeconds:F3}s read={read.TotalSeconds:F3}s analysis={(clock.Elapsed - census - read).TotalSeconds:F3}s total={clock.Elapsed.TotalSeconds:F3}s");
    }

    private static string Read(string name) => File.ReadAllText(Path.Combine(RepoRoot, "tests/Antiphon.Tests/Layering", name));
    private static string[] RepositoryPaths()
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[] { "-c", "core.fsmonitor=false", "ls-files", "-z", "--", "server" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start git ls-files.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5_000);
            throw new TimeoutException("git ls-files exceeded 15 seconds.");
        }
        var paths = output.GetAwaiter().GetResult();
        var diagnostic = error.GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new InvalidOperationException("git ls-files failed: " + diagnostic);
        return paths.Split('\0', StringSplitOptions.RemoveEmptyEntries)
        .Where(p => p.EndsWith(".cs", StringComparison.Ordinal))
        .Where(p => !p.StartsWith("server/Migrations/", StringComparison.Ordinal)
            && !p.Split('/').Any(s => s is "bin" or "obj" || s.StartsWith("bin-", StringComparison.Ordinal)))
        .Order(StringComparer.Ordinal).ToArray();
    }
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
