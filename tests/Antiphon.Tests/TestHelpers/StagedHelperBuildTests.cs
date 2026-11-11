using System.Diagnostics;
using System.Xml.Linq;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

// CARD-1841: under the checkpoint tool's one absolute OutputPath, CopyStagedHelpers builds each helper
// into its own folder (CARD-1238). The OutDir it passes reached the helper's ProjectReferences too,
// so Antiphon.Server compiled again into host-load-probe/ and channel-outbound-probe/ (168 s of a
// 455 s build). These build a Lib <- Helper <- Consumer fixture that runs the real target.
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class StagedHelperBuildTests
{
    [Test]
    [Timeout(180_000)]
    public async Task C1841_Shared_output_stages_a_helper_without_rebuilding_its_references(CancellationToken cancellationToken)
    {
        var root = CreateFixture();
        try
        {
            var output = Path.Combine(root, "out") + Path.DirectorySeparatorChar;
            var build = await BuildAsync(root, SharedLayout(output), cancellationToken);
            build.Exit.ShouldBe(0, build.Text);

            // One Lib build, into the shared output; none into the staged helper folder.
            var libBuilds = build.Text.Split('\n').Select(line => line.Trim())
                .Where(line => line.StartsWith("Lib -> ", StringComparison.Ordinal)).ToList();
            libBuilds.ShouldBe([$"Lib -> {Path.Combine(output, "Lib.dll")}"], build.Text);

            var staged = Path.Combine(output, "helper");
            foreach (var name in new[] { "Helper.dll", "Helper.runtimeconfig.json", "Helper.deps.json", "Lib.dll" })
                File.Exists(Path.Combine(staged, name)).ShouldBeTrue(name);
            BuildOutputNesting.Find(output, ["helper"]).ShouldBeEmpty();

            var run = await RunAsync(root, "dotnet", [Path.Combine(staged, "Helper.dll")], cancellationToken);
            run.Exit.ShouldBe(0, run.Text);
            run.Text.Trim().ShouldBe("1841");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Timeout(180_000)]
    public async Task C1841_Shared_output_stages_the_files_its_own_output_stages(CancellationToken cancellationToken)
    {
        var sharedRoot = CreateFixture();
        var nativeRoot = CreateFixture();
        try
        {
            var output = Path.Combine(sharedRoot, "out") + Path.DirectorySeparatorChar;
            var shared = await BuildAsync(sharedRoot, SharedLayout(output), cancellationToken);
            shared.Exit.ShouldBe(0, shared.Text);
            var native = await BuildAsync(nativeRoot, [], cancellationToken);
            native.Exit.ShouldBe(0, native.Text);

            var nativeStaged = Path.Combine(nativeRoot, "Consumer", "bin", "Debug", "net9.0", "helper");
            Inventory(Path.Combine(output, "helper")).ShouldBe(Inventory(nativeStaged));
        }
        finally
        {
            Directory.Delete(sharedRoot, recursive: true);
            Directory.Delete(nativeRoot, recursive: true);
        }
    }

    // The checkpoint tool's layout (BuildConfinement): one absolute OutputPath and artifacts below it.
    private static string[] SharedLayout(string output) =>
    [
        "--property:OutputPath=" + output,
        "--property:ArtifactsPath=" + Path.Combine(output, "artifacts"),
        "--property:UseArtifactsOutput=true",
    ];

    private static List<string> Inventory(string dir)
    {
        Directory.Exists(dir).ShouldBeTrue(dir);
        return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(dir, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal).ToList();
    }

    private static string CreateFixture()
    {
        var root = Directory.CreateTempSubdirectory("c1841-staged-").FullName;
        // Keep any Directory.Build.* above the temp root out of the fixture.
        File.WriteAllText(Path.Combine(root, "Directory.Build.props"), "<Project />");
        File.WriteAllText(Path.Combine(root, "Directory.Build.targets"), "<Project />");
        Write(root, "Lib/Lib.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        Write(root, "Lib/Value.cs", "namespace Lib; public static class Value { public static int Card => 1841; }");
        Write(root, "Helper/Helper.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup>
              <ItemGroup><ProjectReference Include="../Lib/Lib.csproj" /></ItemGroup>
            </Project>
            """);
        Write(root, "Helper/Program.cs", "System.Console.WriteLine(Lib.Value.Card);");
        Write(root, "Consumer/Consumer.csproj", $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
              <ItemGroup><ProjectReference Include="../Helper/Helper.csproj" ReferenceOutputAssembly="false" /></ItemGroup>
              <ItemGroup><StagedHelper Include="$(MSBuildThisFileDirectory)../Helper/Helper.csproj" Folder="helper" Glob="**\*" /></ItemGroup>
            {RealCopyStagedHelpersTarget()}
            </Project>
            """);
        return root;
    }

    // The target text from Antiphon.Tests.csproj itself, so the fixture builds what the tests build.
    private static string RealCopyStagedHelpersTarget()
    {
        var project = XDocument.Load(Path.Combine(FindRepoRoot(), "tests", "Antiphon.Tests", "Antiphon.Tests.csproj"));
        return project.Descendants("Target")
            .Single(target => (string?)target.Attribute("Name") == "CopyStagedHelpers")
            .ToString();
    }

    private static void Write(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static Task<(int Exit, string Text)> BuildAsync(string root, string[] layout, CancellationToken cancellationToken) =>
        RunAsync(root, "dotnet",
        [
            "build", "Consumer/Consumer.csproj", "--nologo", "-nodeReuse:false", "-tl:off", "-v:minimal",
            "--property:UseAppHost=false", .. layout,
        ], cancellationToken);

    private static async Task<(int Exit, string Text)> RunAsync(string root, string file, string[] args, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(file)
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
        using var process = Process.Start(start) ?? throw new InvalidOperationException(file + " did not start");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return (process.ExitCode, await stdout + await stderr);
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "tests", "Antiphon.Tests", "Antiphon.Tests.csproj")))
                return dir.FullName;
        }

        throw new InvalidOperationException("repository root not found above " + AppContext.BaseDirectory);
    }
}
