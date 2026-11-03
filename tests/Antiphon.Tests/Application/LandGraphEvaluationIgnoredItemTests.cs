using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class LandGraphEvaluationTests
{
    // Review 436ebdc6 D-1: the canary pass sees the clean state, with no ignored file at all. A
    // docs item gated on an ignored file's absence is evaluated there and refuses, as a clean
    // land checkout does. A placeholder for the ignored gate file would hide it.
    [Test]
    [Arguments("item")]
    [Arguments("group")]
    [Timeout(300_000)]
    public async Task C1385_IgnoredGateFileDoesNotHideDocsItem(string gate)
    {
        const string condition = "Condition=\"!Exists('TestOutput/flag.local')\"";
        await using var repo = await AuditRepo.CreateAsync(new()
        {
            [".gitignore"] = "App/TestOutput/\n",
            ["docs/a.md"] = "# a\n",
            ["App/Program.cs"] = "System.Console.WriteLine();\n",
            ["App/App.csproj"] = SdkProject(gate == "item"
                ? $"""<ItemGroup><None Include="../docs/a.md" {condition} /></ItemGroup>"""
                : $"""<ItemGroup {condition}><None Include="../docs/a.md" /></ItemGroup>"""),
        }, untrackedFiles: new() { ["App/TestOutput/flag.local"] = "local\n" });

        var run = await repo.RunToolAsync();

        run.ExitCode.ShouldBe(1, run.Text);
        run.Lines.ShouldContain(l => l.StartsWith("refused: differential canary evaluation refused: ", StringComparison.Ordinal)
                                     && l.Replace('\\', '/').Contains("/docs/a.md", StringComparison.Ordinal), run.Text);
        run.Lines.ShouldNotContain("approved", run.Text);
        repo.AssertUntouched();
    }

    // The same dependency in a realistic shape: copied docs content gated on ignored build output.
    [Test]
    [Timeout(300_000)]
    public async Task C1385_IgnoredBuildOutputDoesNotHideDocsContent()
    {
        await using var repo = await AuditRepo.CreateAsync(new()
        {
            [".gitignore"] = "bin-*/\n",
            ["docs/a.md"] = "# a\n",
            ["App/Program.cs"] = "System.Console.WriteLine();\n",
            ["App/App.csproj"] = SdkProject("""
                <ItemGroup Condition="!Exists('bin-x/App.dll')">
                  <Content Include="../docs/a.md" CopyToOutputDirectory="PreserveNewest" />
                </ItemGroup>
                """),
        }, untrackedFiles: new() { ["App/bin-x/App.dll"] = "MZ\n" });

        var run = await repo.RunToolAsync();

        run.ExitCode.ShouldBe(1, run.Text);
        run.Lines.ShouldContain(l => l.StartsWith("refused: differential canary evaluation refused: ", StringComparison.Ordinal)
                                     && l.Replace('\\', '/').Contains("/docs/a.md", StringComparison.Ordinal), run.Text);
        run.Lines.ShouldNotContain("approved", run.Text);
        repo.AssertUntouched();
    }

    // Only an ignored file's own item row is tolerated. A copied file (tracked, or untracked and
    // not ignored) whose row the canary pass lacks still refuses: here an ignored gate file outside
    // the default globs removes it there.
    [Test]
    [Arguments("tracked")]
    [Arguments("untracked")]
    [Timeout(300_000)]
    public async Task C1385_CopiedFileMissingFromCanaryPassRefuses(string kind)
    {
        var files = new Dictionary<string, string>
        {
            [".gitignore"] = "bin/\n",
            ["App/Program.cs"] = "System.Console.WriteLine();\n",
            ["App/App.csproj"] = SdkProject("""<ItemGroup><Compile Remove="Extra.cs" Condition="!Exists('bin/flag.local')" /></ItemGroup>"""),
        };
        var untracked = new Dictionary<string, string> { ["App/bin/flag.local"] = "local\n" };
        (kind == "tracked" ? files : untracked)["App/Extra.cs"] = "class Extra { }\n";
        await using var repo = await AuditRepo.CreateAsync(files, untrackedFiles: untracked);

        var run = await repo.RunToolAsync();

        run.ExitCode.ShouldBe(1, run.Text);
        run.Lines.ShouldContain(l => l.StartsWith("refused: differential ", StringComparison.Ordinal)
                                     && l.Replace('\\', '/').EndsWith("App/App.csproj item Compile changed", StringComparison.Ordinal), run.Text);
        run.Lines.ShouldNotContain("approved", run.Text);
        repo.AssertUntouched();
    }

    // A tolerated ignored item row in the same project does not mask a real difference.
    [Test]
    [Timeout(300_000)]
    public async Task C1385_IgnoredItemBesideRealDifferenceRefuses()
    {
        await using var repo = await AuditRepo.CreateAsync(new()
        {
            [".gitignore"] = "App/TestOutput/\nbin/\n",
            ["App/Program.cs"] = "System.Console.WriteLine();\n",
            ["App/Extra.cs"] = "class Extra { }\n",
            ["App/App.csproj"] = SdkProject("""<ItemGroup><Compile Remove="Extra.cs" Condition="!Exists('bin/flag.local')" /></ItemGroup>"""),
        }, untrackedFiles: new()
        {
            ["App/TestOutput/Logs/run.log"] = "run output\n",
            ["App/bin/flag.local"] = "local\n",
        });

        var run = await repo.RunToolAsync();

        run.ExitCode.ShouldBe(1, run.Text);
        run.Lines.ShouldContain(l => l.StartsWith("refused: differential ", StringComparison.Ordinal)
                                     && l.Replace('\\', '/').EndsWith("App/App.csproj item Compile changed", StringComparison.Ordinal), run.Text);
        run.Lines.ShouldNotContain("approved", run.Text);
        repo.AssertUntouched();
    }

    // An ignored link, or an item path outside the repository, is not an ignored repository file:
    // its canary-pass absence still refuses (fail closed). The pattern has no trailing slash so it
    // also ignores a directory link, which Git does not treat as a directory.
    [Test]
    [Arguments("file-link")]
    [Arguments("directory-link")]
    [Arguments("outside")]
    [Timeout(300_000)]
    public async Task C1385_IgnoredLinkOrOutsideItemRefuses(string kind)
    {
        var scratch = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "c1385-out-" + Guid.NewGuid().ToString("N")[..12])).FullName;
        var outsideFile = Path.Combine(scratch, "x.txt");
        await File.WriteAllTextAsync(outsideFile, "outside\n");
        try
        {
            var files = new Dictionary<string, string>
            {
                [".gitignore"] = "App/TestOutput\nbin/\n",
                ["App/Program.cs"] = "System.Console.WriteLine();\n",
                ["App/App.csproj"] = SdkProject(kind == "outside"
                    ? $"""<ItemGroup><None Include="{outsideFile}" Condition="Exists('bin/flag.local')" /></ItemGroup>"""
                    : ""),
            };
            var links = kind switch
            {
                "file-link" => new Dictionary<string, string> { ["App/TestOutput/link.log"] = "../Program.cs" },
                "directory-link" => new Dictionary<string, string> { ["App/TestOutput"] = scratch },
                _ => null,
            };
            await using var repo = await AuditRepo.CreateAsync(files, links: links,
                untrackedFiles: new() { ["App/bin/flag.local"] = "local\n" });

            var run = await repo.RunToolAsync();

            run.ExitCode.ShouldBe(1, run.Text);
            run.Lines.ShouldContain(l => l.StartsWith("refused: differential ", StringComparison.Ordinal)
                                         && l.Replace('\\', '/').EndsWith("App/App.csproj item None changed", StringComparison.Ordinal), run.Text);
            run.Lines.ShouldNotContain("approved", run.Text);
            repo.AssertUntouched();
            (await File.ReadAllTextAsync(outsideFile)).ShouldBe("outside\n");
            Names(scratch).ShouldBe(new[] { "x.txt" });
        }
        finally
        {
            if (Directory.Exists(scratch)) StartRefGit.DeleteDirectory(scratch);
        }
    }
}
