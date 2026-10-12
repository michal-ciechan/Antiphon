using System.Diagnostics;
using System.Text;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

// Real dotnet builds. Each preservation case reaches Lib through a graph the static
// project-XML scan does not see, then requires the tracked Lib bytes to survive and
// the build output to land under App/bin-probe/.
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointOutputConfinementTests : CheckpointTestBase
{
    private const string TrackedDllText = "TRACKED DEPENDENCY ORIGINAL";
    private const string OutputPath = "bin-probe/";

    [Test]
    [Timeout(300_000)]
    public async Task imported_props_build_preserves_the_tracked_dependency(CancellationToken cancellationToken)
    {
        RequireLinux();
        var fixture = CreateImportedProps();
        await AssertPreservedAsync(fixture, "confine-import", cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task directory_build_props_build_preserves_the_tracked_dependency(CancellationToken cancellationToken)
    {
        RequireLinux();
        var fixture = CreateDirectoryBuildProps();
        await AssertPreservedAsync(fixture, "confine-dirprops", cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task conditional_import_build_preserves_the_tracked_dependency(CancellationToken cancellationToken)
    {
        RequireLinux();
        var fixture = CreateConditionalImport();
        await AssertPreservedAsync(fixture, "confine-conditional", cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task transitive_import_build_preserves_the_tracked_dependency(CancellationToken cancellationToken)
    {
        RequireLinux();
        var fixture = CreateTransitiveImport();
        await AssertPreservedAsync(fixture, "confine-transitive", cancellationToken);
    }

    [Test]
    [Timeout(180_000)]
    public async Task unevaluable_import_is_refused_before_the_build(CancellationToken cancellationToken)
    {
        RequireLinux();
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

        // The tool creates bin-probe before the graph refusal. The script is a
        // second run and refuses a directory it did not create.
        RemoveUntrackedOutput(root);
        var script = await RunScriptAsync(root, "confine-unevaluable-script", cancellationToken);
        AssertUnevaluable(script, sentinel, root);
    }

    private async Task AssertPreservedAsync(Fixture fixture, string name, CancellationToken cancellationToken)
    {
        var tool = await RunToolAsync(fixture.Root, name + "-tool", cancellationToken);
        AssertOwnedAndUnchanged(fixture, tool.Output, "tool");
        Restore(fixture);
        RemoveUntrackedOutput(fixture.Root);
        var script = await RunScriptAsync(fixture.Root, name + "-script", cancellationToken);
        AssertOwnedAndUnchanged(fixture, script, "script");
    }

    private static void AssertOwnedAndUnchanged(Fixture fixture, string output, string entry)
    {
        var detail = entry + " output:\n" + output;
        File.ReadAllBytes(fixture.Dll).ShouldBe(fixture.DllBytes, detail);
        File.ReadAllBytes(fixture.Assets).ShouldBe(fixture.AssetBytes, detail);
        File.Exists(Path.Combine(fixture.Root, "App", "bin-probe", "Lib.dll")).ShouldBeTrue(detail);
        File.Exists(Path.Combine(fixture.Root, "App", "bin-probe", "artifacts", "obj", "Lib", "project.assets.json")).ShouldBeTrue(detail);
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
        var output = new StringWriter();
        var exit = await Antiphon.Checkpoints.Program.RunAsync(
        [
            "row", "--repo-root", root, "--slots", "off",
            "--name", name, "--project", "App/App.csproj",
            "--output-path", OutputPath, "--filter", "/*/*/Unused/*",
            "--results-root", ".antiphon/tool-results", "--min-executed", "1",
        ], new CheckpointApp.Runtime { Output = output });
        cancellationToken.ThrowIfCancellationRequested();
        return (exit, output.ToString());
    }

    private async Task<string> RunScriptAsync(string root, string name, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[]
        {
            "-NoProfile", "-NonInteractive", "-File",
            Path.Combine(CheckpointFixtures.RepoRoot, "scripts", "run-checkpoint.ps1"),
            "-Name", name, "-Project", "App/App.csproj", "-OutputPath", OutputPath,
            "-Filter", "/*/*/Unused/*", "-ResultsRoot", ".antiphon/script-results",
            "-MinExecuted", "1", "-NoSlot",
        })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start);
        process.ShouldNotBeNull();
        RegisterCheckpointChild(process);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (await stdoutTask) + (await stderrTask);
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
        // The seed build's App and Mid output is untracked. A checkpoint refuses
        // that directory before it can preserve the tracked Lib bytes.
        RemoveUntrackedOutput(root);
        return new Fixture(root, dll, File.ReadAllBytes(dll), assets, File.ReadAllBytes(assets));
    }

    private static void RemoveUntrackedOutput(string root)
    {
        foreach (var relative in new[] { "App/bin-probe", "App/obj", "Mid/bin-probe", "Mid/obj" })
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
    }

    private static void SeedBuild(string root)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[]
        {
            "build", "App/App.csproj", "--property:OutputPath=bin-probe/",
            "--property:UseAppHost=false", "-nodeReuse:false", "--nologo",
        })
            start.ArgumentList.Add(arg);
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
        using var process = Process.Start(start);
        process.ShouldNotBeNull();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(120_000).ShouldBeTrue(stdout + stderr);
        process.ExitCode.ShouldBe(0, stdout + stderr);
    }

    private static void Restore(Fixture fixture)
    {
        File.WriteAllBytes(fixture.Dll, fixture.DllBytes);
        File.WriteAllBytes(fixture.Assets, fixture.AssetBytes);
    }

    private static void RequireLinux()
    {
        if (!OperatingSystem.IsLinux())
            Skip.Test("CARD-1199 output confinement real-build proof runs on Linux; Windows is unverified.");
    }

    private string InitGitRepo()
    {
        var root = TempDir();
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
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void Git(string root, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment.Remove("GIT_DIR");
        start.Environment.Remove("GIT_WORK_TREE");
        start.Environment.Remove("GIT_INDEX_FILE");
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start);
        process.ShouldNotBeNull();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(30_000).ShouldBeTrue(stdout + stderr);
        process.ExitCode.ShouldBe(0, stdout + stderr);
    }

    private sealed record Fixture(string Root, string Dll, byte[] DllBytes, string Assets, byte[] AssetBytes);
}
