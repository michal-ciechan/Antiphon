using System.Diagnostics;
using System.Text;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

// Real dotnet builds through the row verb (scripts/run-checkpoint.ps1 launches it since unit 20, so the script leg these
// cases once ran is the same code). Each preservation case reaches Lib through a graph the static project-XML scan does
// not see, then requires the tracked Lib bytes to survive and the build output to land under App/bin-confined/.
[Category("Unit")]
[Category("Slow")]
[ParallelLimiter<CheckpointToolSpawnLimit>]
public sealed class CheckpointOutputConfinementTests : CheckpointTestBase
{
    private const string TrackedDllText = "TRACKED DEPENDENCY ORIGINAL";
    private const string OutputPath = "bin-confined/";
    private CheckpointFixtureGitEnvironment _git = null!;

    [Test]
    [Timeout(300_000)]
    public async Task imported_props_build_preserves_the_tracked_dependency(CancellationToken cancellationToken)
    {
        var fixture = CreateImportedProps();
        await AssertPreservedAsync(fixture, "confine-import", cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task directory_build_props_build_preserves_the_tracked_dependency(CancellationToken cancellationToken)
    {
        var fixture = CreateDirectoryBuildProps();
        await AssertPreservedAsync(fixture, "confine-dirprops", cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task conditional_import_build_preserves_the_tracked_dependency(CancellationToken cancellationToken)
    {
        var fixture = CreateConditionalImport();
        await AssertPreservedAsync(fixture, "confine-conditional", cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task transitive_import_build_preserves_the_tracked_dependency(CancellationToken cancellationToken)
    {
        var fixture = CreateTransitiveImport();
        await AssertPreservedAsync(fixture, "confine-transitive", cancellationToken);
    }

    [Test]
    [Timeout(180_000)]
    public async Task unevaluable_import_is_refused_before_the_build(CancellationToken cancellationToken)
    {
        var root = InitGitRepo();
        Write(root, ".gitignore", "bin-*/\nobj/\n.antiphon/\n.checkpoint-test-root.json\n");
        Write(root, "keep.txt", "KEEP-SENTINEL");
        Write(root, "App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
              <Import Project="Missing.props" />
            </Project>
            """);
        Write(root, "App/Marker.cs", "namespace App; public static class Marker { public static int Value => 1; }\n");
        Git(root, "add", "-A");
        CommitAll(root);
        var sentinel = File.ReadAllBytes(Path.Combine(root, "keep.txt"));

        var tool = await RunToolAsync(root, "confine-unevaluable", cancellationToken);
        AssertUnevaluable(tool.Output, sentinel, root);
    }

    // CARD-1310: on Windows the confined build leaves UseAppHost unset unless the row names it.
    [Test]
    [Timeout(300_000)]
    public void windows_confined_build_leaves_use_app_host_unset(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            Antiphon.Tests.Scripts.WindowsUnitSkipRoster.SkipOnLinux(nameof(CheckpointOutputConfinementTests), nameof(windows_confined_build_leaves_use_app_host_unset));

        // Prepare stats with system git config disabled. A host autocrlf commit
        // then looks dirty. InitGitRepo stores the fixture bytes as written (core.autocrlf=false).
        var root = InitGitRepo();
        Write(root, ".gitignore", "bin-*/\nobj/\n.antiphon/\n");
        Write(root, "App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        Write(root, "App/Program.cs", "Console.WriteLine(1);\n");
        Git(root, "add", "-A");
        CommitAll(root);

        try
        {
            var prepared = BuildConfinement.Prepare(
                root,
                ["build", "App/App.csproj", "--property:OutputPath=bin-c1310/"],
                null,
                cancellationToken);
            prepared.Refusal.ShouldBeNull(prepared.Detail);
            prepared.Arguments.Any(arg => arg.Contains("UseAppHost", StringComparison.OrdinalIgnoreCase))
                .ShouldBeFalse(string.Join("\n", prepared.Arguments));

            var explicitHost = BuildConfinement.Prepare(
                root,
                ["build", "App/App.csproj", "--property:OutputPath=bin-c1310/", "--property:UseAppHost=true"],
                null,
                cancellationToken);
            explicitHost.Refusal.ShouldBeNull(explicitHost.Detail);
            explicitHost.Arguments.ShouldContain("--property:UseAppHost=true");
            explicitHost.Arguments.Any(arg => arg.Contains("UseAppHost=false", StringComparison.OrdinalIgnoreCase))
                .ShouldBeFalse(string.Join("\n", explicitHost.Arguments));
        }
        finally
        {
            // Git loose objects are read-only. Directory.Delete in fixture teardown denies them.
            ClearReadOnly(root);
        }
    }

    private async Task AssertPreservedAsync(Fixture fixture, string name, CancellationToken cancellationToken)
    {
        var tool = await RunToolAsync(fixture.Root, name + "-tool", cancellationToken);
        AssertOwnedAndUnchanged(fixture, tool.Output);
    }

    private static void AssertOwnedAndUnchanged(Fixture fixture, string output)
    {
        var detail = "row output:\n" + output;
        var directory = "bin-confined";
        File.ReadAllBytes(fixture.Dll).ShouldBe(fixture.DllBytes, detail);
        File.ReadAllBytes(fixture.Assets).ShouldBe(fixture.AssetBytes, detail);
        File.Exists(Path.Combine(fixture.Root, "App", directory, "Lib.dll")).ShouldBeTrue(detail);
        File.Exists(Path.Combine(fixture.Root, "App", directory, "artifacts", "obj", "Lib", "project.assets.json")).ShouldBeTrue(detail);
        output.ShouldNotContain("source_changed", Case.Sensitive);
        output.ShouldNotContain("refused: tampered", Case.Sensitive);
        output.ShouldNotContain("refused: graph-unevaluated", Case.Sensitive);
    }

    private static void AssertUnevaluable(string output, byte[] sentinel, string root)
    {
        File.ReadAllBytes(Path.Combine(root, "keep.txt")).ShouldBe(sentinel, output);
        output.ShouldContain("refused: graph-unevaluated", Case.Sensitive);
        output.ShouldNotContain("Build succeeded", Case.Sensitive);
    }

    private async Task<(int Exit, string Output)> RunToolAsync(string root, string name, CancellationToken cancellationToken)
    {
        using var phase = TestTiming.BeginPhase(TestPhaseKind.OurCode, "checkpoint-tool");
        var output = new StringWriter();
        var exit = await Antiphon.Checkpoints.Program.RunAsync(
        [
            "row", "--repo-root", root, "--slots", "off",
            "--name", name, "--project", "App/App.csproj",
            "--output-path", OutputPath, "--filter", "/*/*/Unused/*",
            "--results-root", ".antiphon/tool-results", "--min-executed", "1",
        ], _git.ToolRuntime(output));
        cancellationToken.ThrowIfCancellationRequested();
        return (exit, output.ToString());
    }

    private static void ClearReadOnly(string path)
    {
        if (!Directory.Exists(path))
            return;
        var root = new DirectoryInfo(path) { Attributes = FileAttributes.Normal };
        foreach (var entry in root.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
            entry.Attributes = FileAttributes.Normal;
    }

    private Fixture CreateImportedProps()
    {
        var root = BeginRepo();
        Write(root, "App/References.props", """
            <Project>
              <ItemGroup>
                <ProjectReference Include="..\Lib\Lib.csproj" />
              </ItemGroup>
            </Project>
            """);
        Write(root, "App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
              <Import Project="References.props" />
            </Project>
            """);
        Write(root, "App/Marker.cs", "namespace App; public static class Marker { public static int Use() => Lib.Marker.Value; }\n");
        return Finish(root);
    }

    private Fixture CreateDirectoryBuildProps()
    {
        var root = BeginRepo();
        Write(root, "Directory.Build.props", """
            <Project>
              <ItemGroup Condition="'$(MSBuildProjectName)' == 'App'">
                <ProjectReference Include="$(MSBuildThisFileDirectory)Lib/Lib.csproj" />
              </ItemGroup>
            </Project>
            """);
        Write(root, "App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        Write(root, "App/Marker.cs", "namespace App; public static class Marker { public static int Use() => Lib.Marker.Value; }\n");
        return Finish(root);
    }

    private Fixture CreateConditionalImport()
    {
        var root = BeginRepo();
        Write(root, "App/Refs.props", """
            <Project>
              <ItemGroup>
                <ProjectReference Include="..\Lib\Lib.csproj" />
              </ItemGroup>
            </Project>
            """);
        Write(root, "App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <ImportRefs>true</ImportRefs>
              </PropertyGroup>
              <Import Project="Refs.props" Condition="'$(ImportRefs)' == 'true'" />
              <Import Project="Missing.props" Condition="'$(ImportRefs)' == 'false'" />
            </Project>
            """);
        Write(root, "App/Marker.cs", "namespace App; public static class Marker { public static int Use() => Lib.Marker.Value; }\n");
        return Finish(root);
    }

    private Fixture CreateTransitiveImport()
    {
        var root = BeginRepo();
        Write(root, "Mid/Mid.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
              <ItemGroup><ProjectReference Include="..\Lib\Lib.csproj" /></ItemGroup>
            </Project>
            """);
        Write(root, "Mid/M.cs", "namespace Mid; public static class M { public static int Use() => Lib.Marker.Value; }\n");
        Write(root, "App/References.props", """
            <Project>
              <ItemGroup>
                <ProjectReference Include="..\Mid\Mid.csproj" />
              </ItemGroup>
            </Project>
            """);
        Write(root, "App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
              <Import Project="References.props" />
            </Project>
            """);
        Write(root, "App/Marker.cs", "namespace App; public static class Marker { public static int Use() => Mid.M.Use(); }\n");
        return Finish(root);
    }

    private string BeginRepo()
    {
        var root = InitGitRepo();
        Write(root, ".gitignore", "bin-*/\nobj/\n.antiphon/\n.checkpoint-test-root.json\n");
        Write(root, "Lib/Lib.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        Write(root, "Lib/Marker.cs", "namespace Lib; public static class Marker { public static int Value => 7; }\n");
        return root;
    }

    private Fixture Finish(string root)
    {
        SeedBuild(root);
        var dll = Path.Combine(root, "Lib", "bin-probe", "Lib.dll");
        var assets = Path.Combine(root, "Lib", "obj", "project.assets.json");
        File.Exists(dll).ShouldBeTrue("seed build did not write Lib/bin-probe/Lib.dll");
        File.Exists(assets).ShouldBeTrue("seed build did not write Lib/obj/project.assets.json");
        File.WriteAllBytes(dll, Encoding.ASCII.GetBytes(TrackedDllText));
        Git(root, "add", "-A");
        Git(root, "add", "-f", "--", "Lib/bin-probe/Lib.dll", "Lib/obj/project.assets.json");
        CommitAll(root);
        return new Fixture(root, dll, File.ReadAllBytes(dll), assets, File.ReadAllBytes(assets));
    }

    private void SeedBuild(string root)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root };
        foreach (var arg in new[]
        {
            "build", "App/App.csproj", "--property:OutputPath=bin-probe/",
            "--property:UseAppHost=false", "-nodeReuse:false", "--nologo",
        })
            start.ArgumentList.Add(arg);
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
        _git.Apply(start);
        var output = CheckpointFixtureProcess.Run(start, 120_000);
        output.Exit.ShouldBe(0, output.Stdout + output.Stderr);
    }

    private string InitGitRepo()
    {
        var scope = TempDir();
        var root = Path.Combine(scope, "repo");
        Directory.CreateDirectory(root);
        _git = CheckpointFixtureGitEnvironment.Create(Path.Combine(scope, "git-isolation"));
        Git(root, "init", "-b", "card1199");
        return root;
    }

    private void CommitAll(string root)
    {
        Git(root, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "fixture", "--no-verify");
    }

    private static void Write(string root, string relative, string content)
    {
        if (relative.EndsWith(".csproj", StringComparison.Ordinal)) content = content.Replace("<PropertyGroup>", "<PropertyGroup><DefaultItemExcludes>$(DefaultItemExcludes);bin-*/**</DefaultItemExcludes>", StringComparison.Ordinal);
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void Git(string root, params string[] args) => _git.Git(root, args);

    private sealed record Fixture(string Root, string Dll, byte[] DllBytes, string Assets, byte[] AssetBytes);
}
