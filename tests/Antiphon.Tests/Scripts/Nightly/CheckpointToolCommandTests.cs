using System.IO.Abstractions;
using Antiphon.CliLauncher;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// CheckpointTool, the checkpoints command (unit 20 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md): the
/// checkpoint tool built from a checkout and run with dotnet exec and the arguments as given. Since unit 59 the tool is
/// published as the cached launcher publishes the CLI (CheckpointTool.Product): its inputs by CliInputs' rules from its own
/// project, one folder per identity under bin-launcher/antiphon-checkpoints/ with the pointer, the shared incremental
/// build and the lease its run holds. A recording launcher plays dotnet, so no real build runs; the files are real.
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
        public Action<NightlyLaunch>? OnExec { get; set; }
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

    private static string Cache(string root) => Path.Combine(root, "bin-launcher", "antiphon-checkpoints");

    private static Outcome Run(string root, Builds builds, string[] arguments, IReadOnlyDictionary<string, string>? environment = null)
    {
        var clock = new FakeTimeProvider(NightlyFixture.DefaultNow);
        var launcher = new RecordingLauncher(launch =>
        {
            launch.FileName.ShouldBe("dotnet");
            var argv = launch.Arguments.ToArray();
            if (argv[0] == "exec")
            {
                builds.OnExec?.Invoke(launch);
                return new NightlyLaunchResult { ExitCode = builds.ToolExit };
            }
            clock.Advance(TimeSpan.FromSeconds(2));
            if (builds.Fail)
                return new NightlyLaunchResult { ExitCode = 1, LogText = "restore\nerror CS1002: ; expected\nBuild FAILED.\n" };
            builds.Count++;
            var output = Path.Combine(argv[Array.IndexOf(argv, "--artifacts-path") + 1], "bin", "Antiphon.Checkpoints", "release");
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

    private static string Identity(string root) => CliIdentity.Compute(CheckpointTool.Inputs(root), null, NightlyFixture.DefaultNow.UtcDateTime, CheckpointTool.Product.Recipe).Identity;

    [Test]
    public void The_inputs_are_the_project_graph_and_its_links_and_nothing_built()
    {
        using var fixture = NightlyFixture.Create("checkpoint-tool-sources");
        WriteTool(fixture.Root);
        CheckpointTool.Inputs(fixture.Root).Select(input => input.Name).ShouldBe(new[]
        {
            "Directory.Build.props", "scripts/lib/Linked.cs", "src/Lib/Lib.cs", "src/Lib/Lib.csproj", "src/Shared/Shared.cs",
            "tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj", "tools/Antiphon.Checkpoints/Program.cs",
        });
        var identity = Identity(fixture.Root);
        Write(fixture.Root, "tools/Antiphon.Checkpoints/obj/Generated.cs", "class G2 { }");
        Write(fixture.Root, "scripts/lib/NotLinked.cs", "class N2 { }");
        Identity(fixture.Root).ShouldBe(identity, "build output and unrelated files are not inputs");
        Write(fixture.Root, "src/Shared/Shared.cs", "class S2 { }");
        Identity(fixture.Root).ShouldNotBe(identity, "a file a referenced project links is an input");
        CheckpointTool.Product.Recipe.ShouldNotBe(CliProduct.Cli.Recipe, "the tool's identity is never the CLI's");
        CheckpointTool.Product.Recipe.ShouldContain("copy bin/Antiphon.Checkpoints/release");
    }

    // The real tool's graph: its own folder, HostCleanup and GitInProcess, and the files they link from scripts/lib and src.
    [Test]
    public void The_real_tool_inputs_include_every_linked_file()
    {
        var names = CheckpointTool.Inputs(DelegateScriptRunner.RepoRoot).Select(input => input.Name).ToArray();
        foreach (var expected in new[]
        {
            "tools/Antiphon.Checkpoints/Program.cs", "tools/Antiphon.Checkpoints/Execution/RowCommand.cs",
            "src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj", "src/Antiphon.GitInProcess/Antiphon.GitInProcess.csproj",
            "scripts/lib/CheckpointProcessRunner.cs", "scripts/lib/BuildDriverEnvironment.cs", "scripts/lib/LeaseTempDirectory.cs",
            "scripts/lib/PhaseJournalDiagnostic.cs", "src/Antiphon.Hosting/PwshPrivateCache.cs", "Directory.Build.props", "global.json",
        })
            names.ShouldContain(expected);
        names.ShouldNotContain(path => path.Contains("/bin/", StringComparison.Ordinal) || path.Contains("/obj/", StringComparison.Ordinal)
            || path.StartsWith("bin-launcher/", StringComparison.Ordinal));
    }

    [Test]
    public void The_tool_is_published_once_per_identity_and_run_with_the_arguments_as_given()
    {
        using var fixture = NightlyFixture.Create("checkpoint-tool-run");
        WriteTool(fixture.Root);
        var builds = new Builds { ToolExit = 3 };
        var first = Run(fixture.Root, builds, ["row", "--name", "CP-1", "--filter", "/*/*/(A*)|(B*)/*", ""],
            new Dictionary<string, string> { ["PATH"] = "p", ["MSBuildExtensionsPath"] = "x", ["MSBUILD_EXE_PATH"] = "y" });
        first.Exit.ShouldBe(3, first.Error);
        first.Error.ShouldContain("checkpoints: building the checkpoint tool");
        first.Error.ShouldContain("checkpoints: built in 2 s");
        var identity = Identity(fixture.Root);
        var folder = Path.Combine(Cache(fixture.Root), identity[..24]);
        CliCache.Stamp(folder).ShouldNotBeNull().Identity.ShouldBe(identity);
        new CliCache(fixture.Root, CheckpointTool.Product).ReadPointer().ShouldBe(identity[..24]);
        first.Launches.Count.ShouldBe(2);
        var build = first.Launches[0];
        build.Arguments.ShouldBe(new[]
        {
            "build", Path.Combine(fixture.Root, Project.Replace('/', Path.DirectorySeparatorChar)), "-c", "Release",
            "--artifacts-path", Path.Combine(Cache(fixture.Root), "build"), "-nologo", "-v", "q", "-m:2", "-nodeReuse:false",
            "-p:UseSharedCompilation=false",
        });
        build.WorkingDirectory.ShouldBe(fixture.Root);
        build.CaptureOutput.ShouldBeTrue("the build log goes to build.log, not the console");
        build.Environment.ShouldNotBeNull().Keys.ShouldBe(new[] { "PATH" }, "the caller's MSBuild* names stay out of the build");
        File.ReadAllText(Path.Combine(Cache(fixture.Root), "build.log")).ShouldContain("Build succeeded.");
        var exec = first.Launches[1];
        exec.Arguments.ShouldBe(new[] { "exec", Path.Combine(folder, CheckpointTool.DllName), "row", "--name", "CP-1", "--filter", "/*/*/(A*)|(B*)/*", "" });
        exec.WorkingDirectory.ShouldBe(Path.Combine(fixture.Root, "caller"));
        exec.Environment.ShouldBeNull("the tool inherits the caller's environment");

        var again = Run(fixture.Root, builds, ["validate", "--evidence", "r.json"]);
        again.Launches.Single().Arguments[0].ShouldBe("exec", "a current build is reused");
        again.Error.ShouldBeEmpty();
        builds.Count.ShouldBe(1);

        // A running row holds its folder's lease: the next publish keeps it.
        Write(fixture.Root, "tools/Antiphon.Checkpoints/Program.cs", "class P2 { }");
        var inUse = new List<bool>();
        builds.OnExec = launch => inUse.Add(CliCache.InUse(Path.GetDirectoryName(launch.Arguments[1])!));
        var changed = Run(fixture.Root, builds, ["row"]);
        changed.Launches.Count.ShouldBe(2, "an input change publishes a new folder");
        builds.Count.ShouldBe(2);
        inUse.ShouldBe([true], "the command holds the lease of the folder its tool runs from");
        Directory.Exists(folder).ShouldBeTrue("the earlier build stays (kept with the newest)");
        Directory.GetDirectories(Cache(fixture.Root), "tmp-*").ShouldBeEmpty();
        CliCache.InUse(Path.GetDirectoryName(changed.Launches[1].Arguments[1])!).ShouldBeFalse("the lease ends with the tool");
    }

    // The builds before unit 59 (a source-hash folder without a manifest) are not reused, and stay for a week.
    [Test]
    public void An_older_source_hash_build_is_neither_reused_nor_removed_at_once()
    {
        using var fixture = NightlyFixture.Create("checkpoint-tool-legacy");
        WriteTool(fixture.Root);
        var legacy = Path.Combine(Cache(fixture.Root), "abcdefabcdefabcdefabcdef");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, CheckpointTool.DllName), "old");
        File.WriteAllText(Path.Combine(legacy, "source-hash"), new string('a', 64));
        var builds = new Builds();
        var run = Run(fixture.Root, builds, ["row"]);
        run.Exit.ShouldBe(0, run.Error);
        builds.Count.ShouldBe(1);
        Directory.Exists(legacy).ShouldBeTrue("a row may still run from it");
        Directory.SetLastWriteTimeUtc(legacy, NightlyFixture.DefaultNow.UtcDateTime.AddDays(-6));
        Write(fixture.Root, "tools/Antiphon.Checkpoints/Program.cs", "class P2 { }");
        Run(fixture.Root, builds, ["row"]).Exit.ShouldBe(0);
        Directory.Exists(legacy).ShouldBeTrue("six days untouched is not yet a week");
        Directory.SetLastWriteTimeUtc(legacy, NightlyFixture.DefaultNow.UtcDateTime.AddDays(-8));
        Write(fixture.Root, "tools/Antiphon.Checkpoints/Program.cs", "class P3 { }");
        Run(fixture.Root, builds, ["row"]).Exit.ShouldBe(0);
        Directory.Exists(legacy).ShouldBeFalse("untouched for a week, it goes with the next publish");
    }

    [Test]
    public void A_tool_that_does_not_build_is_exit_9_with_one_line()
    {
        using var fixture = NightlyFixture.Create("checkpoint-tool-fail");
        WriteTool(fixture.Root);
        var failed = Run(fixture.Root, new Builds { Fail = true }, ["row", "--name", "CP-1"]);
        failed.Exit.ShouldBe(CheckpointTool.Unavailable);
        failed.Error.ShouldContain("checkpoints: the checkpoint tool did not build (exit 1, log " + Path.Combine(Cache(fixture.Root), "build.log") + "): error CS1002: ; expected");
        failed.Launches.Count.ShouldBe(1, "nothing runs after a failed build");

        using var empty = NightlyFixture.Create("checkpoint-tool-missing");
        var missing = Run(empty.Root, new Builds(), ["row"]);
        missing.Exit.ShouldBe(CheckpointTool.Unavailable);
        missing.Error.ShouldContain("checkpoints: missing ");
        Should.Throw<NightlyUsageException>(() => CheckpointToolOptions.Parse([], empty.Root, empty.Root, null));
    }
}
