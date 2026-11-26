using Antiphon.CliLauncher;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The cached CLI launcher (N0 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, unit 57), in process:
/// the CLI's build inputs and their content identity (CliInputs, CliIdentity), the per-identity cache with its pointer,
/// lease and pruning (CliCache), the locked, atomic publish with its incremental-build guard (CliPublisher) and the check
/// the CLI makes before a command when a launcher asked (CliGate). A fake build plays dotnet; the files are real.
/// </summary>
[Category("Unit")]
public sealed class CliLauncherTests
{
    private const string Scripts = "scripts/lib/Antiphon.Scripts/";
    private const string Nightly = "scripts/lib/Antiphon.Nightly/";

    private static void Write(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    // A checkout shaped like the real one: the CLI project links a file, references a project that references another
    // and a package; build files above them; build output, IDE folders and unrelated files beside them.
    private static NightlyFixture Checkout()
    {
        var fixture = NightlyFixture.Create("cli-launcher");
        var root = fixture.Root;
        Write(root, Scripts + "Antiphon.Scripts.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <!-- <Compile Include="../Commented.cs" /> -->
              <ItemGroup>
                <Compile Include="../Linked.cs" Link="Linked.cs" />
                <ProjectReference Include="../Antiphon.Nightly/Antiphon.Nightly.csproj" />
              </ItemGroup>
            </Project>
            """);
        Write(root, Scripts + "Program.cs", "class P { }");
        Write(root, Scripts + "Sub/bin/Nested.cs", "class N { }");
        Write(root, Scripts + "bin/Release/Out.dll", "out");
        Write(root, Scripts + "obj/Generated.cs", "class G { }");
        Write(root, Scripts + "bin-c1/Out.cs", "class O { }");
        Write(root, Scripts + "workspace/session.log", "log");
        Write(root, Scripts + ".vs/state.json", "{}");
        Write(root, Nightly + "Antiphon.Nightly.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Npgsql" Version="9.0.3" />
                <ProjectReference Include="..\..\..\src\Lib\Lib.csproj" />
              </ItemGroup>
            </Project>
            """);
        Write(root, Nightly + "Core.cs", "class C { }");
        Write(root, "src/Lib/Lib.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        Write(root, "src/Lib/Lib.cs", "class L { }");
        Write(root, "src/Other/Other.cs", "class Unrelated { }");
        Write(root, "scripts/lib/Linked.cs", "class K { }");
        Write(root, "scripts/lib/Commented.cs", "class Commented { }");
        Write(root, "scripts/lib/NotLinked.cs", "class NotLinked { }");
        Write(root, "Directory.Build.props", "<Project><Import Project=\"$(MSBuildThisFileDirectory)build/Shared.targets\" Condition=\"Exists('x')\" /></Project>");
        Write(root, "build/Shared.targets", "<Project />");
        Write(root, "global.json", "{ \"sdk\": { \"version\": \"10.0.204\" } }");
        Write(root, ".editorconfig", "root = true");
        Write(root, "docs/readme.md", "docs");
        Write(root, "bin-launcher/antiphon-scripts/build.log", "log");
        return fixture;
    }

    private static readonly string[] CheckoutInputs =
    [
        ".editorconfig", "Directory.Build.props", "build/Shared.targets", "global.json", Nightly + "Antiphon.Nightly.csproj", Nightly + "Core.cs",
        Scripts + "Antiphon.Scripts.csproj", Scripts + "Program.cs", Scripts + "Sub/bin/Nested.cs", "scripts/lib/Linked.cs", "src/Lib/Lib.cs",
        "src/Lib/Lib.csproj",
    ];

    private static string Identity(string root) => CliIdentity.Compute(CliInputs.Discover(root), null, DateTime.UtcNow).Identity;

    private static void Age(string root, TimeSpan age)
    {
        foreach (var input in CliInputs.Discover(root)) File.SetLastWriteTimeUtc(input.FullPath, DateTime.UtcNow - age);
    }

    // ---- the inputs and their identity ----

    [Test]
    public void The_inputs_are_the_project_graph_its_links_and_the_build_files_above_it()
    {
        using var fixture = Checkout();
        CliInputs.Discover(fixture.Root).Select(input => input.Name).ShouldBe(CheckoutInputs);
        Should.Throw<CliInputException>(() => CliInputs.Discover(fixture.PathOf("src")))
            .Message.ShouldBe(fixture.PathOf("src") + " has no " + CliInputs.EntryProject);
    }

    [Test]
    [Arguments("a source edit", Scripts + "Program.cs", "class P2 { }")]
    [Arguments("an untracked new file", Nightly + "Untracked.cs", "class U { }")]
    [Arguments("a project file edit", Scripts + "Antiphon.Scripts.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><Compile Include=\"../Linked.cs\" /><ProjectReference Include=\"../Antiphon.Nightly/Antiphon.Nightly.csproj\" /></ItemGroup></Project>")]
    [Arguments("a package version", Nightly + "Antiphon.Nightly.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"Npgsql\" Version=\"9.0.4\" /><ProjectReference Include=\"..\\..\\..\\src\\Lib\\Lib.csproj\" /></ItemGroup></Project>")]
    [Arguments("a Directory.Build edit", "Directory.Build.props", "<Project><Import Project=\"$(MSBuildThisFileDirectory)build/Shared.targets\" /></Project>")]
    [Arguments("a new Directory.Build file above a project", "scripts/lib/Directory.Build.targets", "<Project />")]
    [Arguments("a NuGet config", "scripts/NuGet.Config", "<configuration />")]
    [Arguments("global.json", "global.json", "{ \"sdk\": { \"version\": \"10.0.300\" } }")]
    [Arguments("an imported targets file", "build/Shared.targets", "<Project><PropertyGroup /></Project>")]
    [Arguments("a linked file", "scripts/lib/Linked.cs", "class K2 { }")]
    [Arguments("a referenced project's source", "src/Lib/Lib.cs", "class L2 { }")]
    [Arguments("a nested bin folder (the SDK compiles it)", Scripts + "Sub/bin/Nested.cs", "class N2 { }")]
    public void The_identity_moves_with_every_input(string change, string relative, string text)
    {
        using var fixture = Checkout();
        var before = Identity(fixture.Root);
        Write(fixture.Root, relative, text);
        Identity(fixture.Root).ShouldNotBe(before, change);
    }

    [Test]
    public void The_identity_moves_when_an_input_is_deleted_and_not_when_only_its_time_changes()
    {
        using var fixture = Checkout();
        var before = Identity(fixture.Root);
        File.SetLastWriteTimeUtc(fixture.PathOf("scripts", "lib", "Antiphon.Nightly", "Core.cs"), DateTime.UtcNow.AddDays(-3));
        Identity(fixture.Root).ShouldBe(before, "content, not times");
        File.Delete(fixture.PathOf("scripts", "lib", "Antiphon.Nightly", "Core.cs"));
        Identity(fixture.Root).ShouldNotBe(before);
    }

    [Test]
    [Arguments("an unreferenced project", "src/Other/Other.cs")]
    [Arguments("an unlinked file beside a link", "scripts/lib/NotLinked.cs")]
    [Arguments("a file only a comment names", "scripts/lib/Commented.cs")]
    [Arguments("docs", "docs/readme.md")]
    [Arguments("build output", Scripts + "obj/Generated.cs")]
    [Arguments("a bin-* folder", Scripts + "bin-c1/Out.cs")]
    [Arguments("the DefaultItemExcludes workspace folder", Scripts + "workspace/session.log")]
    [Arguments("an IDE folder", Scripts + ".vs/state.json")]
    [Arguments("the launcher cache", "bin-launcher/antiphon-scripts/build.log")]
    public void The_identity_does_not_move_with_files_outside_the_inputs(string change, string relative)
    {
        using var fixture = Checkout();
        var before = Identity(fixture.Root);
        Write(fixture.Root, relative, "changed " + Guid.NewGuid());
        Identity(fixture.Root).ShouldBe(before, change);
    }

    // The real CLI: its four projects and the launcher, the files they link, and the repository's build files.
    [Test]
    public void The_real_cli_inputs_include_every_project_link_and_build_file()
    {
        var names = CliInputs.Discover(DelegateScriptRunner.RepoRoot).Select(input => input.Name).ToArray();
        foreach (var expected in new[]
        {
            "scripts/lib/Antiphon.Scripts/Antiphon.Scripts.csproj", "scripts/lib/Antiphon.Scripts/Program.cs",
            "scripts/lib/Antiphon.Nightly/Antiphon.Nightly.csproj", "scripts/lib/Antiphon.LiveLane/Antiphon.LiveLane.csproj",
            "scripts/lib/Antiphon.CliLauncher/Antiphon.CliLauncher.csproj", "scripts/lib/Antiphon.CliLauncher/CliGate.cs",
            "src/Antiphon.IO.WriteBehind/Antiphon.IO.WriteBehind.csproj", "scripts/lib/CheckpointProcessRunner.cs",
            "scripts/lib/BuildDriverEnvironment.cs", "scripts/lib/LeaseTempDirectory.cs", "Directory.Build.props", "Directory.Build.targets",
            "Directory.Build.rsp", "global.json", ".editorconfig", "tests/Shared/CheckpointRepeat.targets",
        })
            names.ShouldContain(expected);
        names.ShouldNotContain(name => name.Contains("/bin/", StringComparison.Ordinal) || name.Contains("/obj/", StringComparison.Ordinal)
            || name.StartsWith("bin-launcher/", StringComparison.Ordinal) || name.StartsWith("tests/Antiphon.Tests/", StringComparison.Ordinal));
        AppHostCli.PublishedStamp.ShouldBe(CliCache.StampName, "DevAspire's run copy skips a published build by its manifest");
    }

    [Test]
    public void A_manifest_round_trips_and_a_partial_one_is_not_read()
    {
        using var fixture = Checkout();
        Age(fixture.Root, TimeSpan.FromHours(1));
        var manifest = CliIdentity.Compute(CliInputs.Discover(fixture.Root), null, DateTime.UtcNow).WithBuildEnd(42);
        var path = fixture.PathOf("manifest");
        manifest.Write(path);
        var read = CliManifest.Read(path).ShouldNotBeNull();
        read.Identity.ShouldBe(manifest.Identity);
        read.Name.ShouldBe(manifest.Identity[..24]);
        read.BuildEnd.ShouldBe(42);
        read.Recipe.ShouldBe(CliPublisher.Recipe);
        read.Files.Keys.ShouldBe(CheckoutInputs);
        read.Files.Values.ShouldAllBe(entry => entry.Ticks != 0, "inputs an hour old are trusted by time");
        CliIdentity.Of(read.Files).ShouldBe(manifest.Identity);
        File.WriteAllLines(path, File.ReadAllLines(path).SkipLast(1));
        CliManifest.Read(path).ShouldBeNull("a manifest missing a line is not a manifest");
        CliManifest.Read(fixture.PathOf("absent")).ShouldBeNull();
    }

    [Test]
    public void The_check_trusts_sizes_and_times_and_falls_back_to_the_bytes()
    {
        using var fixture = Checkout();
        Age(fixture.Root, TimeSpan.FromHours(1));
        var stamp = CliIdentity.Compute(CliInputs.Discover(fixture.Root), null, DateTime.UtcNow);
        CliIdentity.Matches(CliInputs.Discover(fixture.Root), stamp).ShouldBeTrue();

        var program = fixture.PathOf("scripts", "lib", "Antiphon.Scripts", "Program.cs");
        File.SetLastWriteTimeUtc(program, DateTime.UtcNow);
        CliIdentity.Matches(CliInputs.Discover(fixture.Root), stamp).ShouldBeTrue("a new time with the same bytes (a checkout) is the same input");
        File.WriteAllText(program, "class Q { }");
        CliIdentity.Matches(CliInputs.Discover(fixture.Root), stamp).ShouldBeFalse("same size, new bytes");
        File.WriteAllText(program, "class P { }");
        Write(fixture.Root, Nightly + "Untracked.cs", "class U { }");
        CliIdentity.Matches(CliInputs.Discover(fixture.Root), stamp).ShouldBeFalse("a new input");
        File.Delete(fixture.PathOf("scripts", "lib", "Antiphon.Nightly", "Untracked.cs"));
        CliIdentity.Matches(CliInputs.Discover(fixture.Root), stamp).ShouldBeTrue();

        var other = new CliManifest { Identity = stamp.Identity, Os = stamp.Os, Recipe = "0000000000000000", Hashed = stamp.Hashed, Files = stamp.Files };
        CliIdentity.Matches(CliInputs.Discover(fixture.Root), other).ShouldBeFalse("a build made by another recipe");
    }

    // A time recorded within the racy window is stored as 0: the same clock tick could hold a second write of the same size.
    [Test]
    public void A_time_too_close_to_the_hashing_is_never_trusted()
    {
        using var fixture = Checkout();
        var program = fixture.PathOf("scripts", "lib", "Antiphon.Scripts", "Program.cs");
        var written = File.GetLastWriteTimeUtc(program);
        var stamp = CliIdentity.Compute(CliInputs.Discover(fixture.Root), null, written.AddSeconds(1));
        stamp.Files[Scripts + "Program.cs"].Ticks.ShouldBe(0);
        File.WriteAllText(program, "class Q { }");
        File.SetLastWriteTimeUtc(program, written);
        CliIdentity.Matches(CliInputs.Discover(fixture.Root), stamp).ShouldBeFalse("same size and time, new bytes: hashed");
    }

    // ---- publishing: a fake build into the shared folder ----

    private sealed class Builds
    {
        public int Count;
        public int Exit;
        public string Log = "Build succeeded.\n";
        public bool WriteOutput = true;
        public List<bool> Incremental { get; } = [];
        public List<CliBuildRequest> Requests { get; } = [];
        public Action? During;

        public CliBuildResult Build(CliBuildRequest request)
        {
            Interlocked.Increment(ref Count);
            lock (Requests) Requests.Add(request);
            var marker = Path.Combine(request.Artifacts, "marker");
            lock (Incremental) Incremental.Add(File.Exists(marker));
            During?.Invoke();
            if (Exit == 0 && WriteOutput)
            {
                var output = Path.Combine(request.Artifacts, "bin", "Antiphon.Scripts", "release");
                Directory.CreateDirectory(Path.Combine(output, "runtimes"));
                File.WriteAllText(Path.Combine(output, CliCache.DllName), "built " + Count);
                File.WriteAllText(Path.Combine(output, "runtimes", "native.bin"), "native");
                File.WriteAllText(marker, "artifacts");
            }
            return new CliBuildResult { ExitCode = Exit, Log = Log };
        }
    }

    private static CliPublishOptions Options(Builds builds, StringWriter error, int keep = 3) => new()
    {
        Error = error, Build = builds.Build, Keep = keep, Sleep = _ => throw new InvalidOperationException("unexpected wait"),
    };

    private static string Folder(NightlyFixture fixture, string dll) => Path.GetDirectoryName(dll)!;

    [Test]
    public void A_first_call_builds_once_and_publishes_atomically_beside_a_pointer()
    {
        using var fixture = Checkout();
        var builds = new Builds();
        var error = new StringWriter();
        var cache = new CliCache(fixture.Root);

        var dll = CliPublisher.Ensure(fixture.Root, Options(builds, error)).ShouldNotBeNull(error.ToString());

        var identity = Identity(fixture.Root);
        dll.ShouldBe(Path.Combine(cache.Root, identity[..24], CliCache.DllName));
        File.ReadAllText(dll).ShouldBe("built 1");
        File.ReadAllText(Path.Combine(Folder(fixture, dll), "runtimes", "native.bin")).ShouldBe("native");
        CliCache.Stamp(Folder(fixture, dll))!.Identity.ShouldBe(identity);
        File.Exists(Path.Combine(Folder(fixture, dll), CliCache.LeaseName)).ShouldBeTrue();
        cache.ReadPointer().ShouldBe(identity[..24]);
        File.ReadAllText(cache.PointerPath).ShouldBe(identity[..24], "no line ending: cmd's set /p reads it whole");
        File.ReadAllText(cache.LogPath).ShouldBe("Build succeeded.\n");
        CliManifest.Read(cache.RecordPath)!.BuildEnd.ShouldBeGreaterThan(0);
        Directory.GetDirectories(cache.Root, "tmp-*").ShouldBeEmpty();
        error.ToString().ShouldBeEmpty();

        var request = builds.Requests.Single();
        request.Checkout.ShouldBe(fixture.Root);
        request.Artifacts.ShouldBe(cache.BuildDirectory);
        request.Arguments.ShouldBe(CliPublisher.BuildArguments(request.Project, cache.BuildDirectory));
        request.Arguments.ShouldBe(["build", request.Project, "-c", "Release", "--artifacts-path", cache.BuildDirectory, "-nologo", "-v", "q", "-m:2",
            "-nodeReuse:false", "-p:UseSharedCompilation=false"]);
        request.Project.ShouldBe(Path.Combine(fixture.Root, "scripts", "lib", "Antiphon.Scripts", "Antiphon.Scripts.csproj"));

        CliPublisher.Ensure(fixture.Root, Options(builds, error)).ShouldBe(dll);
        builds.Count.ShouldBe(1, "unchanged inputs reuse the published build");
    }

    [Test]
    public void A_source_edit_publishes_a_new_identity_beside_the_old_and_a_return_repoints_without_a_build()
    {
        using var fixture = Checkout();
        var builds = new Builds();
        var error = new StringWriter();
        var cache = new CliCache(fixture.Root);
        var first = CliPublisher.Ensure(fixture.Root, Options(builds, error))!;

        Write(fixture.Root, Scripts + "Program.cs", "class Edited { }");
        File.SetLastWriteTimeUtc(fixture.PathOf("scripts", "lib", "Antiphon.Scripts", "Program.cs"), DateTime.UtcNow.AddMinutes(1));
        var second = CliPublisher.Ensure(fixture.Root, Options(builds, error))!;
        second.ShouldNotBe(first);
        File.ReadAllText(second).ShouldBe("built 2");
        File.ReadAllText(first).ShouldBe("built 1", "a published build is never written again");
        builds.Incremental.ShouldBe([false, true], "the first build is clean; an edit newer than it builds incrementally");
        cache.ReadPointer().ShouldBe(Path.GetFileName(Folder(fixture, second)));

        Write(fixture.Root, Scripts + "Program.cs", "class P { }");
        CliPublisher.Ensure(fixture.Root, Options(builds, error)).ShouldBe(first);
        builds.Count.ShouldBe(2, "the earlier identity is still published");
        cache.ReadPointer().ShouldBe(Path.GetFileName(Folder(fixture, first)));
        error.ToString().ShouldBeEmpty();
    }

    [Test]
    public void A_failed_build_publishes_nothing_and_says_so_in_one_line()
    {
        using var fixture = Checkout();
        var builds = new Builds { Exit = 1, Log = "restore\nProgram.cs(1,1): error CS1002: ; expected\nBuild FAILED.\n" };
        var error = new StringWriter();
        var cache = new CliCache(fixture.Root);

        CliPublisher.Ensure(fixture.Root, Options(builds, error)).ShouldBeNull();

        error.ToString().ShouldBe("antiphon-cli: the CLI did not build (exit 1, log " + cache.LogPath + "): Program.cs(1,1): error CS1002: ; expected" + Environment.NewLine);
        File.ReadAllText(cache.LogPath).ShouldBe(builds.Log);
        cache.ReadPointer().ShouldBeNull();
        Directory.GetDirectories(cache.Root).ShouldBeEmpty("no published or temporary folder");

        var empty = new Builds { WriteOutput = false };
        error.GetStringBuilder().Clear();
        CliPublisher.Ensure(fixture.Root, Options(empty, error)).ShouldBeNull();
        error.ToString().ShouldStartWith("antiphon-cli: the CLI did not build (exit 0, log ");
    }

    [Test]
    public void Inputs_that_change_during_a_build_are_built_again_from_clean()
    {
        using var fixture = Checkout();
        var builds = new Builds();
        builds.During = () =>
        {
            if (builds.Count == 1) Write(fixture.Root, Scripts + "Program.cs", "class DuringTheBuild { }");
        };
        var error = new StringWriter();

        var dll = CliPublisher.Ensure(fixture.Root, Options(builds, error)).ShouldNotBeNull(error.ToString());

        builds.Count.ShouldBe(2);
        builds.Incremental.ShouldBe([false, false], "the edit is older than the first build's end, so the second is clean");
        CliCache.Stamp(Folder(fixture, dll))!.Identity.ShouldBe(Identity(fixture.Root), "the published identity is the final sources'");
        File.ReadAllText(dll).ShouldBe("built 2");

        Write(fixture.Root, Scripts + "Program.cs", "class NotYetBuilt { }");
        var restless = new Builds();
        restless.During = () => Write(fixture.Root, Scripts + "Program.cs", "class Restless" + restless.Count + " { }");
        error.GetStringBuilder().Clear();
        CliPublisher.Ensure(fixture.Root, Options(restless, error)).ShouldBeNull();
        restless.Count.ShouldBe(3);
        error.ToString().ShouldBe("antiphon-cli: the CLI's inputs changed during each of 3 builds; run it again" + Environment.NewLine);
    }

    // MSBuild's incremental build goes by file times; it is used only when every changed input is newer than the last build.
    [Test]
    public void The_shared_build_is_incremental_only_when_every_change_is_newer_than_the_last_build()
    {
        using var fixture = Checkout();
        var builds = new Builds();
        var error = new StringWriter();
        CliPublisher.Ensure(fixture.Root, Options(builds, error)).ShouldNotBeNull();
        var program = fixture.PathOf("scripts", "lib", "Antiphon.Scripts", "Program.cs");

        File.WriteAllText(program, "class Older { }");
        File.SetLastWriteTimeUtc(program, DateTime.UtcNow.AddDays(-1));
        CliPublisher.Ensure(fixture.Root, Options(builds, error)).ShouldNotBeNull();

        Write(fixture.Root, Nightly + "Added.cs", "class Added { }");
        File.SetLastWriteTimeUtc(fixture.PathOf("scripts", "lib", "Antiphon.Nightly", "Added.cs"), DateTime.UtcNow.AddMinutes(1));
        CliPublisher.Ensure(fixture.Root, Options(builds, error)).ShouldNotBeNull();

        Write(fixture.Root, Nightly + "notes.json", "{}");
        File.SetLastWriteTimeUtc(fixture.PathOf("scripts", "lib", "Antiphon.Nightly", "notes.json"), DateTime.UtcNow.AddMinutes(1));
        CliPublisher.Ensure(fixture.Root, Options(builds, error)).ShouldNotBeNull();

        builds.Incremental.ShouldBe([false, false, true, false],
            "first build; a change older than the last build; a new source newer than it; a new non-source file");
        error.ToString().ShouldBeEmpty();
    }

    [Test]
    public void The_incremental_guard_covers_new_changed_and_removed_inputs()
    {
        var end = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
        CliManifestEntry Entry(string hash) => new(hash.PadRight(64, '0'), 1, end.AddHours(-1).Ticks);
        CliManifest Manifest(params (string Name, string Hash)[] files) => new()
        {
            Identity = new string('a', 64), Os = CliIdentity.Os, Recipe = CliPublisher.Recipe, Hashed = end.Ticks, BuildEnd = end.Ticks,
            Files = new SortedDictionary<string, CliManifestEntry>(files.ToDictionary(f => f.Name, f => Entry(f.Hash)), StringComparer.Ordinal),
        };
        CliInput Input(string name, DateTime time) => new(name, name, 1, time.Ticks);
        var newer = end.AddMinutes(1);
        var older = end.AddMinutes(-1);
        var record = Manifest(("A.cs", "1"), ("p.csproj", "2"));

        bool Safe(CliManifest current, CliManifest? previous, bool output, params CliInput[] inputs) =>
            CliPublisher.IncrementalIsSafe(inputs, current, previous, output);

        Safe(Manifest(("A.cs", "1"), ("p.csproj", "2")), record, true, Input("A.cs", older), Input("p.csproj", older)).ShouldBeTrue("nothing changed");
        Safe(Manifest(("A.cs", "9"), ("p.csproj", "2")), record, true, Input("A.cs", newer), Input("p.csproj", older)).ShouldBeTrue("a newer edit");
        Safe(Manifest(("A.cs", "9"), ("p.csproj", "2")), record, true, Input("A.cs", older), Input("p.csproj", older)).ShouldBeFalse("an edit older than the build");
        Safe(Manifest(("A.cs", "1"), ("p.csproj", "9")), record, true, Input("A.cs", older), Input("p.csproj", newer)).ShouldBeTrue("a newer project edit");
        Safe(Manifest(("A.cs", "1"), ("B.cs", "3"), ("p.csproj", "2")), record, true, Input("A.cs", older), Input("B.cs", newer), Input("p.csproj", older)).ShouldBeTrue("a new source");
        Safe(Manifest(("A.cs", "1"), ("p.csproj", "2"), ("x.props", "3")), record, true, Input("A.cs", older), Input("p.csproj", older), Input("x.props", newer)).ShouldBeFalse("a new build file");
        Safe(Manifest(("p.csproj", "2")), record, true, Input("p.csproj", older)).ShouldBeTrue("a removed source (the compile list changes)");
        Safe(Manifest(("A.cs", "1")), record, true, Input("A.cs", older)).ShouldBeFalse("a removed build file");
        Safe(Manifest(("A.cs", "1"), ("p.csproj", "2")), null, true, Input("A.cs", older), Input("p.csproj", older)).ShouldBeFalse("no record");
        Safe(Manifest(("A.cs", "1"), ("p.csproj", "2")), record, false, Input("A.cs", older), Input("p.csproj", older)).ShouldBeFalse("no output");
        Safe(Manifest(("A.cs", "1"), ("p.csproj", "2")), record.WithBuildEnd(0), true, Input("A.cs", older), Input("p.csproj", older)).ShouldBeFalse("no build end");
    }

    // ---- the lock, concurrency, the pointer and pruning ----

    [Test]
    public void A_caller_waits_for_the_build_lock_and_gives_up_with_one_line()
    {
        using var fixture = Checkout();
        var cache = new CliCache(fixture.Root);
        Directory.CreateDirectory(cache.Root);
        var builds = new Builds();
        var now = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
        var error = new StringWriter();
        var waits = 0;

        using (new FileStream(cache.LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            CliPublisher.Ensure(fixture.Root, new CliPublishOptions
            {
                Error = error, Build = builds.Build, UtcNow = () => now, LockWait = TimeSpan.FromSeconds(1), LockPoll = TimeSpan.FromMilliseconds(250),
                Sleep = wait => { waits++; now += wait; },
            }).ShouldBeNull();
        }
        waits.ShouldBe(4);
        builds.Count.ShouldBe(0);
        error.ToString().ShouldBe("antiphon-cli: the build lock " + cache.LockPath + " was still held after 1 second" + Environment.NewLine);

        // Released while waiting: the caller builds once it has the lock.
        var held = new FileStream(cache.LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        error.GetStringBuilder().Clear();
        var dll = CliPublisher.Ensure(fixture.Root, new CliPublishOptions
        {
            Error = error, Build = builds.Build, Sleep = _ => held.Dispose(), LockPoll = TimeSpan.Zero,
        });
        held.Dispose();
        dll.ShouldNotBeNull(error.ToString());
        builds.Count.ShouldBe(1);
    }

    [Test]
    public async Task Concurrent_first_calls_build_once_and_share_the_published_build()
    {
        using var fixture = Checkout();
        var builds = new Builds { During = () => Thread.Sleep(300) };
        var errors = new StringWriter();
        var options = new CliPublishOptions { Error = TextWriter.Synchronized(errors), Build = builds.Build, LockPoll = TimeSpan.FromMilliseconds(20) };

        var calls = Enumerable.Range(0, 4).Select(_ => Task.Run(() => CliPublisher.Ensure(fixture.Root, options))).ToArray();
        var dlls = await Task.WhenAll(calls);

        builds.Count.ShouldBe(1, errors.ToString());
        dlls.Distinct().ShouldHaveSingleItem().ShouldNotBeNull();
        errors.ToString().ShouldBeEmpty();
    }

    [Test]
    public void The_pointer_is_replaced_whole_even_while_a_reader_holds_it()
    {
        using var fixture = Checkout();
        var cache = new CliCache(fixture.Root);
        Directory.CreateDirectory(cache.Root);
        cache.WritePointer("aaaaaaaaaaaaaaaaaaaaaaaa", _ => { }).ShouldBeTrue();
        var reader = new FileStream(cache.PointerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var waits = 0;
        cache.WritePointer("bbbbbbbbbbbbbbbbbbbbbbbb", _ =>
        {
            waits++;
            reader.Dispose();
        }).ShouldBeTrue();
        reader.Dispose();
        cache.ReadPointer().ShouldBe("bbbbbbbbbbbbbbbbbbbbbbbb");
        Directory.GetFiles(cache.Root, "*.tmp").ShouldBeEmpty("the new pointer is renamed into place");
        waits.ShouldBeLessThanOrEqualTo(1);
        File.WriteAllText(cache.PointerPath, "not-a-build");
        cache.ReadPointer().ShouldBeNull();
    }

    [Test]
    public void Pruning_keeps_the_current_the_newest_and_every_build_in_use()
    {
        using var fixture = Checkout();
        var builds = new Builds();
        var error = new StringWriter();
        var cache = new CliCache(fixture.Root);
        var start = DateTime.UtcNow.AddHours(-1);
        var folders = new List<string>();
        FileStream? lease = null;
        try
        {
            for (var i = 0; i < 5; i++)
            {
                Write(fixture.Root, Scripts + "Program.cs", "class Version" + i + " { }");
                var folder = Folder(fixture, CliPublisher.Ensure(fixture.Root, Options(builds, error))!);
                File.SetLastWriteTimeUtc(Path.Combine(folder, CliCache.StampName), start.AddMinutes(i));
                folders.Add(folder);
                // The first build is running (a nightly): it holds its lease.
                if (i == 0) lease = new FileStream(Path.Combine(folder, CliCache.LeaseName), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (i == 3)
                {
                    foreach (var name in new[] { "tmp-left", "trash-left", "boot-old", "boot-new" }) Directory.CreateDirectory(Path.Combine(cache.Root, name));
                    Directory.SetLastWriteTimeUtc(Path.Combine(cache.Root, "boot-old"), DateTime.UtcNow.AddHours(-2));
                    foreach (var name in new[] { "run-old", "run-new" }) Directory.CreateDirectory(Path.Combine(cache.Launcher, name));
                    Directory.SetLastWriteTimeUtc(Path.Combine(cache.Launcher, "run-old"), DateTime.UtcNow.AddDays(-8));
                }
            }

            Directory.GetDirectories(cache.Root).Select(Path.GetFileName).ShouldBe(
                new[] { Path.GetFileName(folders[0]), Path.GetFileName(folders[2]), Path.GetFileName(folders[3]), Path.GetFileName(folders[4]), "boot-new", "build" },
                ignoreOrder: true, "the current and the two newest (three kept), the one in use and a young bootstrap; the rest and every leftover went");
            Directory.GetDirectories(cache.Launcher).Select(Path.GetFileName).ShouldBe(["antiphon-scripts", "run-new"], ignoreOrder: true);
            CliCache.InUse(folders[0]).ShouldBeTrue();
            CliCache.InUse(folders[4]).ShouldBeFalse();
        }
        finally
        {
            lease?.Dispose();
        }
        CliCache.InUse(folders[0]).ShouldBeFalse("the lease ends with its holder");
    }

    // ---- the launch check (CliGate) ----

    private sealed class Gate
    {
        public bool Cleared;
        public List<string> Leases { get; } = [];
        public List<(string Dll, string[] Args)> Children { get; } = [];
        public int ChildExit;
        public Exception? ChildFailure;

        public CliGateContext Context(string? request, string baseDirectory, Builds builds, StringWriter error) => new()
        {
            Request = request,
            ClearRequest = () => Cleared = true,
            BaseDirectory = baseDirectory,
            Publish = Options(builds, error),
            HoldLease = Leases.Add,
            RunChild = (dll, args) =>
            {
                if (ChildFailure != null) throw ChildFailure;
                Children.Add((dll, args.ToArray()));
                return ChildExit;
            },
        };
    }

    private static readonly string[] Argv = ["build-slot", "--label", "a b", "", "say \"hi\"", @"C:\dir\", "--", "-m:2"];

    [Test]
    public void Without_the_variable_nothing_is_checked_and_the_command_runs_here()
    {
        using var fixture = Checkout();
        var builds = new Builds();
        var error = new StringWriter();
        var gate = new Gate();

        CliGate.Enter(Argv, gate.Context(null, fixture.PathOf("anywhere"), builds, error)).ShouldBeNull();
        CliGate.Enter(Argv, gate.Context("", fixture.PathOf("anywhere"), builds, error)).ShouldBeNull();

        gate.Cleared.ShouldBeFalse();
        gate.Leases.ShouldBe([fixture.PathOf("anywhere"), fixture.PathOf("anywhere")]);
        builds.Count.ShouldBe(0);
        gate.Children.ShouldBeEmpty();
        error.ToString().ShouldBeEmpty();
    }

    [Test]
    public void A_current_build_runs_the_command_here_and_its_children_never_see_the_variable()
    {
        using var fixture = Checkout();
        var builds = new Builds();
        var error = new StringWriter();
        var published = Folder(fixture, CliPublisher.Ensure(fixture.Root, Options(builds, error))!);
        var gate = new Gate();

        CliGate.Enter(Argv, gate.Context(fixture.Root, published, builds, error)).ShouldBeNull();

        gate.Cleared.ShouldBeTrue();
        gate.Leases.ShouldBe([published]);
        builds.Count.ShouldBe(1, "only the publish above built");
        gate.Children.ShouldBeEmpty();
        error.ToString().ShouldBeEmpty();
    }

    // No exit code is reserved: whatever the current build's command exits with (9 and 10 included) is the exit code.
    [Test]
    [Arguments(0)]
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(137)]
    [Arguments(-1073741510)]
    public void A_stale_build_publishes_the_current_one_and_runs_it_with_the_same_arguments(int exit)
    {
        using var fixture = Checkout();
        var builds = new Builds();
        var error = new StringWriter();
        var stale = Folder(fixture, CliPublisher.Ensure(fixture.Root, Options(builds, error))!);
        Write(fixture.Root, Nightly + "Core.cs", "class Changed { }");
        var gate = new Gate { ChildExit = exit };

        CliGate.Enter(Argv, gate.Context(fixture.Root, stale, builds, error)).ShouldBe(exit);

        builds.Count.ShouldBe(2);
        var (dll, args) = gate.Children.ShouldHaveSingleItem();
        CliCache.Stamp(Path.GetDirectoryName(dll)!)!.Identity.ShouldBe(Identity(fixture.Root));
        dll.ShouldNotStartWith(stale);
        args.ShouldBe(Argv);
        gate.Cleared.ShouldBeTrue();
        gate.Leases.ShouldBe([stale], "the stale build keeps its folder while it waits");
        new CliCache(fixture.Root).ReadPointer().ShouldBe(Path.GetFileName(Path.GetDirectoryName(dll)));
        error.ToString().ShouldBeEmpty();
    }

    [Test]
    public void An_unpublished_build_with_the_variable_publishes_and_runs_the_published_one()
    {
        using var fixture = Checkout();
        var builds = new Builds();
        var error = new StringWriter();
        var gate = new Gate { ChildExit = 3 };

        CliGate.Enter(["no-such-command"], gate.Context(fixture.Root, fixture.PathOf("bin", "Debug"), builds, error)).ShouldBe(3);

        builds.Count.ShouldBe(1);
        gate.Children.ShouldHaveSingleItem().Args.ShouldBe(["no-such-command"]);
    }

    [Test]
    public void A_build_that_cannot_be_had_exits_9_with_one_line_and_runs_nothing()
    {
        using var fixture = Checkout();
        var error = new StringWriter();
        var gate = new Gate();

        CliGate.Enter(Argv, gate.Context(fixture.Root, fixture.PathOf("old"), new Builds { Exit = 1, Log = "error MSB1009: Project file does not exist.\n" }, error)).ShouldBe(9);
        error.ToString().ShouldStartWith("antiphon-cli: the CLI did not build (exit 1, log ");
        error.ToString().Trim().Split('\n').ShouldHaveSingleItem();

        error.GetStringBuilder().Clear();
        CliGate.Enter(Argv, gate.Context(fixture.PathOf("src"), fixture.PathOf("old"), new Builds(), error)).ShouldBe(9);
        error.ToString().ShouldBe("antiphon-cli: " + fixture.PathOf("src") + " has no " + CliInputs.EntryProject + Environment.NewLine);

        error.GetStringBuilder().Clear();
        var cannot = new Gate { ChildFailure = new System.ComponentModel.Win32Exception(2, "The system cannot find the file specified") };
        CliGate.Enter(Argv, cannot.Context(fixture.Root, fixture.PathOf("old"), new Builds(), error)).ShouldBe(9);
        error.ToString().ShouldStartWith("antiphon-cli: cannot start the CLI (dotnet exec ");
        error.ToString().Trim().Split('\n').ShouldHaveSingleItem();
        gate.Children.ShouldBeEmpty();
    }

    [Test]
    public void The_variable_takes_a_git_bash_path_on_windows()
    {
        if (OperatingSystem.IsWindows())
        {
            CliGate.Checkout("/c/src/antiphon-wt-d").ShouldBe(@"C:\src\antiphon-wt-d");
            CliGate.Checkout("C:/src/antiphon").ShouldBe(@"C:\src\antiphon");
        }
        else
        {
            CliGate.Checkout("/c/src/antiphon").ShouldBe("/c/src/antiphon");
        }
    }
}
