using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<CheckpointToolSpawnLimit>]
public sealed class CheckpointColdBuildTests : CheckpointTestBase
{
    private const string Project = "tests/Antiphon.SessionRunner.Tests/Probe.csproj";
    private const string Filter = "/*/*/ColdBuildProbe/*";

    [Test]
    [Arguments("tool")]
    [Arguments("row")]
    [Timeout(180_000)]
    public async Task C1734_Cold_build_preserves_native_obj_and_terminal_receipt(string entry, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("Exec target builds the Linux native helper");
        var fixture = CreateFixture();
        var seed = fixture.Git.Command("dotnet", fixture.Root);
        foreach (var arg in new[] { "build", "src/Antiphon.PtyHost/Antiphon.PtyHost.csproj", "-p:UseAppHost=false", "-nodeReuse:false", "--nologo" })
            seed.ArgumentList.Add(arg);
        var seeded = await CheckpointFixtureProcess.RunAsync(seed, cancellationToken);
        seeded.Exit.ShouldBe(0, seeded.Stdout + seeded.Stderr);
        var native = Path.Combine(fixture.Root, "src/Antiphon.PtyHost/obj");
        var before = Snapshot(native);
        before.Keys.ShouldContain("project.assets.json");
        before.Keys.ShouldContain("project.nuget.cache");

        var result = entry == "tool"
            ? await RunToolAsync(fixture, cancellationToken)
            : await RunRowAsync(fixture, cancellationToken);
        // Check custody before success: ignoring obj differences cannot satisfy this witness.
        Snapshot(native).ShouldBe(before, result.Detail);
        result.Exit.ShouldBe(0, result.Detail);
        result.Detail.ShouldContain("executed=1 passed=1 failed=0 skipped=0");
        result.Detail.ShouldNotContain("refused: tampered");
    }

    [Test]
    [Timeout(180_000)]
    public async Task C1734_Real_tampering_keeps_a_red_terminal_report(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("Exec target builds the Linux native helper");
        var fixture = CreateFixture(tamper: true);
        var result = await RunToolAsync(fixture, cancellationToken);
        result.Exit.ShouldBe(2, result.Detail);
        result.Report!.Builds.Single().SlotReason.ShouldBe("refused: tampered");
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(result.Report.Evidence)!, "builds/bin-c1734/build.log"))
            .ShouldContain("TAMPERED keep.txt");
        File.ReadAllText(Path.Combine(fixture.Root, "keep.txt")).Trim().ShouldBe("CHANGED");
    }

    private async Task<(int Exit, string Detail, ReportModel? Report)> RunToolAsync(Fixture fixture, CancellationToken cancellationToken)
    {
        var output = new StringWriter();
        var isolated = fixture.Git.ToolRuntime(output);
        var runtime = new CheckpointApp.Runtime
        {
            Driver = isolated.Driver, Output = output, EnvironmentLookup = _ => null,
            Slots = new FixedSlotClient("off"),
        };
        var manifest = new CheckpointManifest
        {
            ResultsRoot = ".antiphon/cold-tool",
            Builds = [new BuildSpec { Id = "bin-c1734", Project = Project, OutputPath = "bin-c1734/" }],
            Checkpoints = [new CheckpointSpec
            {
                Id = "CP-1", After = ["all"], Build = "bin-c1734", Filter = Filter,
                MinExecuted = 1, Expect = ["ColdBuildProbe"],
            }],
        };
        var sha = fixture.Git.Git(fixture.Root, "rev-parse", "HEAD").Trim();
        var run = CheckpointApp.CreateRun(manifest, new RunRequest
        {
            ExpectedSourceSha = sha, KeepOutputs = true, Slots = "off",
        }, fixture.Root, runtime);
        var exit = await CheckpointApp.ExecuteAsync(run, cancellationToken, runtime);
        var state = new RunStateStore().TryRead(Path.Combine(run, "state.json"));
        state.ShouldNotBeNull(output.ToString());
        state.Phase.ShouldBe("done", output.ToString());
        state.ExitCode.ShouldBe(exit);
        ReportWriter.RequiredReceiptRefusal(run, state).ShouldBeNull(output.ToString());
        var report = JsonSerializer.Deserialize<ReportModel>(File.ReadAllText(Path.Combine(run, "report.json")), CheckpointApp.Json)!;
        report.Commit.ShouldBe(sha);
        report.ExitCode.ShouldBe(exit);
        report.Rows.Count.ShouldBe(1);
        var detail = File.ReadAllText(Path.Combine(run, "report.md"));
        Console.WriteLine(detail);
        return (exit, detail, report);
    }

    // The row verb, which scripts/run-checkpoint.ps1 launches (unit 20): the same build in process, its receipt in report.json.
    private async Task<(int Exit, string Detail, ReportModel? Report)> RunRowAsync(Fixture fixture, CancellationToken cancellationToken)
    {
        var output = new StringWriter();
        var runtime = new CheckpointApp.Runtime
        {
            Driver = fixture.Git.ToolRuntime(output).Driver, Output = output, EnvironmentLookup = _ => null,
            CancellationToken = cancellationToken,
        };
        var exit = await Antiphon.Checkpoints.Program.RunAsync(
        [
            "row", "--repo-root", fixture.Root, "--name", "CP-1", "--project", Project, "--output-path", "bin-c1734/", "--filter", Filter,
            "--results-root", ".antiphon/cold-row", "--min-executed", "1", "--expect", "ColdBuildProbe", "--no-slot",
            "--expected-source-sha", fixture.Git.Git(fixture.Root, "rev-parse", "HEAD").Trim(),
        ], runtime);
        var receipt = Directory.GetFiles(Path.Combine(fixture.Root, ".antiphon/cold-row"), "report.json", SearchOption.AllDirectories).Single();
        var report = JsonSerializer.Deserialize<ReportModel>(File.ReadAllText(receipt), CheckpointApp.Json)!;
        output.ToString().ShouldContain(report.Rows.Single().Line!);
        Console.WriteLine(output);
        return (exit, output.ToString(), report);
    }

    private Fixture CreateFixture(bool tamper = false)
    {
        var scope = TempDir();
        var root = Path.Combine(scope, "repo");
        Directory.CreateDirectory(root);
        var git = CheckpointFixtureGitEnvironment.Create(Path.Combine(scope, "git-isolation"));
        git.Git(root, "init", "-b", "card1734");
        Write(".gitignore", "bin/\nobj/\nbin-*/\n.antiphon/\n");
        Write("keep.txt", "ORIGINAL\n");
        Write("Directory.Build.props", "<Project><PropertyGroup><DefaultItemExcludes>$(DefaultItemExcludes);bin-*/**</DefaultItemExcludes></PropertyGroup></Project>");
        Write("src/Antiphon.PtyHost/Antiphon.PtyHost.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup>
              <Target Name="NativePayload" AfterTargets="Build">
                <WriteLinesToFile File="$(TargetDir)libporta_pty.so" Lines="C1734 native payload" Overwrite="true" />
              </Target>
            </Project>
            """);
        Write("src/Antiphon.PtyHost/Program.cs", "System.Console.WriteLine(1734);");
        var app = XDocument.Parse("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework><OutputType>Exe</OutputType>
                <IsTestProject>true</IsTestProject><EnableMicrosoftTestingPlatformRunner>true</EnableMicrosoftTestingPlatformRunner>
              </PropertyGroup>
              <ItemGroup><PackageReference Include="TUnit" Version="1.44.0" /></ItemGroup>
            </Project>
            """);
        // Execute the shipped target verbatim, including its OS selection and child command.
        var shipped = XDocument.Load(Path.Combine(CheckpointFixtures.RepoRoot,
            "tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj"));
        app.Root!.Add(new XElement(shipped.Root!.Elements("Target").Single(target =>
            (string?)target.Attribute("Name") == "CopyOwnedPtyHostAppHostForHerdr")));
        if (tamper) app.Root.Add(XElement.Parse("""
            <Target Name="Tamper" AfterTargets="Build"><WriteLinesToFile File="$(MSBuildThisFileDirectory)../../keep.txt" Lines="CHANGED" Overwrite="true" /></Target>
            """));
        Write(Project, app.ToString());
        Write("tests/Antiphon.SessionRunner.Tests/Probe.cs", """
            public sealed class ColdBuildProbe
            {
                [TUnit.Core.Test]
                public void Native_payload_is_staged()
                {
                    var root = System.AppContext.BaseDirectory;
                    if (!System.IO.File.Exists(System.IO.Path.Combine(root, "Antiphon.PtyHost")))
                        throw new System.Exception("native apphost was not staged");
                    if (System.IO.File.ReadAllText(System.IO.Path.Combine(root, "libporta_pty.so")).Trim() != "C1734 native payload")
                        throw new System.Exception("native payload was not staged");
                }
            }
            """);
        git.Git(root, "add", "-A");
        git.Git(root, "-c", "user.email=card1734@example.invalid", "-c", "user.name=card1734",
            "-c", "commit.gpgsign=false", "commit", "--no-verify", "-m", "fixture");
        return new Fixture(root, git);

        void Write(string relative, string text)
        {
            var path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
    }

    private static Dictionary<string, string> Snapshot(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(directory, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));

    private sealed record Fixture(string Root, CheckpointFixtureGitEnvironment Git);
}
