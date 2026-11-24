using System.Text;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1526: the check-os-witnesses command (Antiphon.Nightly.OsWitnessCheck, which replaced scripts/check-os-witnesses.ps1 in
/// unit 35 of the 2026-11-22 plan) in process against disposable Git fixtures, through its production git. The rows keep the
/// names of the retired portable driver scripts/test-os-witnesses.ps1 (deleted with the unreferenced batch, f47da556e). Its
/// strict-mode rows (strict-valid-absent, strict-whole-suite) checked PowerShell's Set-StrictMode and have no C# counterpart; the
/// optional witnessId they guarded is the non-behavior-without-witness-id row. Nothing is executed from the evidence.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class OsWitnessCheckTests
{
    private static readonly UTF8Encoding Utf8 = new(false);

    // Lint inputs, not execution receipts: real code shapes pinned in Git (LF, whatever this file's checkout uses).
    private static readonly string RealTest = """
        using System.Diagnostics;
        class WitnessTests {
            [Test] public void AbsentOptOut() {
                var environment = new ProcessStartInfo().EnvironmentVariables;
                environment.Remove("OPT_OUT");
                Assert.False(environment.ContainsKey("OPT_OUT"));
                ReadOptional(environment, windows: true);
            }
            [Test] public void EmptyOptOut() {
                var environment = new ProcessStartInfo().EnvironmentVariables;
                environment["OPT_OUT"] = "";
                Assert.Equal("", environment["OPT_OUT"]);
                ReadOptional(environment, windows: true);
            }
            [Test] public void ValueOptOut() {
                var environment = new ProcessStartInfo().EnvironmentVariables;
                environment["OPT_OUT"] = "1";
                Assert.Equal("1", environment["OPT_OUT"]);
                ReadOptional(environment, windows: true);
            }
        }

        """.ReplaceLineEndings("\n");

    private const string Seam = "new StringDictionary()";

    [Test]
    public void C1526_Forced_real_type_reads_only_the_pinned_revisions() => Case(4, (f, rows) =>
    {
        var valid = f.Valid();
        rows.Expect(f.Run(valid), 0, "complete syntax/inventory", "forced-real-type");

        f.Put("source.cs", "// unstaged source must not hide the pinned candidate");
        rows.Expect(f.Run(valid), 0, "CANDIDATE head source.cs:1", "pinned-source");
        f.Put("source.cs", "if (OperatingSystem.IsWindows()) ReadOptional();");

        f.Put("test.cs", "// uncommitted deletion of witness must not replace the pinned test");
        rows.Expect(f.Run(valid), 0, "complete syntax/inventory", "pinned-test-ignores-worktree");
        f.Put("test.cs", RealTest);

        f.Put("seam.cs", RealTest);
        rows.Expect(f.Run(WithTest(valid, "seam.cs")), 1, "productionType missing from test code", "pinned-test-rejects-worktree-repair");
        f.Put("seam.cs", RealTest.Replace("new ProcessStartInfo().EnvironmentVariables", Seam));
    });

    [Test]
    public void C1526_The_lexical_view_masks_comments_and_strings_and_refuses_ambiguity() => Case(22, (f, rows) =>
    {
        var valid = f.Valid();
        foreach (var (name, path, exit, diagnostic) in new[]
        {
            ("test-unterminated-csharp-block", "unterminated-csharp.cs", 2, "unterminated /* block comment"),
            ("test-unterminated-powershell-block", "unterminated-powershell.ps1", 2, "unterminated <# block comment"),
            ("test-nested-powershell-block", "nested-powershell.ps1", 1, "testCase declaration missing"),
            ("test-nested-powershell-inner-close-only", "nested-unclosed-powershell.ps1", 2, "unterminated <# block comment"),
            ("test-closed-powershell-block", "closed-powershell.ps1", 1, "testCase declaration missing"),
            ("test-csharp-block-does-not-nest", "csharp-nonnesting.cs", 0, "complete syntax/inventory"),
            ("test-csharp-string-comment-delimiters", "string-delimiters.cs", 0, "complete syntax/inventory"),
            ("test-powershell-string-comment-delimiters", "string-delimiters.ps1", 0, "complete syntax/inventory"),
            ("test-csharp-escaped-string-delimiters", "escaped-strings.cs", 0, "complete syntax/inventory"),
            ("test-powershell-escaped-string-delimiters", "escaped-strings.ps1", 0, "complete syntax/inventory"),
            ("test-unterminated-string", "unterminated-string.cs", 2, "unterminated string literal"),
            ("test-unterminated-block-after-code", "unterminated-comment-after-code.cs", 2, "unterminated /* block comment"),
            ("test-seam-only", "seam.cs", 1, "productionType missing from test code"),
            ("test-always-assigned", "assigned.cs", 1, "absent setupAssertion missing from test code"),
            ("test-comment-only", "comment-only.cs", 1, "testCase declaration missing"),
            ("test-comment-strings", "comment-strings.cs", 1, "testCase declaration missing"),
            ("test-line-comment-strings", "line-comments.cs", 1, "testCase declaration missing"),
            ("test-hash-comment-strings", "hash-comments.ps1", 1, "testCase declaration missing"),
            ("test-factory-missing", "factory-missing.cs", 1, "factory missing from test code"),
            ("test-name-call-only", "method-call.cs", 1, "testCase declaration missing"),
            ("test-name-prefix", "method-prefix.cs", 1, "testCase declaration missing"),
        })
            rows.Expect(f.Run(WithTest(valid, path)), exit, diagnostic, name);

        var missingCase = Copy(valid);
        Witness(missingCase)["testCase"] = "MissingOptOut";
        rows.Expect(f.Run(missingCase), 1, "testCase declaration missing", "test-case-missing");
    });

    [Test]
    public void C1526_Declaration_records_name_each_missing_or_contradictory_obligation() => Case(20, (f, rows) =>
    {
        var valid = f.Valid();
        JsonObject Edit(Action<JsonObject> change)
        {
            var copy = Copy(valid);
            change(copy);
            return copy;
        }

        rows.Expect(f.Run(Edit(e => e["inspectedPaths"] = "not-an-array")), 2, "invalid array", "malformed-record");
        rows.Expect(f.Run(Edit(e => Witness(e)["testPath"] = "../outside.ps1")), 1, "invalid path", "unsafe-path");
        rows.Expect(f.Run(Edit(e => Witness(e)["witnessKind"] = "seam")), 1, "real-type required", "seam-only");
        rows.Expect(f.Run(Edit(e =>
        {
            Witness(e)["executionMode"] = "native";
            Witness(e)["nativeObligation"] = new JsonObject { ["status"] = "pending", ["targetOS"] = "Windows", ["sourceSha"] = f.Head, ["command"] = "native test", ["reason"] = "native API" };
        })), 0, "NATIVE pending", "native-pending");
        rows.Expect(f.Run(Edit(e => e["witnesses"] = new JsonArray())), 1, "missing witness", "missing-witness");
        rows.Expect(f.Run(Edit(e =>
        {
            var absent = Cases(e)[0]!.AsObject();
            absent["keyPresent"] = true;
            absent["value"] = "";
        })), 1, "absent requires removal", "empty-is-not-absent");
        rows.Expect(f.Run(Edit(e => Cases(e).RemoveAt(0))), 1, "absent, empty and value", "absent-case-required");
        rows.Expect(f.Run(Edit(e => Inventory(e)["candidates"] = new JsonArray())), 1, "candidate unclassified", "candidate-mismatch");
        rows.Expect(f.Run(Edit(e => Inventory(e)["disposition"] = "portable")), 1, "witness requires os-gated", "candidate-contradiction");

        // The candidate deliberately has no witnessId member, not an empty one.
        var reasoned = Edit(e =>
        {
            Inventory(e)["disposition"] = "portable";
            Inventory(e)["candidates"] = new JsonArray(new JsonObject { ["revision"] = "head", ["line"] = 1, ["disposition"] = "non-behavior", ["reason"] = "unchanged guard, changed comment only" });
            e["witnesses"] = new JsonArray();
        });
        rows.Expect(f.Run(reasoned), 0, "CANDIDATE head source.cs:1", "candidate-reason");
        rows.Check(!Inventory(reasoned)["candidates"]![0]!.AsObject().ContainsKey("witnessId"), "non-behavior-without-witness-id setup");
        rows.Expect(f.Run(reasoned), 0, "CANDIDATE head source.cs:1", "non-behavior-without-witness-id");

        var helper = Edit(e => Inventory(e)["helpers"] = new JsonArray("helper.cs"));
        rows.Expect(f.Run(helper), 1, "helper not inspected", "helper-missing");
        ((JsonArray)helper["inspectedPaths"]!).Add(new JsonObject
        {
            ["path"] = "helper.cs", ["disposition"] = "os-gated", ["reason"] = "called by changed code", ["helpers"] = new JsonArray(),
            ["candidates"] = new JsonArray(
                new JsonObject { ["revision"] = "base", ["line"] = 1, ["disposition"] = "witness", ["witnessId"] = "W-2" },
                new JsonObject { ["revision"] = "head", ["line"] = 1, ["disposition"] = "witness", ["witnessId"] = "W-2" }),
        });
        var second = Witness(valid).DeepClone().AsObject();
        second["id"] = "W-2";
        second["sourcePath"] = "helper.cs";
        ((JsonArray)helper["witnesses"]!).Add(second);
        rows.Expect(f.Run(helper), 0, "CANDIDATE base helper.cs:1", "helper-complete");

        rows.Expect(f.Run(Edit(e => e["headSha"] = new string('f', 40))), 2, "SHA mismatch", "sha-mismatch");
        rows.Expect(f.Run(valid, head: new string('f', 40)), 2, "unresolved commit", "unresolved-ref");
        rows.Expect(f.Run(Edit(e => Witness(e)["testPath"] = "missing.ps1")), 1, "path missing", "missing-path");
        rows.Expect(f.Run(Edit(e => Witness(e)["checkpointId"] = "CP-999")), 1, "checkpoint missing", "missing-checkpoint");
        rows.Expect(f.Run(Edit(e => ((JsonArray)e["inspectedPaths"]!).Add(Inventory(e).DeepClone()))), 1, "duplicate inventory", "duplicate-inventory");
        rows.Expect(f.Run(Edit(e => Witness(e)["nativeObligation"] = new JsonObject
        {
            ["status"] = "recorded", ["targetOS"] = "Windows", ["sourceSha"] = f.Head, ["command"] = "native test", ["receiptReference"] = "fixture.json",
            ["executionMode"] = "portable-forced",
        })), 1, "native receipt requires native execution", "fixture-native");
    });

    [Test]
    public void C1526_The_restart_census_follows_committed_renames_deletes_and_unknown_files() => Case(5, (f, rows) =>
    {
        // Rename and delete are separate committed deltas; the checker reads no worktree source.
        f.Git("mv", "scripts/lib/start.cs", "moved.cs");
        f.Git("commit", "-qam", "rename");
        var rename = f.Git("rev-parse", "HEAD");
        var e = f.Valid();
        e["headSha"] = rename;
        var inspected = (JsonArray)e["inspectedPaths"]!;
        inspected.Add(new JsonObject { ["path"] = "scripts/lib/start.cs", ["disposition"] = "removed", ["reason"] = "renamed", ["helpers"] = new JsonArray(), ["candidates"] = new JsonArray() });
        inspected.Add(new JsonObject { ["path"] = "moved.cs", ["disposition"] = "portable", ["reason"] = "renamed startup", ["helpers"] = new JsonArray(), ["candidates"] = new JsonArray() });
        rows.Expect(f.Run(e, head: rename), 1, "restart path census", "restart-rename-missing");

        var native = new JsonObject { ["status"] = "pending", ["targetOS"] = "Windows", ["sourceSha"] = rename, ["command"] = "rc-native-probe", ["reason"] = "future native integration" };
        e["restartImpact"] = new JsonObject
        {
            ["required"] = true, ["paths"] = new JsonArray("scripts/lib/start.cs"), ["additionalPaths"] = new JsonArray(), ["reason"] = "renamed startup",
            ["nativeObligation"] = native,
        };
        rows.Expect(f.Run(e, head: rename), 0, "NATIVE pending", "restart-rename-pending");

        f.Git("rm", "-q", "moved.cs");
        f.Git("commit", "-qm", "delete");
        var deleted = f.Git("rev-parse", "HEAD");
        e["headSha"] = deleted;
        native["sourceSha"] = deleted;
        inspected.RemoveAt(inspected.Count - 1);
        rows.Expect(f.Run(e, head: deleted), 0, "complete syntax/inventory", "restart-delete");

        f.Put("unknown.widget", "unrecognized executable/configuration candidate");
        f.Git("add", ".");
        f.Git("commit", "-qm", "unknown");
        var unknown = f.Git("rev-parse", "HEAD");
        e["headSha"] = unknown;
        native["sourceSha"] = unknown;
        rows.Expect(f.Run(e, head: unknown), 1, "inventory missing: unknown.widget", "unknown-classification");

        // Documentation-only uses a second base at the current commit.
        f.Put("readme.md", "# documentation");
        f.Git("add", "readme.md");
        f.Git("commit", "-qm", "docs");
        var docs = f.Git("rev-parse", "HEAD");
        var documentation = new JsonObject
        {
            ["version"] = 1, ["baseSha"] = unknown, ["headSha"] = docs, ["inspectedPaths"] = new JsonArray(), ["witnesses"] = new JsonArray(),
            ["restartImpact"] = new JsonObject { ["required"] = false, ["paths"] = new JsonArray(), ["additionalPaths"] = new JsonArray(), ["reason"] = "documentation-only readme.md" },
        };
        rows.Expect(f.Run(documentation, @base: unknown, head: docs), 0, "complete syntax/inventory", "documentation-only");
    });

    [Test]
    public void C1526_Refs_evidence_and_flags_are_refused_before_any_lint() => Case(5, (f, rows) =>
    {
        var valid = f.Valid();
        rows.Expect(f.Run(valid, head: "HEAD"), 2, "full commit SHAs required", "short-ref");
        var unreadable = Copy(valid);
        unreadable["version"] = 2;
        rows.Expect(f.Run(unreadable), 2, "invalid version/object", "version");
        var plan = f.Run(valid, plan: "../plan.md");
        rows.Expect(plan, 2, "invalid plan path", "plan-path");
        rows.Check(plan.Text.Contains("OBLIGATION invalid path: ../plan.md", StringComparison.Ordinal), "plan-path names the path", plan.Text);
        var usage = Should.Throw<NightlyUsageException>(() => OsWitnessCheckOptions.Parse(["--base-ref", f.Base, "--head-ref", f.Head, "--plan", "plan.md"]));
        rows.Check(usage.Message.Contains("--evidence-path", StringComparison.Ordinal), "usage names the missing flag", usage.Message);
    });

    [Test]
    public void C1526_The_testing_doc_names_the_command()
    {
        var testing = File.ReadAllText(Path.Combine(DockerStackDocuments.RepoRoot, "docs", "testing-and-build.md"));
        testing.ShouldContain("## OS branch witnesses");
        testing.ShouldContain("scripts/antiphon check-os-witnesses --base-ref <full-base-sha> --head-ref <full-head-sha> --evidence-path <ignored-json> --plan <repo-relative-plan.md>");
        testing.ShouldNotContain("check-os-witnesses.ps1");
        File.ReadAllBytes(Path.Combine(DockerStackDocuments.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "OsWitnessCheck.cs")).ShouldAllBe(b => b < 128);
    }

    private static JsonObject Copy(JsonObject value) => value.DeepClone().AsObject();

    private static JsonObject WithTest(JsonObject valid, string path)
    {
        var copy = Copy(valid);
        Witness(copy)["testPath"] = path;
        return copy;
    }

    private static JsonObject Witness(JsonObject evidence) => evidence["witnesses"]![0]!.AsObject();

    private static JsonObject Inventory(JsonObject evidence) => evidence["inspectedPaths"]![0]!.AsObject();

    private static JsonArray Cases(JsonObject evidence) => Witness(evidence)["input"]!["cases"]!.AsArray();

    private static void Case(int expectedRows, Action<Fixture, Rows> body)
    {
        var root = Path.Combine(Path.GetTempPath(), "c1526-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var rows = new Rows();
            body(Fixture.Create(root), rows);
            rows.Inner.ShouldAllPass(expectedRows);
        }
        finally
        {
            ReadOnlyDirectory.Delete(root);
        }
    }

    private sealed record Result(int Exit, string Text);

    private sealed class Rows
    {
        public NightlyCaseRows Inner { get; } = new();

        public void Check(bool ok, string name, string detail = "") => Inner.Check(ok, "C1526 " + name, detail);

        public void Expect(Result result, int exit, string diagnostic, string name) =>
            Check(result.Exit == exit && result.Text.Contains(diagnostic, StringComparison.Ordinal), name, "exit " + result.Exit + " expected " + exit + " with '" + diagnostic + "'\n" + result.Text);
    }

    private sealed class Fixture
    {
        private readonly string _evidence;

        private Fixture(string root)
        {
            Repo = Path.Combine(root, "repo");
            _evidence = Path.Combine(root, "evidence.json");
        }

        public string Repo { get; }

        public string Base { get; private set; } = "";

        public string Head { get; private set; } = "";

        public static Fixture Create(string root)
        {
            var f = new Fixture(root);
            Directory.CreateDirectory(f.Repo);
            f.Git("init", "-q", "-b", "master");
            f.Git("config", "user.email", "fixture@example.invalid");
            f.Git("config", "user.name", "Fixture");
            f.Git("config", "commit.gpgsign", "false");
            f.Git("config", "core.autocrlf", "false");
            f.Put("plan.md", "### Checkpoints\n\n| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |\n|---|---|---|---|---|---|---|---|---|\n| CP-1 | all | n/a | fixture | dotnet test | V-1 | pass | n/a | 1 |\n");
            f.Put("source.cs", "// base\n");
            f.Put("test.cs", RealTest);
            f.Put("seam.cs", RealTest.Replace("new ProcessStartInfo().EnvironmentVariables", Seam));
            f.Put("assigned.cs", RealTest.Replace("environment.Remove(\"OPT_OUT\");", "environment[\"OPT_OUT\"] = \"\";")
                .Replace("Assert.False(environment.ContainsKey(\"OPT_OUT\"));", "Assert.Equal(\"\", environment[\"OPT_OUT\"]);"));
            f.Put("comment-only.cs", "// fixture with no test or runtime object");
            f.Put("comment-strings.cs", "/*\n" + RealTest + "\n*/");
            f.Put("line-comments.cs", string.Join("\n", RealTest.Split('\n').Select(line => "// " + line)));
            f.Put("hash-comments.ps1", string.Join("\n", RealTest.Split('\n').Select(line => "# " + line)));
            f.Put("unterminated-csharp.cs", "/*\n" + RealTest);
            f.Put("unterminated-powershell.ps1", "<#\n" + RealTest);
            f.Put("nested-powershell.ps1", "<# outer\n<# inner #>\n" + RealTest + "\n#>");
            f.Put("nested-unclosed-powershell.ps1", "<# outer\n<# inner #>\n" + RealTest);
            f.Put("closed-powershell.ps1", "<#\n" + RealTest + "\n#>");
            f.Put("csharp-nonnesting.cs", "/* nested opener /* is only text */\n" + RealTest);
            f.Put("string-delimiters.cs", "class Strings { string slash = \"/*\"; string hash = \"<#\"; string verbatim = @\"/* <#\"; }\n" + RealTest);
            f.Put("string-delimiters.ps1", "$slash = '/*'; $hash = \"<#\"\nfunction AbsentOptOut {\n" + RealTest + "\n}");
            f.Put("escaped-strings.cs", "class Strings { string text = \"escaped \\\" /* <#\"; string verbatim = @\"doubled \"\" /* <#\"; }\n" + RealTest);
            f.Put("escaped-strings.ps1", "$single = 'doubled '' /* <#'; $double = \"escaped `\" /* <#\"\nfunction AbsentOptOut {\n" + RealTest + "\n}");
            f.Put("unterminated-string.cs", "var text = \"/* <#\n" + RealTest);
            f.Put("unterminated-comment-after-code.cs", RealTest + "\n/*");
            f.Put("factory-missing.cs", RealTest.Replace("new ProcessStartInfo().EnvironmentVariables", "new ProcessStartInfo().Environment"));
            f.Put("method-call.cs", RealTest.Replace("[Test] public void AbsentOptOut() {", "[Test] public void AnotherCase() { AbsentOptOut();"));
            f.Put("method-prefix.cs", RealTest.Replace("AbsentOptOut()", "AbsentOptOutExtra()"));
            f.Put("helper.cs", "if (OperatingSystem.IsWindows()) ReadOptional();");
            f.Put("scripts/lib/start.cs", "Start();");
            f.Git("add", ".");
            f.Git("commit", "-qm", "base");
            f.Base = f.Git("rev-parse", "HEAD");
            f.Put("source.cs", "if (OperatingSystem.IsWindows()) ReadOptional();");
            f.Git("add", ".");
            f.Git("commit", "-qm", "head");
            f.Head = f.Git("rev-parse", "HEAD");
            return f;
        }

        public void Put(string relative, string text)
        {
            var full = Path.Combine(Repo, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text, Utf8);
        }

        public string Git(params string[] arguments)
        {
            var result = NightlyNativeGit.Run(Repo, arguments);
            result.ExitCode.ShouldBe(0, "git " + string.Join(" ", arguments) + " -> " + result.Output);
            return result.Output.Trim();
        }

        // The forced real-type declaration of source.cs's changed optional read, witnessed by test.cs.
        public JsonObject Valid() => new()
        {
            ["version"] = 1,
            ["baseSha"] = Base,
            ["headSha"] = Head,
            ["inspectedPaths"] = new JsonArray(new JsonObject
            {
                ["path"] = "source.cs", ["disposition"] = "os-gated", ["reason"] = "changed optional read", ["helpers"] = new JsonArray(),
                ["candidates"] = new JsonArray(new JsonObject { ["revision"] = "head", ["line"] = 1, ["disposition"] = "witness", ["witnessId"] = "W-1" }),
            }),
            ["witnesses"] = new JsonArray(new JsonObject
            {
                ["id"] = "W-1", ["sourcePath"] = "source.cs", ["branchAnchor"] = "OperatingSystem.IsWindows()", ["targetOS"] = "Windows",
                ["witnessKind"] = "real-type", ["productionType"] = "ProcessStartInfo", ["factory"] = "new ProcessStartInfo().EnvironmentVariables",
                ["testPath"] = "test.cs", ["testCase"] = "AbsentOptOut", ["setup"] = "Remove the key from the real property",
                ["outcome"] = "policy applies without exception", ["executionMode"] = "portable-forced", ["checkpointId"] = "CP-1",
                ["nativeObligation"] = new JsonObject { ["status"] = "not-required", ["reason"] = "existing OS parameter is forceable" },
                ["input"] = new JsonObject
                {
                    ["kind"] = "optional-config", ["key"] = "OPT_OUT",
                    ["cases"] = new JsonArray(
                        new JsonObject { ["state"] = "absent", ["keyPresent"] = false, ["setupAssertion"] = "Assert.False(environment.ContainsKey(\"OPT_OUT\"))" },
                        new JsonObject { ["state"] = "empty", ["keyPresent"] = true, ["value"] = "", ["setupAssertion"] = "Assert.Equal(\"\", environment[\"OPT_OUT\"])" },
                        new JsonObject { ["state"] = "value", ["keyPresent"] = true, ["value"] = "1", ["setupAssertion"] = "Assert.Equal(\"1\", environment[\"OPT_OUT\"])" }),
                },
            }),
            ["restartImpact"] = new JsonObject { ["required"] = false, ["paths"] = new JsonArray(), ["additionalPaths"] = new JsonArray(), ["reason"] = "no startup path" },
        };

        public Result Run(JsonObject evidence, string? @base = null, string? head = null, string plan = "plan.md")
        {
            File.WriteAllText(_evidence, evidence.ToJsonString(), Utf8);
            var output = new StringWriter();
            var options = new OsWitnessCheckOptions { BaseRef = @base ?? Base, HeadRef = head ?? Head, EvidencePath = _evidence, Plan = plan, Repository = Repo };
            var exit = OsWitnessCheck.Run(options, NightlyHost.Production(output, output), new System.IO.Abstractions.FileSystem(), OsWitnessCheck.NativeGit);
            return new Result(exit, output.ToString());
        }
    }
}
