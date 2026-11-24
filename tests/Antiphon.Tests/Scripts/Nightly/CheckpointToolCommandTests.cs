using System.IO.Abstractions;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// CheckpointTool, the checkpoints command (unit 20 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md): the
/// checkpoint tool built from a checkout, once per source hash, into bin-launcher/antiphon-checkpoints/&lt;hash&gt;/, and run
/// with dotnet exec and the arguments as given. A recording launcher plays dotnet, so no real build runs; the files are real.
/// </summary>
[Category("Unit")]
public sealed class CheckpointToolCommandTests
{
    private const string Project = "tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj";

    private sealed class Builds
    {
        public int Count { get; set; }
        public bool Fail { get; set; }
        public int ToolExit { get; set; }
    }

    private static void Write(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static void WriteTool(string root)
    {
        Write(root, Project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <Compile Include="../../scripts/lib/Linked.cs" Link="Linked.cs" />
                <ProjectReference Include="..\..\src\Lib\Lib.csproj" />
              </ItemGroup>
            </Project>
            """);
        Write(root, "tools/Antiphon.Checkpoints/Program.cs", "class P { }");
        Write(root, "tools/Antiphon.Checkpoints/obj/Generated.cs", "class G { }");
        Write(root, "tools/Antiphon.Checkpoints/bin-c1/Out.cs", "class O { }");
        Write(root, "src/Lib/Lib.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><Compile Include=\"../Shared/Shared.cs\" /></ItemGroup></Project>");
        Write(root, "src/Lib/Lib.cs", "class L { }");
        Write(root, "src/Shared/Shared.cs", "class S { }");
        Write(root, "src/Other/Other.cs", "class Unrelated { }");
        Write(root, "scripts/lib/Linked.cs", "class K { }");
        Write(root, "scripts/lib/NotLinked.cs", "class N { }");
        Write(root, "Directory.Build.props", "<Project />");
    }

    private sealed record Outcome(int Exit, string Error, IReadOnlyList<NightlyLaunch> Launches);

    private static Outcome Run(string root, Builds builds, string[] arguments, IReadOnlyDictionary<string, string>? environment = null)
    {
        var clock = new FakeTimeProvider(NightlyFixture.DefaultNow);
        var launcher = new RecordingLauncher(launch =>
        {
            launch.FileName.ShouldBe("dotnet");
            var argv = launch.Arguments.ToArray();
            if (argv[0] == "exec")
                return new NightlyLaunchResult { ExitCode = builds.ToolExit };
            clock.Advance(TimeSpan.FromSeconds(2));
            if (builds.Fail)
                return new NightlyLaunchResult { ExitCode = 1, LogText = "restore\nerror CS1002: ; expected\nBuild FAILED.\n" };
            builds.Count++;
            var output = argv[Array.IndexOf(argv, "-o") + 1];
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, CheckpointTool.DllName), "built " + builds.Count);
            return new NightlyLaunchResult { ExitCode = 0, LogText = "Build succeeded.\n" };
        });
        var error = new StringWriter();
        var host = new NightlyHost
        {
            Clock = clock, Git = new FakeGit().Run, Launcher = launcher, Processes = new FakeProcessTable(), Probe = new FakeHostProbe(),
            Out = TextWriter.Null, Error = error, Trace = TextWriter.Null,
            Sleep = _ => throw new InvalidOperationException("unexpected lock wait"),
        };
        var options = CheckpointToolOptions.Parse(arguments, root, Path.Combine(root, "caller"), environment);
        return new Outcome(CheckpointTool.Run(options, host, new FileSystem()), error.ToString(), launcher.Launches);
    }

    [Test]
    public void The_sources_are_the_project_graph_and_its_links_and_nothing_built()
    {
        using var fixture = NightlyFixture.Create("checkpoint-tool-sources");
        WriteTool(fixture.Root);
        CheckpointTool.SourceFiles(fixture.Root, new FileSystem()).ShouldBe(new[]
        {
            "Directory.Build.props", "scripts/lib/Linked.cs", "src/Lib/Lib.cs", "src/Lib/Lib.csproj", "src/Shared/Shared.cs",
            "tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj", "tools/Antiphon.Checkpoints/Program.cs",
        });
        var hash = CheckpointTool.SourceHash(fixture.Root, new FileSystem());
        Write(fixture.Root, "tools/Antiphon.Checkpoints/obj/Generated.cs", "class G2 { }");
        Write(fixture.Root, "scripts/lib/NotLinked.cs", "class N2 { }");
        CheckpointTool.SourceHash(fixture.Root, new FileSystem()).ShouldBe(hash, "build output and unrelated files are not sources");
        Write(fixture.Root, "src/Shared/Shared.cs", "class S2 { }");
        CheckpointTool.SourceHash(fixture.Root, new FileSystem()).ShouldNotBe(hash, "a file a referenced project links is a source");
    }

    // The real tool's graph: its own folder, HostCleanup and GitInProcess, and the files they link from scripts/lib and src.
    [Test]
    public void The_real_tool_sources_include_every_linked_file()
    {
        var sources = CheckpointTool.SourceFiles(DelegateScriptRunner.RepoRoot, new FileSystem());
        foreach (var expected in new[]
        {
            "tools/Antiphon.Checkpoints/Program.cs", "tools/Antiphon.Checkpoints/Execution/RowCommand.cs",
            "src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj", "src/Antiphon.GitInProcess/Antiphon.GitInProcess.csproj",
            "scripts/lib/CheckpointProcessRunner.cs", "scripts/lib/BuildDriverEnvironment.cs", "scripts/lib/LeaseTempDirectory.cs",
            "scripts/lib/PhaseJournalDiagnostic.cs", "src/Antiphon.Hosting/PwshPrivateCache.cs",
        })
            sources.ShouldContain(expected);
        sources.ShouldNotContain(path => path.Contains("/bin/", StringComparison.Ordinal) || path.Contains("/obj/", StringComparison.Ordinal));
    }

    [Test]
    public void The_tool_is_built_once_per_source_hash_and_run_with_the_arguments_as_given()
    {
        using var fixture = NightlyFixture.Create("checkpoint-tool-run");
        WriteTool(fixture.Root);
        var builds = new Builds { ToolExit = 3 };
        var first = Run(fixture.Root, builds, ["row", "--name", "CP-1", "--filter", "/*/*/A*/*", ""],
            new Dictionary<string, string> { ["PATH"] = "p", ["MSBuildExtensionsPath"] = "x", ["MSBUILD_EXE_PATH"] = "y" });
        first.Exit.ShouldBe(3, first.Error);
        first.Error.ShouldContain("checkpoints: building the checkpoint tool (");
        var hash = CheckpointTool.SourceHash(fixture.Root, new FileSystem());
        var folder = Path.Combine(fixture.Root, "bin-launcher", "antiphon-checkpoints", hash[..24]);
        File.ReadAllText(Path.Combine(folder, CheckpointTool.StampName)).ShouldBe(hash);
        first.Launches.Count.ShouldBe(2);
        var build = first.Launches[0];
        build.Arguments.ShouldBe(new[]
        {
            "build", Path.Combine(fixture.Root, Project.Replace('/', Path.DirectorySeparatorChar)), "-c", "Release",
            "-o", build.Arguments[5], "--artifacts-path", build.Arguments[7], "-nologo", "-v", "q", "-m:2", "-nodeReuse:false",
            "-p:UseSharedCompilation=false",
        });
        build.WorkingDirectory.ShouldBe(fixture.Root);
        build.CaptureOutput.ShouldBeTrue("the build log goes to build.log, not the console");
        build.Environment.ShouldNotBeNull().Keys.ShouldBe(new[] { "PATH" }, "the caller's MSBuild* names stay out of the build");
        File.ReadAllText(Path.Combine(fixture.Root, "bin-launcher", "antiphon-checkpoints", "build.log")).ShouldContain("Build succeeded.");
        var exec = first.Launches[1];
        exec.Arguments.ShouldBe(new[] { "exec", Path.Combine(folder, CheckpointTool.DllName), "row", "--name", "CP-1", "--filter", "/*/*/A*/*", "" });
        exec.WorkingDirectory.ShouldBe(Path.Combine(fixture.Root, "caller"));
        exec.Environment.ShouldBeNull("the tool inherits the caller's environment");

        var again = Run(fixture.Root, builds, ["validate", "--evidence", "r.json"]);
        again.Launches.Single().Arguments[0].ShouldBe("exec", "a current build is reused");
        builds.Count.ShouldBe(1);

        Write(fixture.Root, "tools/Antiphon.Checkpoints/Program.cs", "class P2 { }");
        var changed = Run(fixture.Root, builds, ["row"]);
        changed.Launches.Count.ShouldBe(2, "a source change builds a new folder");
        builds.Count.ShouldBe(2);
        Directory.Exists(folder).ShouldBeTrue("the earlier build stays for a row still running it");
        Directory.GetDirectories(Path.Combine(fixture.Root, "bin-launcher", "antiphon-checkpoints"), "tmp-*").ShouldBeEmpty();
    }

    [Test]
    public void A_tool_that_does_not_build_is_exit_9_with_one_line()
    {
        using var fixture = NightlyFixture.Create("checkpoint-tool-fail");
        WriteTool(fixture.Root);
        var failed = Run(fixture.Root, new Builds { Fail = true }, ["row", "--name", "CP-1"]);
        failed.Exit.ShouldBe(CheckpointTool.Unavailable);
        failed.Error.ShouldContain("checkpoints: the checkpoint tool did not build (exit 1, log ");
        failed.Error.ShouldContain("error CS1002: ; expected");
        failed.Launches.Count.ShouldBe(1, "nothing runs after a failed build");

        using var empty = NightlyFixture.Create("checkpoint-tool-missing");
        var missing = Run(empty.Root, new Builds(), ["row"]);
        missing.Exit.ShouldBe(CheckpointTool.Unavailable);
        missing.Error.ShouldContain("checkpoints: missing ");
        Should.Throw<NightlyUsageException>(() => CheckpointToolOptions.Parse([], empty.Root, empty.Root, null));
    }
}
