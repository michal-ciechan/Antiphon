using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Application;

/// <summary>
/// LandAuditTool (it replaced scripts/lib/land-audit-tool.ps1, unit 11 of 2026-11-22-remaining-ps1-to-csharp.md): the
/// default land-graph audit tool is rebuilt when it is missing, has no stamp, or its sources changed; it is reused when
/// current; a failed rebuild refuses and keeps the old tool; an explicit tool path is used as given; a checkout without
/// the tool's project keeps its DLL; one rebuild at a time under the lock. A recording launcher plays dotnet build, so no
/// real build runs; the files are real.
/// </summary>
[Category("Unit")]
public sealed class LandAuditToolResolverTests
{
    private const string Dll = "tools/Antiphon.LandGraphAudit/bin-land-audit/Release/net10.0/Antiphon.LandGraphAudit.dll";
    private const string Stamp = "tools/Antiphon.LandGraphAudit/bin-land-audit/Release/net10.0/land-audit.source-hash";
    private const string Lock = "tools/Antiphon.LandGraphAudit/bin-land-audit/rebuild.lock";

    [Test]
    public void RebuildsOnlyWhenMissingStampedStaleOrChanged()
    {
        using var fixture = NightlyFixture.Create("antiphon-land-tool");
        var root = fixture.Root;
        WriteProject(root);
        var builds = new Builds();

        var first = Resolve(root, null, builds);
        first.Text.ShouldContain("PATH=" + Full(root, Dll), Case.Sensitive, first.Text);
        first.Text.ShouldContain("land audit tool: rebuilding (missing)", Case.Sensitive, first.Text);
        File.ReadAllText(Full(root, Dll)).ShouldBe("built 1");
        File.ReadAllText(Full(root, Stamp)).ShouldBe(LandAuditTool.SourceHash(root, new FileSystem()));

        var current = Resolve(root, null, builds);
        current.Text.ShouldContain("PATH=" + Full(root, Dll), Case.Sensitive, current.Text);
        current.Text.ShouldNotContain("rebuilding", Case.Sensitive, current.Text);
        current.Launches.ShouldBeEmpty();

        // A build output in bin/obj does not count as a source.
        Write(root, "tools/Antiphon.LandGraphAudit/obj/Generated.cs", "class G { }");
        Resolve(root, null, builds).Text.ShouldNotContain("rebuilding", Case.Sensitive);

        Write(root, "tools/Antiphon.LandGraphAudit.Core/Core.cs", "class C { int x; }");
        var changed = Resolve(root, null, builds);
        changed.Text.ShouldContain("land audit tool: rebuilding (sources changed)", Case.Sensitive, changed.Text);
        File.ReadAllText(Full(root, Dll)).ShouldBe("built 2");

        // A hand-built DLL without a stamp is rebuilt once.
        File.Delete(Full(root, Stamp));
        Resolve(root, null, builds).Text.ShouldContain("rebuilding (sources changed)", Case.Sensitive);
        File.ReadAllText(Full(root, Dll)).ShouldBe("built 3");

        // A failed rebuild refuses with the evaluator-missing text and the build's error lines, and leaves the old tool.
        Write(root, "tools/Antiphon.LandGraphAudit/Program.cs", "class P { int y; }");
        builds.Fail = true;
        var failed = Resolve(root, null, builds);
        failed.Text.ShouldContain("ERROR=built evaluator missing: " + Full(root, Dll) + " (rebuild failed, exit 1): error CS0000: stand-in build failed | Build FAILED with 1 Error",
            Case.Sensitive, failed.Text);
        failed.Text.ShouldNotContain("PATH=", Case.Sensitive);
        File.ReadAllText(Full(root, Dll)).ShouldBe("built 3");
        Directory.GetDirectories(Full(root, "tools/Antiphon.LandGraphAudit/bin-land-audit"), "build-*").ShouldBeEmpty("the work folder is removed");

        // An explicit tool path is used as given, never rebuilt.
        var explicitTool = Path.Combine(root, "elsewhere.dll");
        File.WriteAllText(explicitTool, "x");
        var given = Resolve(root, explicitTool, builds);
        given.Text.ShouldContain("PATH=" + Path.GetFullPath(explicitTool), Case.Sensitive, given.Text);
        given.Text.ShouldNotContain("rebuilding", Case.Sensitive);
        given.Launches.ShouldBeEmpty();
        Resolve(root, Path.Combine(root, "absent.dll"), builds).Text
            .ShouldContain("ERROR=built evaluator missing: " + Path.Combine(root, "absent.dll"), Case.Sensitive);

        // A checkout without the tool's project keeps whatever DLL it has, and refuses without one.
        Directory.Delete(Path.Combine(root, "tools", "Antiphon.LandGraphAudit"), recursive: true);
        Write(root, Dll, "copied");
        Resolve(root, null, builds).Text.ShouldContain("PATH=" + Full(root, Dll), Case.Sensitive);
        File.Delete(Full(root, Dll));
        Resolve(root, null, builds).Text.ShouldContain("ERROR=built evaluator missing: " + Full(root, Dll), Case.Sensitive);
        builds.Count.ShouldBe(3);
    }

    [Test]
    public void RebuildRunsOneQuietReleaseBuildIntoAFreshFolderInTheCheckout()
    {
        using var fixture = NightlyFixture.Create("antiphon-land-tool");
        var root = fixture.Root;
        WriteProject(root);
        Write(root, Dll, "hand built");
        var builds = new Builds();

        var rebuilt = Resolve(root, null, builds, elapsed: TimeSpan.FromSeconds(10.5));
        rebuilt.Text.ShouldContain("land audit tool: rebuilding (sources changed)", Case.Sensitive, rebuilt.Text);
        rebuilt.Text.ShouldContain("land audit tool: rebuilt in 10 s", Case.Sensitive, rebuilt.Text);
        var launch = rebuilt.Launches.ShouldHaveSingleItem();
        launch.FileName.ShouldBe("dotnet");
        launch.WorkingDirectory.ShouldBe(Path.GetFullPath(root));
        launch.CaptureOutput.ShouldBeTrue();
        launch.Custody.ShouldBe(NightlyCustody.Watched);
        launch.Environment.ShouldBeNull("the build inherits this process's environment");
        var arguments = launch.Arguments.ToArray();
        var work = Path.GetDirectoryName(arguments[5])!;
        Path.GetFileName(work).ShouldStartWith("build-");
        Path.GetDirectoryName(work).ShouldBe(Full(root, "tools/Antiphon.LandGraphAudit/bin-land-audit"));
        arguments.ShouldBe(new[]
        {
            "build", Full(root, "tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj"), "-c", "Release", "-o", Path.Combine(work, "out"),
            "--artifacts-path", Path.Combine(work, "artifacts"), "-nologo", "-v", "q", "-m:2", "-nodeReuse:false", "-p:UseSharedCompilation=false",
        });
        Directory.Exists(work).ShouldBeFalse("the work folder is removed after the swap");
        Directory.GetDirectories(Full(root, "tools/Antiphon.LandGraphAudit/bin-land-audit"), "old-*").ShouldBeEmpty("the replaced folder is removed");
        File.ReadAllText(Full(root, Dll)).ShouldBe("built 1");
    }

    [Test]
    public void NoRebuildAnswersStaleAndChangesNothing()
    {
        using var fixture = NightlyFixture.Create("antiphon-land-tool");
        var root = fixture.Root;
        WriteProject(root);
        var builds = new Builds();

        var stale = Resolve(root, null, builds, noRebuild: true);
        stale.Text.ShouldStartWith("REBUILD", Case.Sensitive, stale.Text);
        stale.Launches.ShouldBeEmpty();
        Directory.Exists(Full(root, "tools/Antiphon.LandGraphAudit/bin-land-audit")).ShouldBeFalse();

        Resolve(root, null, builds);
        Resolve(root, null, builds, noRebuild: true).Text.ShouldStartWith("PATH=" + Full(root, Dll), Case.Sensitive);
        Resolve(root, Path.Combine(root, "absent.dll"), builds, noRebuild: true).Text.ShouldStartWith("ERROR=built evaluator missing:", Case.Sensitive);
    }

    [Test]
    public void AHeldLockWaitsFifteenMinutesThenRefuses()
    {
        using var fixture = NightlyFixture.Create("antiphon-land-tool");
        var root = fixture.Root;
        WriteProject(root);
        Directory.CreateDirectory(Path.GetDirectoryName(Full(root, Lock))!);
        var builds = new Builds();
        using (new FileStream(Full(root, Lock), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var waited = Resolve(root, null, builds, sleep: (clock, span) =>
            {
                span.ShouldBe(LandAuditTool.LockPoll);
                clock.Advance(TimeSpan.FromMinutes(1));
            });
            waited.Text.ShouldContain("ERROR=built evaluator missing: " + Full(root, Dll) + " (rebuild lock timed out)", Case.Sensitive, waited.Text);
            waited.Launches.ShouldBeEmpty();
            waited.Sleeps.ShouldBe(16);
        }
    }

    [Test]
    public void AToolRebuiltWhileWaitingForTheLockIsUsedWithoutBuilding()
    {
        using var fixture = NightlyFixture.Create("antiphon-land-tool");
        var root = fixture.Root;
        WriteProject(root);
        Directory.CreateDirectory(Path.GetDirectoryName(Full(root, Lock))!);
        var builds = new Builds();
        var held = new FileStream(Full(root, Lock), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var waited = Resolve(root, null, builds, sleep: (_, _) =>
        {
            // The other process finishes its rebuild and lets go of the lock.
            Write(root, Dll, "built elsewhere");
            Write(root, Stamp, LandAuditTool.SourceHash(root, new FileSystem()));
            held.Dispose();
        });
        held.Dispose();
        waited.Text.ShouldContain("PATH=" + Full(root, Dll), Case.Sensitive, waited.Text);
        waited.Text.ShouldNotContain("rebuilding", Case.Sensitive);
        waited.Launches.ShouldBeEmpty();
        File.ReadAllText(Full(root, Dll)).ShouldBe("built elsewhere");
    }

    [Test]
    public void AToolInUseCannotBeReplacedAndRefuses()
    {
        if (!OperatingSystem.IsWindows())
            throw new SkipTestException("Only Windows refuses to move a folder that holds an open file.");
        using var fixture = NightlyFixture.Create("antiphon-land-tool");
        var root = fixture.Root;
        WriteProject(root);
        Write(root, Dll, "running");
        var builds = new Builds();
        using (new FileStream(Full(root, Dll), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var refused = Resolve(root, null, builds);
            refused.Text.ShouldContain("ERROR=built evaluator missing: " + Full(root, Dll) + " (the built tool is in use and cannot be replaced: ", Case.Sensitive, refused.Text);
        }
        File.ReadAllText(Full(root, Dll)).ShouldBe("running");
        Directory.GetDirectories(Full(root, "tools/Antiphon.LandGraphAudit/bin-land-audit"), "build-*").ShouldBeEmpty();
    }

    [Test]
    public void SourceHashIsTheSortedSourcesAndSharedBuildFiles()
    {
        using var fixture = NightlyFixture.Create("antiphon-land-tool");
        var root = fixture.Root;
        WriteProject(root);
        Write(root, "tools/Antiphon.LandGraphAudit/Upper.CS", "class U { }");
        Write(root, "tools/Antiphon.LandGraphAudit/notes.md", "not a source");
        Write(root, "tools/Antiphon.LandGraphAudit/bin/Release/Built.cs", "output");
        Write(root, "tools/Antiphon.LandGraphAudit.Core/OBJ/Generated.cs", "output");
        Write(root, "tools/Antiphon.LandGraphAudit/bin-land-audit/build-1/artifacts/Generated.cs", "output");
        Write(root, "global.json", "{ }");
        Write(root, "tools/Directory.Build.props", "<Project />");
        Write(root, "Other/Other.cs", "not the tool");

        var expected = new MemoryStream();
        foreach (var relative in new[]
                 {
                     "global.json",
                     "tools/Antiphon.LandGraphAudit.Core/Core.cs",
                     "tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj",
                     "tools/Antiphon.LandGraphAudit/Program.cs",
                     "tools/Antiphon.LandGraphAudit/Upper.CS",
                     "tools/Directory.Build.props",
                 })
        {
            expected.Write(Encoding.UTF8.GetBytes(relative + "\n"));
            expected.Write(File.ReadAllBytes(Full(root, relative)));
        }
        LandAuditTool.SourceHash(root, new FileSystem()).ShouldBe(Convert.ToHexString(SHA256.HashData(expected.ToArray())));
        LandAuditTool.SourceHash(root + Path.DirectorySeparatorChar, new FileSystem()).ShouldBe(LandAuditTool.SourceHash(root, new FileSystem()));
    }

    [Test]
    public void ChildrenGetTheCallersEnvironmentWithoutMsBuildNames()
    {
        LandAuditTool.WithoutMsBuild(null).ShouldBeNull();
        var child = LandAuditTool.WithoutMsBuild(new Dictionary<string, string>
        {
            ["PATH"] = "p",
            ["MSBuildExtensionsPath"] = "x",
            ["MSBUILD_EXE_PATH"] = "y",
            ["msbuildsdkspath"] = "z",
            ["NUGET_PACKAGES"] = "n",
        });
        child.Keys.Order(StringComparer.Ordinal).ShouldBe(new[] { "NUGET_PACKAGES", "PATH" });
    }

    private sealed class Builds
    {
        public int Count { get; set; }

        public bool Fail { get; set; }
    }

    private sealed record Resolution(string Text, IReadOnlyList<NightlyLaunch> Launches, int Sleeps);

    private static string Full(string root, string relative) => Path.GetFullPath(Path.Combine(root, relative));

    private static void Write(string root, string relative, string text)
    {
        var path = Full(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static void WriteProject(string root)
    {
        Write(root, "tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj", "<Project />");
        Write(root, "tools/Antiphon.LandGraphAudit/Program.cs", "class P { }");
        Write(root, "tools/Antiphon.LandGraphAudit.Core/Core.cs", "class C { }");
    }

    // LandAuditTool.Resolve on a host whose launcher plays dotnet build: it writes the DLL into the -o folder, numbering
    // builds, or fails with build output. elapsed is the clock's advance during the build; sleep stands in for the lock
    // wait. The text is PATH=, ERROR= or REBUILD, then the host's error lines.
    private static Resolution Resolve(string root, string? toolPath, Builds builds, bool noRebuild = false, TimeSpan? elapsed = null,
        Action<FakeTimeProvider, TimeSpan>? sleep = null)
    {
        var clock = new FakeTimeProvider(NightlyFixture.DefaultNow);
        var launcher = new RecordingLauncher(launch =>
        {
            launch.FileName.ShouldBe("dotnet");
            var arguments = launch.Arguments.ToArray();
            var output = arguments[Array.IndexOf(arguments, "-o") + 1];
            clock.Advance(elapsed ?? TimeSpan.FromSeconds(1));
            if (builds.Fail)
            {
                return new NightlyLaunchResult
                {
                    ExitCode = 1,
                    LogText = "  Determining projects to restore...\nerror CS0000: stand-in build failed\nBuild FAILED with 1 Error\n",
                };
            }
            builds.Count++;
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, LandAuditTool.DllName), "built " + builds.Count);
            return new NightlyLaunchResult { ExitCode = 0 };
        });
        var error = new StringWriter();
        var sleeps = 0;
        var host = new NightlyHost
        {
            Clock = clock,
            Git = new FakeGit().Run,
            Launcher = launcher,
            Processes = new FakeProcessTable(),
            Probe = new FakeHostProbe(),
            Out = TextWriter.Null,
            Error = error,
            Trace = TextWriter.Null,
            Sleep = span =>
            {
                sleeps++;
                if (sleep is null) throw new InvalidOperationException("unexpected lock wait");
                sleep(clock, span);
            },
        };
        var resolved = LandAuditTool.Resolve(root, toolPath, noRebuild, host, new FileSystem());
        var answer = resolved.Path is not null ? "PATH=" + resolved.Path : resolved.Error is not null ? "ERROR=" + resolved.Error : resolved.Rebuild ? "REBUILD" : "NONE";
        return new Resolution(answer + "\n" + error, launcher.Launches, sleeps);
    }
}
