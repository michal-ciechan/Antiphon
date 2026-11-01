using System.Diagnostics;
using System.Text;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

// Real dotnet builds. Each preservation case reaches Lib through a graph the static
// project-XML scan does not see, then requires the tracked Lib bytes to survive and
// the build output to land under App/bin-confined/ (tool) or App/bin-script-confined/ (script).
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
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

        var script = await RunScriptAsync(root, "confine-unevaluable-script", cancellationToken);
        AssertUnevaluable(script, sentinel, root);
    }

    // CreateProcess resolves "dotnet" from the launching process PATH, and only as dotnet.exe.
    // The checkpoint script is that parent, so its Path starts with a logging apphost.
    [Test]
    [Timeout(300_000)]
    public async Task windows_confined_build_leaves_use_app_host_unset(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            Skip.Test("CARD-1310 confined Windows build leaves UseAppHost unset");

        var root = InitGitRepo();
        // Prepare and the script stat with system git config disabled. A host autocrlf commit
        // then looks dirty. Store the fixture bytes as written.
        Git(root, "config", "core.autocrlf", "false");
        Write(root, ".gitignore", "bin-*/\nobj/\n.antiphon/\n");
        Write(root, "App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        Write(root, "App/Program.cs", "Console.WriteLine(1);\n");
        Git(root, "add", "-A");
        CommitAll(root);

        string? shimRoot = null;
        string? resultsRoot = null;
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

            shimRoot = Path.Combine(Path.GetTempPath(), "c1310-shim-" + Guid.NewGuid().ToString("N"));
            resultsRoot = Path.Combine(Path.GetTempPath(), "c1310-results-" + Guid.NewGuid().ToString("N"));
            var shimBin = BuildUseAppHostShim(shimRoot);
            Directory.CreateDirectory(resultsRoot);
            var log = Path.Combine(shimRoot, "calls.log");
            var output = await RunConfinedScriptAsync(root, resultsRoot, shimBin, log, cancellationToken);
            var lines = File.Exists(log) ? File.ReadAllLines(log) : [];
            var detail = output + "\n--- log ---\n" + string.Join("\n", lines);
            var msbuild = FirstVerb(lines, "msbuild");
            var build = FirstVerb(lines, "build");
            (msbuild is not null).ShouldBeTrue(detail);
            (build is not null).ShouldBeTrue(detail);
            msbuild!.Contains("UseAppHost=false", StringComparison.OrdinalIgnoreCase).ShouldBeFalse(detail);
            build!.Contains("UseAppHost=false", StringComparison.OrdinalIgnoreCase).ShouldBeFalse(detail);
            var run = FirstVerb(lines, "run");
            if (run is not null)
                run.Contains("UseAppHost", StringComparison.OrdinalIgnoreCase).ShouldBeFalse(detail);
        }
        finally
        {
            // Git loose objects are read-only. Directory.Delete in fixture teardown denies them.
            ClearReadOnly(root);
            if (shimRoot is not null)
                DeleteIfPresent(shimRoot);
            if (resultsRoot is not null)
                DeleteIfPresent(resultsRoot);
        }
    }

    private async Task AssertPreservedAsync(Fixture fixture, string name, CancellationToken cancellationToken)
    {
        var tool = await RunToolAsync(fixture.Root, name + "-tool", cancellationToken);
        AssertOwnedAndUnchanged(fixture, tool.Output, "tool");
        Restore(fixture);
        var script = await RunScriptAsync(fixture.Root, name + "-script", cancellationToken);
        AssertOwnedAndUnchanged(fixture, script, "script");
    }

    private static void AssertOwnedAndUnchanged(Fixture fixture, string output, string entry)
    {
        var detail = entry + " output:\n" + output;
        var directory = entry == "tool" ? "bin-confined" : "bin-script-confined";
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
            "-Name", name, "-Project", "App/App.csproj", "-OutputPath", "bin-script-confined/",
            "-Filter", "/*/*/Unused/*", "-ResultsRoot", ".antiphon/script-results",
            "-MinExecuted", "1", "-NoSlot",
        })
            start.ArgumentList.Add(arg);
        _git.Apply(start);
        return await RunScriptAsync(start, cancellationToken);
    }

    internal async Task<string> RunScriptAsync(ProcessStartInfo start, CancellationToken cancellationToken)
    {
        var result = await CheckpointFixtureProcess.RunAsync(start, cancellationToken);
        return result.Stdout + result.Stderr;
    }

    private static string BuildUseAppHostShim(string shimRoot)
    {
        var projectDir = Path.Combine(shimRoot, "src");
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(Path.Combine(projectDir, "shim.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net9.0</TargetFramework>
                <AssemblyName>dotnet</AssemblyName>
                <ImplicitUsings>disable</ImplicitUsings>
                <Nullable>disable</Nullable>
                <UseAppHost>true</UseAppHost>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(projectDir, "Program.cs"), """
            using System;
            using System.IO;
            internal static class Program
            {
                private static int Main(string[] args)
                {
                    var log = Environment.GetEnvironmentVariable("C1310_DOTNET_LOG");
                    if (!string.IsNullOrEmpty(log))
                        File.AppendAllText(log, string.Join(" ", args) + "\n");
                    if (args.Length > 0 && string.Equals(args[0], "msbuild", StringComparison.Ordinal))
                        Console.WriteLine("{\"Items\":{}}");
                    return 0;
                }
            }
            """);
        var bin = Path.Combine(shimRoot, "bin");
        var output = bin.Replace('\\', '/').TrimEnd('/') + "/";
        var start = new ProcessStartInfo(SdkDotnet()) { WorkingDirectory = projectDir };
        foreach (var arg in new[] { "build", "shim.csproj", "--property:OutputPath=" + output, "--nologo" })
            start.ArgumentList.Add(arg);
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
        var built = CheckpointFixtureProcess.Run(start, 120_000);
        var exe = Path.Combine(bin, "dotnet.exe");
        File.Exists(exe).ShouldBeTrue(built.Stdout + built.Stderr);
        built.Exit.ShouldBe(0, built.Stdout + built.Stderr);
        return bin;
    }

    private async Task<string> RunConfinedScriptAsync(
        string root, string resultsRoot, string shimBin, string log, CancellationToken cancellationToken)
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
            "-Name", "CP-1", "-Project", "App/App.csproj", "-OutputPath", "bin-c1310s/",
            "-Filter", "/*/*/Unused/*", "-ResultsRoot", resultsRoot,
            "-MinExecuted", "1", "-NoSlot",
        })
            start.ArgumentList.Add(arg);
        var path = Environment.GetEnvironmentVariable("Path") ?? Environment.GetEnvironmentVariable("PATH") ?? "";
        start.Environment["Path"] = shimBin + Path.PathSeparator + path;
        start.Environment["C1310_DOTNET_LOG"] = log;
        _git.Apply(start);
        return await RunConfinedScriptAsync(start, cancellationToken);
    }

    internal async Task<string> RunConfinedScriptAsync(ProcessStartInfo start, CancellationToken cancellationToken)
    {
        var result = await CheckpointFixtureProcess.RunAsync(start, cancellationToken);
        return result.Stdout + result.Stderr;
    }

    private static string SdkDotnet()
    {
        var path = Environment.GetEnvironmentVariable("Path") ?? Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), "dotnet.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("dotnet.exe");
    }

    private static string? FirstVerb(string[] lines, string verb)
    {
        foreach (var line in lines)
            if (line.Equals(verb, StringComparison.Ordinal) || line.StartsWith(verb + " ", StringComparison.Ordinal))
                return line;
        return null;
    }

    private static void DeleteIfPresent(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
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

    private static void Restore(Fixture fixture)
    {
        File.WriteAllBytes(fixture.Dll, fixture.DllBytes);
        File.WriteAllBytes(fixture.Assets, fixture.AssetBytes);
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
