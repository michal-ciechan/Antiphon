using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class LandGraphEvaluationTests
{
    [Test]
    [Arguments(@"C:\Users\fixture\docs\flag.md", "C:/Users/fixture/docs/flag.md")]
    [Arguments(@"C:\Users\fixture\docs\missing.md", "C:/Users/fixture/docs/missing.md")]
    [Arguments(@"C:\Users\fixture\docs\new", "C:/Users/fixture/docs/new")]
    [Arguments(@"C:\Users\fixture\alias\flag.md", "C:/Users/fixture/alias/flag.md")]
    [Arguments(@"\\host\share\fixture\docs\flag.md", "//host/share/fixture/docs/flag.md")]
    [Arguments("/tmp/fixture/docs/flag.md", "/tmp/fixture/docs/flag.md")]
    public void C1483_PathDiagnosticsUsePortableSeparators(string path, string expected)
    {
        const string prefix = "refused: App/App.csproj file-probe FileOrDirectoryExists ";
        var run = new ToolRun(1, prefix + path);

        run.PathText.ShouldBe(prefix + expected);
        run.Text.ShouldBe(prefix + path, "retain the original diagnostic for failure evidence");
    }

    // V-11: recording catches an existence dependency even when the file does not exist yet.
    [Test]
    [Arguments("docs/flag.md")]
    [Arguments("docs/missing.md")]
    [Arguments("docs/new")]
    [Arguments("README.md")]
    [Arguments("alias/flag.md")]
    [Timeout(300_000)]
    public async Task C1483_S4_ExistsProbeOfDocsRefuses(string path)
    {
        await using var repo = await AuditRepo.CreateAsync(new()
        {
            ["README.md"] = "root",
            ["docs/flag.md"] = "flag",
            ["App/App.csproj"] = SdkProject($"""
                <ItemGroup><None Include="../docs/**/*.json" /></ItemGroup>
                <PropertyGroup Condition="Exists('$(MSBuildThisFileDirectory)../{path}')"><X>y</X></PropertyGroup>
                <ItemGroup Condition="Exists('$(MSBuildThisFileDirectory)../{path}')"><ProbeInput Include="safe.txt" /></ItemGroup>
                """),
        }, links: path.StartsWith("alias", StringComparison.Ordinal) ? new() { ["alias"] = "docs" } : null);
        var run = await repo.RunToolAsync();
        run.ExitCode.ShouldBe(1, run.Text);
        run.Text.ShouldContain("file-probe", Case.Sensitive, run.Text);
        run.Text.ShouldContain("App/App.csproj", Case.Sensitive, run.Text);
        run.PathText.ShouldContain(path, Case.Sensitive, run.Text);
        run.Lines.ShouldNotContain("approved", run.Text);
        repo.AssertUntouched();
    }

    // V-12: direct property functions bypass MSBuild's recording file system.
    [Test]
    [Arguments("README.md", false)]
    [Arguments("README.md", true)]
    [Arguments("docs/flag.md", false)]
    [Arguments("docs/flag.md", true)]
    [Timeout(300_000)]
    public async Task C1483_S4_FileReadingPropertyFunctionRefuses(string path, bool condition)
    {
        var spelling = path == "README.md" && condition ? "%52EADME.md" : path;
        var read = $"$([System.IO.File]::ReadAllText(&quot;$(MSBuildThisFileDirectory)../{spelling}&quot;))";
        var body = condition
            ? $"<PropertyGroup Condition=\"HasTrailingSlash('{read}')\"><ProbeValue>changed</ProbeValue></PropertyGroup>"
            : $"<PropertyGroup><ProbeValue>{read}</ProbeValue></PropertyGroup><ItemGroup><ProbeInput Include=\"safe.txt\" /></ItemGroup>";
        await using var repo = await AuditRepo.CreateAsync(new()
        {
            ["README.md"] = "safe/",
            ["docs/flag.md"] = "safe/",
            ["App/App.csproj"] = SdkProject(body),
        });
        var run = await repo.RunToolAsync();
        run.ExitCode.ShouldBe(1, run.Text);
        run.Text.ShouldContain("filesystem-function", Case.Sensitive, run.Text);
        run.Lines.ShouldNotContain("approved", run.Text);
        repo.AssertUntouched();

        // Matched allowance: recorded non-skippable Exists and lexical functions remain legal.
        await using var safe = await AuditRepo.CreateAsync(new()
        {
            ["App/value.txt"] = "safe/",
            ["App/App.csproj"] = SdkProject("""
                <PropertyGroup Condition="Exists('$(MSBuildThisFileDirectory)value.txt') And HasTrailingSlash('docs/')">
                  <ProbeValue>$([System.IO.Path]::GetFileName('safe/value.txt'))</ProbeValue>
                </PropertyGroup>
                """),
        });
        var safeRun = await safe.RunToolAsync();
        safeRun.ExitCode.ShouldBe(0, safeRun.Text);
        safeRun.Lines.ShouldContain("approved", safeRun.Text);
        safe.AssertUntouched();
    }

    [Test]
    [Timeout(300_000)]
    public async Task C1483_S4_RealRepositoryApproves()
    {
        var run = await AuditRepo.RunToolAtAsync(RepoRoot());
        run.ExitCode.ShouldBe(0, run.Text);
        run.Lines.ShouldContain("approved", run.Text);
        run.Text.ShouldContain("not-built: docs/investigations/evidence/card-0462/Reproduce.csproj");
    }

    // CARD-1614: the land pre-check refuses a definition file that sets a property outside its
    // allow-list before it compares the digest. These two projects are definition files, and the
    // check walks every definition file, so a new disallowed property cannot pass silently.
    [Test]
    [Timeout(300_000)]
    public async Task C1614_RealDefinitionFilesSetOnlyAllowListedProperties()
    {
        var root = RepoRoot();
        foreach (var path in new[]
        {
            "tests/Antiphon.PtyHost.Tests/Antiphon.PtyHost.Tests.csproj",
            "tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj",
        })
        {
            File.Exists(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar))).ShouldBeTrue(path);
        }

        var run = await RunScriptAsync(root, "-Check", "audit-land-verification-precheck.ps1");
        run.Text.ShouldNotContain("sets a property the audit does not allow", Case.Sensitive, run.Text);
        run.Text.ShouldNotContain("_PtyHostNativeProps", Case.Sensitive, run.Text);
        run.Text.ShouldContain("digest=", Case.Sensitive, run.Text);
        run.Text.ShouldContain("count=", Case.Sensitive, run.Text);
        if (run.ExitCode != 0)
        {
            run.ExitCode.ShouldBe(1, run.Text);
            run.Text.ShouldContain("manifest does not match the selected graph", Case.Sensitive, run.Text);
        }
    }
}
