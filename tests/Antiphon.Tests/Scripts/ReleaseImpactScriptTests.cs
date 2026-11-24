using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1820 S1R5 (V-5). <see cref="ReleaseImpactScripts"/> closes test classes over the scripts they reach: ScriptHarness
/// basenames, dot-source/import chains, compiled script helpers and paths embedded in command strings. Real repository
/// sources are parsed as data, never executed. Unknown closure keeps a caller-path reason and Full.
/// 2026-11-22 plan, unit 17: the closure was scripts/lib/release-impact-scripts.ps1, which this class dot-sourced in a pwsh
/// child; it is C# in the core now (its PowerShell reader is <c>PsReader</c>) and every case runs in process on the same
/// inventories. <see cref="PowerShellFormsKeepTheirTargets"/> pins the PowerShell's answers on the forms it reads.
/// </summary>
[Category("Unit")]
public sealed class ReleaseImpactScriptTests
{
    private const string Project = "tests/Antiphon.Tests/Antiphon.Tests.csproj";
    private const string Tests = "tests/Antiphon.Tests/Scripts/";
    private const string CoreDriver = Project + "::Antiphon.Tests.Scripts.LiveLaneFingerprintTests";
    private const string HarnessDriver = Project + "::Antiphon.Tests.Scripts.LiveLaneOutcomeTests";
    private const string Launcher = Project + "::Antiphon.Tests.Scripts.Nightly.AntiphonCliLauncherTests";
    private const string Harmless = "fixture/tests/Harmless/Harmless.csproj::Fixture.HarmlessProbe";
    private const string Looped = "fixture/tests/Chain/Chain.csproj::Fixture.ChainProbe";
    private const string Dynamic = "fixture/tests/Dyn/Dyn.csproj::Fixture.DynamicProbe";
    private const string SeedDeclaration = "fixture/tests/Seed/SeedProbe.cs";
    private const string Seeded = "fixture/tests/Seed/Seed.csproj::Fixture.SeedProbe";
    private const string Formed = "fixture/tests/T/T.csproj::Fixture.FormsProbe";
    private static readonly string LongSeed = "fixture/" + new string('x', 300) + ".ps1";

    // The real tree is immutable input: loaded and closed once per run.
    private static readonly Lazy<JsonElement> Shared = new(() => Close([RealCase()]));

    // Seed-token variants share one run; each case is its own immutable inventory.
    private static readonly Lazy<JsonElement> SeedResults = new(() => Close(
    [
        SeedCase("escape", "\"../../outside.ps1\""),
        SeedCase("dotdot", "\"fixture/scripts/../scripts/real.ps1\""),
        SeedCase("drive", "\"C:/Windows/evil.ps1\""),
        SeedCase("unc", "@\"\\\\server\\share\\evil.ps1\""),
        SeedCase("rooted", "\"/etc/evil.ps1\""),
        SeedCase("case-basename", "\"Foo.ps1\""),
        SeedCase("case-path", "\"fixture/scripts/Real.ps1\""),
        SeedCase("case-twins", "\"Twin.ps1\""),
        SeedCase("missing", "\"fixture/scripts/absent.ps1\"", "\"absent-leaf.ps1\""),
        SeedCase("long", "\"" + LongSeed + "\""),
    ]));

    [Test]
    public void BasenameReachesCliSource()
    {
        var result = Case(Shared.Value, "real");
        // nightly-core.ps1 compiles every scripts/lib/Antiphon.Scripts source into the CLI, so a driver that loads it reaches them.
        // 2026-11-22 plan, unit 8: the release-selection and rc-project-selection drivers this case used are deleted (their
        // classes run in process); unit 15 deleted test-release-gate.ps1, whose policy and run classes were the examples
        // after unit 14. The example is the live-lane fingerprint class, whose driver still loads nightly-core.ps1.
        Classes(result, "scripts/lib/Antiphon.Scripts/Program.cs").ShouldContain(CoreDriver);
        var chain = Chain(result, "scripts/lib/Antiphon.Scripts/Program.cs", CoreDriver);
        chain[..2].ShouldBe([Tests + "LiveLaneFingerprintTests.cs", "scripts/test-live-lane-fingerprint.ps1"]);
        chain[^2..].ShouldBe(["scripts/lib/nightly-core.ps1", "scripts/lib/Antiphon.Scripts/Program.cs"]);
        Kinds(result, "scripts/test-live-lane-fingerprint.ps1", CoreDriver).ShouldContain("script:basename");
        Classes(result, "scripts/lib/Antiphon.Scripts/Program.cs").ShouldNotContain(Harmless);
        AssertIncomplete(result);
    }

    [Test]
    public void DotSourceClosureReachesAuthority()
    {
        var result = Case(Shared.Value, "real");
        foreach (var (identity, file, driver) in new[]
                 {
                     (CoreDriver, "LiveLaneFingerprintTests.cs", "scripts/test-live-lane-fingerprint.ps1"),
                     (HarnessDriver, "LiveLaneOutcomeTests.cs", "scripts/test-live-lane-outcome.ps1"),
                 })
            Chain(result, "scripts/lib/c487-harness.ps1", identity).ShouldBe([Tests + file, driver, "scripts/lib/c487-harness.ps1"], identity);
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

    // W2-nightly S27: NativeLinuxPolicyMatcherParityTests runs in-process and scripts/lib/nightly-coverage.ps1 is deleted.
    // 2026-11-22 (remaining-ps1 plan, units 1 and 2): the toolchain floor and test timing twins are deleted with their ps1,
    // so no twin test embeds a script path any more. The embedded path is the 5.1 launcher, which stays PowerShell (end
    // state 5): the launcher test's driver script, a C# raw string, dot-sources scripts/lib/antiphon-cli.ps1.
    [Test]
    public void EmbeddedPathReachesLauncherTest()
    {
        var result = Case(Shared.Value, "real");
        Kinds(result, "scripts/lib/antiphon-cli.ps1", Launcher).ShouldContain("script:embedded");
        Chain(result, "scripts/lib/antiphon-cli.ps1", Launcher).ShouldBe(
            [Tests + "Nightly/AntiphonCliLauncherTests.cs", "scripts/lib/antiphon-cli.ps1"]);
        // Similar prose and near-miss names are not executable paths.
        Classes(result, "scripts/lib/antiphon-cli.ps1").ShouldNotContain(Harmless);
        Paths(result, Harmless).ShouldBeEmpty();
        AssertIncomplete(result);
    }

    [Test]
    public void DynamicScriptTargetIsIncomplete()
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
        var cases = new List<ClosureCase> { DynamicCase("control", "", new(), 0) };
        cases.AddRange(variants.Select(v => DynamicCase(v.Name, v.Body, v.Extra, v.Visits)));
        var results = Close(cases);

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

    // Seed tokens that miss an exact inventory key keep a path-specific reason, never a silent drop.
    [Test]
    public void EscapingSeedPathIsOutside()
    {
        var result = Case(SeedResults.Value, "escape");
        Reasons(result).ShouldContain(SeedReason("outside"));
        Classes(result, "outside.ps1").ShouldBeEmpty("an escaping literal never reaches a root file");
        AssertSeedControl(result);
    }

    [Test]
    public void NormalizedSeedPathRecordsInventoryEdge()
    {
        var result = Case(SeedResults.Value, "dotdot");
        Classes(result, "fixture/scripts/real.ps1").ShouldBe([Seeded]);
        Chain(result, "fixture/scripts/real.ps1", Seeded).ShouldBe([SeedDeclaration, "fixture/scripts/real.ps1"]);
        Kinds(result, "fixture/scripts/real.ps1", Seeded).ShouldContain("script:path");
        // The closure continues from the normalized node.
        Chain(result, "fixture/scripts/lib/next.ps1", Seeded).ShouldBe(
            [SeedDeclaration, "fixture/scripts/real.ps1", "fixture/scripts/lib/next.ps1"]);
        Reasons(result).ShouldNotContain(r => r.StartsWith("script-target-unresolved:", StringComparison.Ordinal));
        AssertSeedControl(result);
    }

    [Test]
    public void RootedSeedPathIsOutside()
    {
        var results = SeedResults.Value;
        foreach (var name in new[] { "drive", "unc", "rooted" })
        {
            var result = Case(results, name);
            Reasons(result).ShouldContain(SeedReason("outside"), name);
            Classes(result, "fixture/scripts/evil.ps1").ShouldBeEmpty(name);
            AssertSeedControl(result);
        }
    }

    [Test]
    public void CaseOnlySeedMatchIsNamed()
    {
        var results = SeedResults.Value;
        var basename = Case(results, "case-basename");
        Reasons(basename).ShouldContain(SeedReason("case:Foo.ps1"));
        Classes(basename, "fixture/scripts/foo.ps1").ShouldBe([Seeded], "the folded match stays a conservative positive");
        AssertSeedControl(basename);
        var path = Case(results, "case-path");
        Reasons(path).ShouldContain(SeedReason("case:fixture/scripts/Real.ps1"));
        Classes(path, "fixture/scripts/real.ps1").ShouldBe([Seeded]);
        AssertSeedControl(path);
    }

    [Test]
    public void FoldedSeedBasenameWithTwoFilesIsAmbiguous()
    {
        var result = Case(SeedResults.Value, "case-twins");
        Reasons(result).ShouldContain(SeedReason("ambiguous:Twin.ps1"));
        Reasons(result).ShouldNotContain(SeedReason("case:Twin.ps1"));
        foreach (var twin in new[] { "fixture/scripts/twin.ps1", "fixture/tools/TWIN.ps1" })
            Classes(result, twin).ShouldBe([Seeded], twin);
        AssertSeedControl(result);
    }

    [Test]
    public void UnmatchedSeedPathIsMissing()
    {
        var results = SeedResults.Value;
        var missing = Case(results, "missing");
        Reasons(missing).ShouldContain(SeedReason("missing:fixture/scripts/absent.ps1"));
        Reasons(missing).ShouldContain(SeedReason("missing:absent-leaf.ps1"));
        AssertSeedControl(missing);
        // An unbounded token becomes a bounded detail: a fixed prefix plus a digest of the whole detail.
        var detail = "missing:" + LongSeed;
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(detail))).ToLowerInvariant();
        var bounded = Reasons(Case(results, "long")).Where(r => r.StartsWith(SeedReason("missing:"), StringComparison.Ordinal)).ToArray();
        bounded.ShouldBe([SeedReason(detail[..128] + "~" + digest[..16])]);
    }

    // Unit 17: the forms the PowerShell read through System.Management.Automation's parser, now read by PsReader. Each
    // row's paths and reasons are the PowerShell closure's answer on the same inventory (pwsh 7.6, before the .ps1 was
    // deleted): dot-source and call targets in every quoting, [IO.Path] calls, Import-Module, pwsh -File, bash and node
    // scripts, Add-Type sources, Split-Path parents, constant foreach, scoped and typed variables, cwd-relative calls,
    // a parameter and a compound assignment (dynamic), Invoke-Expression, a syntax error and its recovery (parse),
    // switch, try, loops, hashtables, here-strings and script blocks.
    [Test]
    public void PowerShellFormsKeepTheirTargets()
    {
        var results = Close([FormsCase("forms", FormsBody), FormsCase("misc", MiscBody)]);
        var forms = Case(results, "forms");
        Paths(forms, Formed).ShouldBe(new[]
        {
            "fixture/outside.ps1", "fixture/scripts/entry.ps1", "fixture/scripts/lib/Mod.psm1", "fixture/scripts/lib/a.ps1",
            "fixture/scripts/lib/b.ps1", "fixture/scripts/lib/c.cs", "fixture/scripts/lib/d.cs", "fixture/scripts/lib/e.cs",
            "fixture/scripts/lib/w.ps1", "fixture/scripts/lib/x.ps1", "fixture/scripts/lib/y.ps1", "fixture/scripts/lib/z.ps1",
            "fixture/scripts/n.mjs", "fixture/scripts/p.ps1", "fixture/scripts/s.sh", "scripts/lib/w.ps1",
        });
        const string entry = "script-target-unresolved:fixture/scripts/entry.ps1:";
        Reasons(forms).ShouldBe(new[]
        {
            "script-closure-uncertified", entry + "compile", entry + "cwd:./lib/x.ps1", entry + "cwd:./lib/y.ps1",
            entry + "cwd:./lib/z.ps1", entry + "cwd:lib/a.ps1", entry + "cwd:lib/x.ps1", entry + "dynamic", entry + "parse",
            "script-target-unresolved:fixture/scripts/n.mjs:language", "script-target-unresolved:fixture/scripts/s.sh:language",
        });
        AssertIncomplete(forms);
        var misc = Case(results, "misc");
        Paths(misc, Formed).ShouldBe(new[]
        {
            "fixture/scripts/entry.ps1", "fixture/scripts/lib/a.ps1", "fixture/scripts/lib/b.ps1", "fixture/scripts/lib/x.ps1",
            "fixture/scripts/lib/y.ps1", "fixture/scripts/lib/z.ps1",
        });
        Reasons(misc).ShouldBe(new[] { "script-closure-uncertified", entry + "dynamic" });
        AssertIncomplete(misc);
    }

    private const string FormsBody = """
        . "$PSScriptRoot\lib\x.ps1"
        . ($PSScriptRoot + '/lib/y.ps1')
        . ([IO.Path]::Combine($PSScriptRoot, 'lib', 'z.ps1'))
        Import-Module "$PSScriptRoot/lib/Mod.psm1" -Force
        & pwsh -File (Join-Path $PSScriptRoot 'p.ps1')
        pwsh -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot/p.ps1" -Name x
        bash "$PSScriptRoot/s.sh"
        node --max-old-space-size=4096 $PSScriptRoot/n.mjs
        python -m tool
        Add-Type -Path (Join-Path $PSScriptRoot 'lib/c.cs')
        Add-Type -TypeDefinition ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'lib/d.cs')))
        $src = Get-Content (Join-Path $PSScriptRoot 'lib/e.cs') -Raw; Add-Type -TypeDefinition $src
        Add-Type -AssemblyName System.Web
        iex $code
        $root = Split-Path $PSScriptRoot -Parent
        . "$root/scripts/lib/w.ps1"
        foreach ($n in @('a', 'b')) { . "$PSScriptRoot/lib/$n.ps1" }
        $h = @"
        x $y
        "@
        $x = "$PSScriptRoot/../outside.ps1"
        ${PSScriptRoot}/lib/x.ps1 | Out-Null
        $script:base = $PSScriptRoot; . "$script:base\lib\y.ps1"
        [string]$q = Join-Path $PSScriptRoot 'lib/z.ps1'; . $q
        $r = 'lib/x.ps1'; $r += 'x'; . $r
        function f([string]$p) { . $p }
        . .\lib\x.ps1
        & ./lib/y.ps1
        ./lib/z.ps1 -A 1
        lib/a.ps1
        Microsoft.PowerShell.Utility\Write-Output x
        $t = $c ? 'a' : 'b'; $u = $v ?? 'w'; a && b || c
        Get-Item (Join-Path $PSScriptRoot 'lib/b.ps1') | Select-Object -First 1
        $m = "{0}/lib/a.ps1" -f $PSScriptRoot
        [IO.Path]::GetDirectoryName($PSCommandPath) | Out-Null
        . ([System.IO.Path]::Combine((Split-Path -Parent $PSScriptRoot), 'scripts', 'lib', 'w.ps1'))
        'scripts/lib/w.ps1' | Out-Null
        git show HEAD:scripts/lib/w.ps1 2>&1 | Out-Null
        """;

    private const string MiscBody = """
        switch -Regex ($x) { 'a' { . "$PSScriptRoot/lib/a.ps1" } default { . "$PSScriptRoot/lib/b.ps1" } }
        try { . "$PSScriptRoot/lib/x.ps1" } catch [System.Exception] { } finally { }
        do { $i++ } while ($i -lt 3)
        for ($i = 0; $i -lt 3; $i++) { }
        while ($true) { break }
        trap { continue }
        $l = @(
          'a'
          'b', 'c'
        )
        $v = 1,2 + 3 -f 'a', 'b'
        $o = [ordered]@{ k = 'v'; 'q' = (Join-Path $PSScriptRoot 'lib/y.ps1') }
        $z = $o.k.Substring(0, 1).ToUpper()
        $w = @"
        $(Join-Path $PSScriptRoot 'lib/z.ps1')
        "@
        & { param($a) . $a } "$PSScriptRoot/lib/x.ps1"
        $f = { . "$PSScriptRoot/lib/a.ps1" }
        Invoke-Command -ScriptBlock { . "$PSScriptRoot/lib/b.ps1" }
        """;

    private static ClosureCase FormsCase(string name, string body)
    {
        var files = new SortedDictionary<string, string?>(StringComparer.Ordinal)
        {
            ["fixture/scripts/lib/x.ps1"] = "# x\n", ["fixture/scripts/lib/y.ps1"] = "# y\n", ["fixture/scripts/lib/z.ps1"] = "# z\n",
            ["fixture/scripts/lib/a.ps1"] = "# a\n", ["fixture/scripts/lib/b.ps1"] = "# b\n", ["fixture/scripts/lib/w.ps1"] = "# w\n",
            ["fixture/scripts/lib/Mod.psm1"] = "# m\n", ["fixture/scripts/p.ps1"] = "# p\n",
            ["fixture/scripts/s.sh"] = "echo fixture/scripts/lib/x.ps1\n", ["fixture/scripts/n.mjs"] = "import 'fixture/scripts/lib/y.ps1'\n",
            ["fixture/scripts/lib/c.cs"] = "class C {}\n", ["fixture/scripts/lib/d.cs"] = "class D {}\n", ["fixture/scripts/lib/e.cs"] = "class E {}\n",
            ["scripts/lib/w.ps1"] = "# real-like w\n", ["fixture/outside.ps1"] = "# o\n",
            ["fixture/tests/T/T.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\" />\n",
            ["fixture/tests/T/Probe.cs"] = "class Probe { void R() => Run(\"entry.ps1\"); }\n",
            // The raw strings' lines end with the source file's line ends; the reader takes either.
            ["fixture/scripts/entry.ps1"] = body,
        };
        return new ClosureCase(name, files, [Consumer(Formed, "fixture/tests/T/Probe.cs")], 0);
    }

    private static ClosureCase RealCase()
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
            Project, Tests + "ScriptHarness.cs",
            Tests + "LiveLaneFingerprintTests.cs", Tests + "LiveLaneOutcomeTests.cs",
            Tests + "Nightly/AntiphonCliLauncherTests.cs",
        })
            files[path] = File.ReadAllText(Path.Combine(root, path));
        foreach (var (path, text) in ChainFixture()) files[path] = text;
        var consumers = new List<ReleaseScriptConsumer>
        {
            Consumer(Harmless, "fixture/tests/Harmless/HarmlessProbe.cs"),
            Consumer(Looped, "fixture/tests/Chain/ChainProbe.cs"),
        };
        foreach (var name in new[] { "LiveLaneFingerprintTests", "LiveLaneOutcomeTests" })
            consumers.Add(Consumer(Project + "::Antiphon.Tests.Scripts." + name, Tests + name + ".cs"));
        consumers.Add(Consumer(Launcher, Tests + "Nightly/AntiphonCliLauncherTests.cs"));
        return new ClosureCase("real", files, consumers, 0);
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
                    var prose = "the antiphon-cli launcher runs later";
                    var nearMiss = "scripts/lib/antiphon-cli.ps1.orig";
                    var suffix = "see 'scripts/lib/antiphon-cli.ps1x' for details";
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

    private static ClosureCase DynamicCase(string name, string body, Dictionary<string, string> extra, int visits)
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
        return new ClosureCase(name, files, [Consumer(Dynamic, "fixture/tests/Dyn/DynamicProbe.cs")], visits);
    }

    private static ClosureCase SeedCase(string name, params string[] literals)
    {
        var calls = string.Concat(literals.Select(l => $"        await Harness.RunAsync({l});\n"));
        var files = new SortedDictionary<string, string?>(StringComparer.Ordinal)
        {
            ["fixture/tests/Seed/Seed.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\" />\n",
            [SeedDeclaration] = "namespace Fixture;\ninternal class SeedProbe\n{\n    public async Task Runs()\n    {\n"
                + "        await Harness.RunAsync(\"fixture/scripts/const.ps1\");\n" + calls + "    }\n}\n",
            ["fixture/scripts/const.ps1"] = "function Get-FixtureConstant { 'constant' }\n",
            ["fixture/scripts/real.ps1"] = ". (Join-Path $PSScriptRoot 'lib/next.ps1')\n",
            ["fixture/scripts/lib/next.ps1"] = "function Get-FixtureNext { 'next' }\n",
            ["fixture/scripts/foo.ps1"] = "# foo\n",
            ["fixture/scripts/evil.ps1"] = "throw 'EXECUTED'\n",
            ["outside.ps1"] = "throw 'EXECUTED'\n",
        };
        if (name == "case-twins")
        {
            files["fixture/scripts/twin.ps1"] = "# one\n";
            files["fixture/tools/TWIN.ps1"] = "# two\n";
        }
        return new ClosureCase(name, files, [Consumer(Seeded, SeedDeclaration)], 0);
    }

    private static string SeedReason(string detail) => "script-target-unresolved:" + SeedDeclaration + ":" + detail;

    private static void AssertSeedControl(JsonElement result)
    {
        // Positive control: the exact constant literal beside every variant still records its edge.
        Classes(result, "fixture/scripts/const.ps1").ShouldBe([Seeded]);
        AssertIncomplete(result);
    }

    private static ReleaseScriptConsumer Consumer(string identity, string declaration)
    {
        var split = identity.IndexOf("::", StringComparison.Ordinal);
        return new ReleaseScriptConsumer(identity[..split], identity[(split + 2)..], [declaration]);
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
        result.GetProperty("schema").GetString().ShouldBe("antiphon-release-script-closure-v1");
        result.GetProperty("complete").GetBoolean().ShouldBeFalse();
        result.GetProperty("fallback").GetString().ShouldBe("Full");
        result.GetProperty("incompleteReasons").GetArrayLength().ShouldBeGreaterThan(0);
    }

    // Each case's closure document under {"cases": {name: closure}}, the shape the PowerShell's JSON adapter returned.
    private static JsonElement Close(IReadOnlyList<ClosureCase> cases)
    {
        var text = new StringBuilder("{\"cases\":{");
        for (var i = 0; i < cases.Count; i++)
        {
            if (i > 0) text.Append(',');
            var closure = ReleaseImpactScripts.Close(cases[i].Files, cases[i].Consumers, cases[i].MaxVisits);
            text.Append(JsonSerializer.Serialize(cases[i].Name)).Append(':').Append(closure.ToJson());
        }
        using var document = JsonDocument.Parse(text.Append("}}").ToString());
        return document.RootElement.Clone();
    }

    private sealed record ClosureCase(string Name, IReadOnlyDictionary<string, string?> Files, IReadOnlyList<ReleaseScriptConsumer> Consumers, int MaxVisits);

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Antiphon.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Antiphon.sln was not found");
    }
}
