using System.Diagnostics;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1820 S1R5 (V-5). <c>scripts/lib/release-impact-scripts.ps1</c> closes test classes over the
/// scripts they reach: ScriptHarness basenames, dot-source/import chains, compiled script helpers and
/// paths embedded in command strings. Real repository sources are parsed as data, never executed.
/// Unknown closure keeps a caller-path reason and Full.
/// </summary>
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseImpactScriptTests
{
    private const string Project = "tests/Antiphon.Tests/Antiphon.Tests.csproj";
    private const string Tests = "tests/Antiphon.Tests/Scripts/";
    private const string Cli = Project + "::Antiphon.Tests.Scripts.ReleaseSelectionCliTests";
    private const string Selection = Project + "::Antiphon.Tests.Scripts.TestSelectionTests";
    private const string Publication = Project + "::Antiphon.Tests.Scripts.ReleaseGatePublicationAuthorityTests";
    private const string Contract = Project + "::Antiphon.Tests.Scripts.ReleaseGateAuthorityContractTests";
    private const string Parity = Project + "::Antiphon.Tests.Scripts.NativeLinuxPolicyMatcherParityTests";
    private const string Harmless = "fixture/tests/Harmless/Harmless.csproj::Fixture.HarmlessProbe";
    private const string Looped = "fixture/tests/Chain/Chain.csproj::Fixture.ChainProbe";
    private const string Dynamic = "fixture/tests/Dyn/Dyn.csproj::Fixture.DynamicProbe";

    // The real tree is immutable input: loaded and closed once per run in one bounded process.
    private static readonly Lazy<Task<JsonElement>> Shared = new(() => CloseAsync([RealCase()]));

    [Test]
    public async Task BasenameReachesBaselineHelper()
    {
        var result = Case(await Shared.Value, "real");
        Classes(result, "scripts/lib/Antiphon.Scripts/Baseline.cs").ShouldContain(Cli);
        var chain = Chain(result, "scripts/lib/Antiphon.Scripts/Baseline.cs", Cli);
        chain[..2].ShouldBe([Tests + "ReleaseSelectionCliTests.cs", "scripts/test-release-selection-cli.ps1"]);
        // nightly-core.ps1 compiles every scripts/lib/Antiphon.Scripts source into the CLI.
        chain[^2..].ShouldBe(["scripts/lib/nightly-core.ps1", "scripts/lib/Antiphon.Scripts/Baseline.cs"]);
        Kinds(result, "scripts/test-release-selection-cli.ps1", Cli).ShouldContain("script:basename");
        Kinds(result, "scripts/test-test-selection.ps1", Selection).ShouldContain("script:basename");
        Chain(result, "scripts/lib/test-selection.ps1", Selection).ShouldBe(
            [Tests + "TestSelectionTests.cs", "scripts/test-test-selection.ps1", "scripts/lib/test-selection.ps1"]);
        Classes(result, "scripts/lib/Antiphon.Scripts/Baseline.cs").ShouldNotContain(Harmless);
        AssertIncomplete(result);
    }

    [Test]
    public async Task DotSourceClosureReachesAuthority()
    {
        var result = Case(await Shared.Value, "real");
        foreach (var (identity, file) in new[] { (Publication, "ReleaseGatePublicationAuthorityTests.cs"), (Contract, "ReleaseGateAuthorityContractTests.cs") })
            Chain(result, "scripts/lib/release-authority.ps1", identity).ShouldBe(
                [Tests + file, "scripts/test-release-gate.ps1", "scripts/lib/release-authority.ps1"], identity);
        // Fixture: constant foreach imports, multi-hop dot-source, Import-Module and two cycles.
        Paths(result, Looped).ShouldBe(new[]
        {
            "fixture/scripts/chain-entry.ps1", "fixture/scripts/lib/Mod.psm1", "fixture/scripts/lib/hop-a.ps1",
            "fixture/scripts/lib/hop-b.ps1", "fixture/scripts/lib/hop-c.ps1", "fixture/scripts/lib/module-helper.ps1",
            "fixture/scripts/lib/release-authority.ps1",
        });
        Chain(result, "fixture/scripts/lib/release-authority.ps1", Looped).ShouldBe(
            ["fixture/tests/Chain/ChainProbe.cs", "fixture/scripts/chain-entry.ps1", "fixture/scripts/lib/hop-a.ps1",
             "fixture/scripts/lib/hop-c.ps1", "fixture/scripts/lib/release-authority.ps1"]);
        Chain(result, "fixture/scripts/lib/module-helper.ps1", Looped).ShouldBe(
            ["fixture/tests/Chain/ChainProbe.cs", "fixture/scripts/chain-entry.ps1", "fixture/scripts/lib/Mod.psm1",
             "fixture/scripts/lib/module-helper.ps1"]);
        Reasons(result).ShouldNotContain(r => r.StartsWith("script-target-unresolved:fixture/", StringComparison.Ordinal));
        AssertIncomplete(result);
    }

    [Test]
    public async Task EmbeddedPathReachesParityTest()
    {
        var result = Case(await Shared.Value, "real");
        Kinds(result, "scripts/lib/nightly-coverage.ps1", Parity).ShouldContain("script:embedded");
        Chain(result, "scripts/lib/nightly-coverage.ps1", Parity).ShouldBe(
            [Tests + "NativeLinuxPolicyMatcherParityTests.cs", "scripts/lib/nightly-coverage.ps1"]);
        // Similar prose and near-miss names are not executable paths.
        Classes(result, "scripts/lib/nightly-coverage.ps1").ShouldNotContain(Harmless);
        Paths(result, Harmless).ShouldBeEmpty();
        AssertIncomplete(result);
    }

    [Test]
    public async Task DynamicScriptTargetIsIncomplete()
    {
        const string entry = "fixture/scripts/entry.ps1";
        var variants = new (string Name, string Body, Dictionary<string, string> Extra, int Visits, string Reason)[]
        {
            ("computed", "$target = Get-Random\n. (Join-Path $PSScriptRoot $target)\n& $command\n", new(), 0,
                "script-target-unresolved:" + entry + ":dynamic"),
            ("missing", ". (Join-Path $PSScriptRoot 'lib/absent.ps1')\n", new(), 0,
                "script-target-unresolved:" + entry + ":missing:fixture/scripts/lib/absent.ps1"),
            ("parse", ". \"$PSScriptRoot/lib/broken.ps1\"\n", new() { ["fixture/scripts/lib/broken.ps1"] = "function Broken {\n" }, 0,
                "script-target-unresolved:fixture/scripts/lib/broken.ps1:parse"),
            ("ambiguous", "", new() { ["fixture/scripts/twin.ps1"] = "# one\n", ["fixture/tools/twin.ps1"] = "# two\n" }, 0,
                "script-target-unresolved:fixture/tests/Dyn/DynamicProbe.cs:ambiguous:twin.ps1"),
            ("outside", ". (Join-Path $PSScriptRoot '../../../outside.ps1')\n", new(), 0,
                "script-target-unresolved:" + entry + ":outside"),
            ("budget", ". (Join-Path $PSScriptRoot 'hop1.ps1')\n", new()
            {
                ["fixture/scripts/hop1.ps1"] = ". (Join-Path $PSScriptRoot 'hop2.ps1')\n",
                ["fixture/scripts/hop2.ps1"] = ". (Join-Path $PSScriptRoot 'hop3.ps1')\n",
                ["fixture/scripts/hop3.ps1"] = "# beyond the bound\n",
            }, 2, "script-target-unresolved:fixture/scripts/hop2.ps1:budget"),
        };
        var cases = new List<object> { DynamicCase("control", "", new(), 0) };
        cases.AddRange(variants.Select(v => DynamicCase(v.Name, v.Body, v.Extra, v.Visits)));
        var results = await CloseAsync(cases);

        // Positive control: a bounded constant closure resolves with no path-specific reason.
        var control = Case(results, "control");
        Classes(control, "fixture/scripts/lib/const.ps1").ShouldBe([Dynamic]);
        Reasons(control).ShouldNotContain(r => r.StartsWith("script-target-unresolved:", StringComparison.Ordinal));
        AssertIncomplete(control);
        foreach (var variant in variants)
        {
            var result = Case(results, variant.Name);
            Reasons(result).ShouldContain(variant.Reason, variant.Name);
            Classes(result, "fixture/scripts/lib/const.ps1").ShouldBe([Dynamic], variant.Name);
            AssertIncomplete(result);
        }
        var budget = Case(results, "budget");
        Classes(budget, "fixture/scripts/hop2.ps1").ShouldBe([Dynamic]);
        Classes(budget, "fixture/scripts/hop3.ps1").ShouldBeEmpty("closure beyond the bound is unknown, not reached");
        var ambiguous = Case(results, "ambiguous");
        foreach (var twin in new[] { "fixture/scripts/twin.ps1", "fixture/tools/twin.ps1" })
            Classes(ambiguous, twin).ShouldBe([Dynamic], twin);
    }

    private static object RealCase()
    {
        var root = RepoRoot();
        var files = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        var code = new[] { ".ps1", ".psm1", ".cs", ".csproj", ".sh", ".mjs", ".js" };
        void Walk(DirectoryInfo directory)
        {
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if (entry.LinkTarget is not null) continue;
                if (entry is DirectoryInfo child)
                {
                    if (child.Name is not ("bin" or "obj" or "node_modules")) Walk(child);
                    continue;
                }
                var relative = Path.GetRelativePath(root, entry.FullName).Replace('\\', '/');
                files[relative] = code.Contains(Path.GetExtension(entry.Name), StringComparer.OrdinalIgnoreCase)
                    ? File.ReadAllText(entry.FullName) : null;
            }
        }
        Walk(new DirectoryInfo(Path.Combine(root, "scripts")));
        foreach (var path in new[]
        {
            Project, Tests + "ScriptHarness.cs", Tests + "ReleaseSelectionCliTests.cs", Tests + "TestSelectionTests.cs",
            Tests + "ReleaseGatePublicationAuthorityTests.cs", Tests + "ReleaseGateAuthorityContractTests.cs",
            Tests + "NativeLinuxPolicyMatcherParityTests.cs",
        })
            files[path] = File.ReadAllText(Path.Combine(root, path));
        foreach (var (path, text) in ChainFixture()) files[path] = text;
        var consumers = new List<object>
        {
            Consumer(Harmless, "fixture/tests/Harmless/HarmlessProbe.cs"),
            Consumer(Looped, "fixture/tests/Chain/ChainProbe.cs"),
        };
        foreach (var name in new[] { "ReleaseSelectionCliTests", "TestSelectionTests", "ReleaseGatePublicationAuthorityTests", "ReleaseGateAuthorityContractTests", "NativeLinuxPolicyMatcherParityTests" })
            consumers.Add(Consumer(Project + "::Antiphon.Tests.Scripts." + name, Tests + name + ".cs"));
        return new { name = "real", files, consumers, maxVisits = 0 };
    }

    private static Dictionary<string, string> ChainFixture() => new(StringComparer.Ordinal)
    {
        ["fixture/tests/Harmless/Harmless.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\" />\n",
        ["fixture/tests/Harmless/HarmlessProbe.cs"] = """
            namespace Fixture;
            internal class HarmlessProbe
            {
                public void Runs()
                {
                    var prose = "the nightly-coverage report runs later";
                    var nearMiss = "scripts/lib/nightly-coverage.ps1.orig";
                    var suffix = "see 'scripts/lib/nightly-coverage.ps1x' for details";
                }
            }
            """,
        ["fixture/tests/Chain/Chain.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\" />\n",
        ["fixture/tests/Chain/ChainProbe.cs"] = """
            namespace Fixture;
            internal class ChainProbe { public Task Runs() => Harness.RunAsync("chain-entry.ps1"); }
            """,
        ["fixture/scripts/chain-entry.ps1"] = """
            foreach ($name in 'hop-a', 'hop-b') { . (Join-Path $PSScriptRoot ('lib/' + $name + '.ps1')) }
            Import-Module (Join-Path $PSScriptRoot 'lib/Mod.psm1') -Force
            """,
        ["fixture/scripts/lib/hop-a.ps1"] = ". \"$PSScriptRoot/hop-c.ps1\"\n",
        ["fixture/scripts/lib/hop-b.ps1"] = ". (Join-Path $PSScriptRoot 'hop-a.ps1')\n",
        ["fixture/scripts/lib/hop-c.ps1"] = ". (Join-Path $PSScriptRoot 'hop-a.ps1')\n$authority = Join-Path $PSScriptRoot 'release-authority.ps1'\n. $authority\n",
        ["fixture/scripts/lib/release-authority.ps1"] = "function Get-FixtureAuthority { 'fixture' }\n",
        ["fixture/scripts/lib/Mod.psm1"] = ". $PSScriptRoot/module-helper.ps1\n",
        ["fixture/scripts/lib/module-helper.ps1"] = "function Get-FixtureHelper { 'helper' }\n",
    };

    private static object DynamicCase(string name, string body, Dictionary<string, string> extra, int visits)
    {
        var literal = name == "ambiguous" ? "twin.ps1" : "entry.ps1";
        var files = new SortedDictionary<string, string?>(StringComparer.Ordinal)
        {
            ["fixture/tests/Dyn/Dyn.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\" />\n",
            ["fixture/tests/Dyn/DynamicProbe.cs"] =
                $"namespace Fixture;\ninternal class DynamicProbe {{ public Task Runs() => Harness.RunAsync(\"{literal}\", \"entry.ps1\"); }}\n",
            ["fixture/scripts/entry.ps1"] = ". (Join-Path $PSScriptRoot 'lib/const.ps1')\n" + body,
            ["fixture/scripts/lib/const.ps1"] = "function Get-FixtureConstant { 'constant' }\n",
        };
        foreach (var (path, text) in extra) files[path] = text;
        return new { name, files, consumers = new[] { Consumer(Dynamic, "fixture/tests/Dyn/DynamicProbe.cs") }, maxVisits = visits };
    }

    private static object Consumer(string identity, string declaration)
    {
        var split = identity.IndexOf("::", StringComparison.Ordinal);
        return new { project = identity[..split], fullName = identity[(split + 2)..], declarationPaths = new[] { declaration } };
    }

    private static JsonElement Case(JsonElement results, string name) => results.GetProperty("cases").GetProperty(name);

    private static string Identity(JsonElement value) => value.GetProperty("project").GetString() + "::" + value.GetProperty("fullName").GetString();

    private static JsonElement? Entry(JsonElement result, string path, string identity) => result.GetProperty("edges").EnumerateArray()
        .Where(e => e.GetProperty("path").GetString() == path).SelectMany(e => e.GetProperty("classes").EnumerateArray())
        .Select(c => (JsonElement?)c).FirstOrDefault(c => Identity(c!.Value) == identity);

    private static string[] Classes(JsonElement result, string path) => result.GetProperty("edges").EnumerateArray()
        .Where(e => e.GetProperty("path").GetString() == path).SelectMany(e => e.GetProperty("classes").EnumerateArray())
        .Select(Identity).Order(StringComparer.Ordinal).ToArray();

    private static string[] Paths(JsonElement result, string identity) => result.GetProperty("edges").EnumerateArray()
        .Where(e => e.GetProperty("classes").EnumerateArray().Any(c => Identity(c) == identity)
            && !e.GetProperty("classes").EnumerateArray().First(c => Identity(c) == identity).GetProperty("kinds").EnumerateArray().Any(k => k.GetString() == "declaration"))
        .Select(e => e.GetProperty("path").GetString()!).Order(StringComparer.Ordinal).ToArray();

    private static string[] Kinds(JsonElement result, string path, string identity) =>
        (Entry(result, path, identity) ?? throw new ShouldAssertException($"{identity} does not reach {path}"))
        .GetProperty("kinds").EnumerateArray().Select(k => k.GetString()!).ToArray();

    private static string[] Chain(JsonElement result, string path, string identity) =>
        (Entry(result, path, identity) ?? throw new ShouldAssertException($"{identity} does not reach {path}"))
        .GetProperty("chain").EnumerateArray().Select(k => k.GetString()!).ToArray();

    private static string[] Reasons(JsonElement result) =>
        result.GetProperty("incompleteReasons").EnumerateArray().Select(r => r.GetString()!).ToArray();

    private static void AssertIncomplete(JsonElement result)
    {
        result.GetProperty("complete").GetBoolean().ShouldBeFalse();
        result.GetProperty("fallback").GetString().ShouldBe("Full");
        result.GetProperty("incompleteReasons").GetArrayLength().ShouldBeGreaterThan(0);
    }

    private static async Task<JsonElement> CloseAsync(IReadOnlyList<object> cases)
    {
        var library = Path.Combine(RepoRoot(), "scripts", "lib", "release-impact-scripts.ps1");
        var directory = Directory.CreateTempSubdirectory("antiphon-script-closure-");
        var input = Path.Combine(directory.FullName, "input.json");
        var driver = Path.Combine(directory.FullName, "driver.ps1");
        try
        {
            await File.WriteAllTextAsync(input, JsonSerializer.Serialize(new { cases }));
            await File.WriteAllTextAsync(driver,
                "param([string]$Library, [string]$InputPath)\n$ErrorActionPreference = 'Stop'\n. $Library\nInvoke-ReleaseImpactScriptClosureJson -InputPath $InputPath\n");
            var (exit, stdout, text) = await RunAsync(["-NoProfile", "-NonInteractive", "-File", driver, "-Library", library, "-InputPath", input], 120);
            exit.ShouldBe(0, text);
            using var document = JsonDocument.Parse(stdout);
            return document.RootElement.Clone();
        }
        finally
        {
            // Only this run's exclusively created files: no recursive delete.
            File.Delete(input);
            File.Delete(driver);
            directory.Delete();
        }
    }

    private static async Task<(int Exit, string Stdout, string Text)> RunAsync(string[] arguments, int seconds)
    {
        var start = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        var watch = Stopwatch.StartNew();
        using var process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            await process.WaitForExitAsync(deadline.Token);
            await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(10));
            return (process.ExitCode, stdout.Result, stdout.Result + stderr.Result);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            }
            Console.WriteLine($"C1820 phase=script-closure elapsedMs={watch.ElapsedMilliseconds}");
        }
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Antiphon.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Antiphon.sln was not found");
    }
}
