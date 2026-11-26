using Antiphon.CliLauncher;
using Antiphon.Tests.Scripts.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 63 (N1 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md): scripts/antiphon.exe, the PowerShell CLI
/// entry (CliEntry), in process: the single-file product it is published as, its run (the published CLI with the launch
/// check, the arguments untouched, the in-process bootstrap, exit 9), its refresh once per published CLI build (publish,
/// install, the record that keeps a failure from repeating, the lock that never waits) and the CliGate hook that calls it.
/// A fake build plays dotnet publish; the files are real. The clock is fixed.
/// </summary>
[Category("Unit")]
public sealed class CliEntryTests
{
    private static readonly DateTime Now = new(2026, 11, 26, 9, 0, 0, DateTimeKind.Utc);
    private static readonly string[] Argv = ["build-slot", "--label", "a b", "", "say \"hi\"", @"C:\dir\", "--", "dotnet", "test", "--treenode-filter", "/*/*/(A*)|(B*)/*"];
    private const string Launcher = "scripts/lib/Antiphon.CliLauncher/";

    private static void Write(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    // A checkout with the CLI project (one source) and the launcher project (the entry's only source).
    private static NightlyFixture Checkout()
    {
        var fixture = NightlyFixture.Create("cli-entry");
        Write(fixture.Root, "scripts/lib/Antiphon.Scripts/Antiphon.Scripts.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        Write(fixture.Root, "scripts/lib/Antiphon.Scripts/Program.cs", "class P { }");
        Write(fixture.Root, Launcher + "Antiphon.CliLauncher.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        Write(fixture.Root, Launcher + "CliEntry.cs", "class E { }");
        Write(fixture.Root, "global.json", "{ \"sdk\": { \"version\": \"10.0.204\" } }");
        return fixture;
    }

    // Plays dotnet: the CLI's build writes Antiphon.Scripts.dll, the entry's publish its single file.
    private sealed class Builds
    {
        public int Cli;
        public int Entry;
        public int EntryExit;
        public List<CliBuildRequest> Requests { get; } = [];

        public CliBuildResult Build(CliBuildRequest request)
        {
            lock (Requests) Requests.Add(request);
            if (request.Arguments[0] == "publish")
            {
                Entry++;
                if (EntryExit != 0) return new CliBuildResult { ExitCode = EntryExit, Log = "error NETSDK1084: There is no application host available for the specified RuntimeIdentifier.\n" };
                var output = Path.Combine(request.Artifacts, "publish", "Antiphon.CliLauncher", "release_" + CliEntry.Rid);
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "Antiphon.CliLauncher.exe"), "entry " + Entry);
                return new CliBuildResult { ExitCode = 0, Log = "ok\n" };
            }
            Cli++;
            var bin = Path.Combine(request.Artifacts, "bin", "Antiphon.Scripts", "release");
            Directory.CreateDirectory(bin);
            File.WriteAllText(Path.Combine(bin, CliCache.DllName), "cli " + Cli);
            return new CliBuildResult { ExitCode = 0, Log = "ok\n" };
        }
    }

    private static CliPublishOptions Options(Builds builds, TextWriter error) => new()
    {
        Error = error, Build = builds.Build, UtcNow = () => Now, Sleep = _ => throw new InvalidOperationException("unexpected wait"),
        LockWait = TimeSpan.Zero,
    };

    private static string Exe(NightlyFixture fixture) => fixture.PathOf("scripts", "antiphon.exe");

    private static string Record(NightlyFixture fixture) => File.ReadAllText(fixture.PathOf("bin-launcher", "antiphon-entry", CliEntry.CheckedName));

    // ---- the product ----

    [Test]
    public void The_entry_is_the_launcher_project_published_as_one_file_for_this_runtime_in_its_own_cache()
    {
        var product = CliEntry.Product;
        product.CacheName.ShouldBe("antiphon-entry");
        product.Project.ShouldBe(CliEntry.LauncherProject);
        product.DllName.ShouldBe("Antiphon.CliLauncher.exe");
        product.Output.ShouldBe("publish/Antiphon.CliLauncher/release_" + CliEntry.Rid);
        product.Arguments("P.csproj", "A").ShouldBe(["publish", "P.csproj", "-c", "Release", "-r", CliEntry.Rid, "--no-self-contained",
            "-p:PublishSingleFile=true", "--artifacts-path", "A", "-nologo", "-v", "q", "-m:2", "-nodeReuse:false", "-p:UseSharedCompilation=false"]);
        product.Recipe.ShouldContain("-r " + CliEntry.Rid + " --no-self-contained -p:PublishSingleFile=true");
        product.Recipe.ShouldNotBe(CliProduct.Cli.Recipe);
        // The CLI's recipe is the one the publisher had before products named their own arguments.
        CliProduct.Cli.Recipe.ShouldBe("dotnet build <project> -c Release --artifacts-path <artifacts> -nologo -v q -m:2 -nodeReuse:false "
            + "-p:UseSharedCompilation=false; copy bin/Antiphon.Scripts/release; environment without MSBuild*");
        CliProduct.Cli.Arguments("P.csproj", "A").ShouldBe(CliPublisher.BuildArguments("P.csproj", "A"));

        using var fixture = Checkout();
        CliInputs.Discover(fixture.Root, CliEntry.LauncherProject).Select(input => input.Name)
            .ShouldBe(["global.json", Launcher + "Antiphon.CliLauncher.csproj", Launcher + "CliEntry.cs"], "the entry's inputs are the launcher's alone");
    }

    [Test]
    public void Only_a_process_named_antiphon_exe_is_the_entry_and_its_checkout_is_the_folder_above_scripts()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "a checkout"));
        CliEntry.IsEntry(Path.Combine(root, "scripts", "antiphon.exe")).ShouldBeTrue();
        CliEntry.IsEntry(Path.Combine(root, "scripts", "ANTIPHON.EXE")).ShouldBeTrue();
        CliEntry.IsEntry(Path.Combine(root, "dotnet", "dotnet.exe")).ShouldBeFalse("the bootstrap runs under dotnet exec");
        CliEntry.IsEntry(Path.Combine(root, "bin-launcher", "antiphon-entry", "0123", "Antiphon.CliLauncher.exe")).ShouldBeFalse("a published entry is not run from its cache");
        CliEntry.IsEntry(null).ShouldBeFalse();
        CliEntry.CheckoutOf(Path.Combine(root, "scripts", "antiphon.exe")).ShouldBe(root);
        CliEntry.PathIn(root).ShouldBe(Path.Combine(root, "scripts", "antiphon.exe"));
    }

    // ---- the run ----

    [Test]
    [Arguments(0)]
    [Arguments(9)]
    [Arguments(130)]
    [Arguments(-1)]
    public void The_entry_runs_the_published_cli_with_the_check_and_every_argument_and_returns_its_exit_code(int exit)
    {
        using var fixture = Checkout();
        var builds = new Builds();
        var error = new StringWriter();
        var dll = CliPublisher.Ensure(fixture.Root, Options(builds, error)).ShouldNotBeNull(error.ToString());
        var runs = new List<(string Dll, string[] Args, string Check)>();

        CliEntry.Run(Argv, Exe(fixture), Options(builds, error), (d, a, c) => { runs.Add((d, a.ToArray(), c)); return exit; }).ShouldBe(exit);

        var run = runs.ShouldHaveSingleItem();
        run.Dll.ShouldBe(dll);
        run.Args.ShouldBe(Argv);
        run.Check.ShouldBe(fixture.Root);
        builds.Cli.ShouldBe(1, "a published CLI runs as it is: the CLI checks itself");
        error.ToString().ShouldBeEmpty();
    }

    [Test]
    public void With_nothing_published_the_entry_publishes_the_cli_in_process_first()
    {
        using var fixture = Checkout();
        var builds = new Builds();
        var error = new StringWriter();
        string? ran = null;

        CliEntry.Run(["--help"], Exe(fixture), Options(builds, error), (d, _, _) => { ran = d; return 2; }).ShouldBe(2);

        builds.Cli.ShouldBe(1);
        ran.ShouldBe(Path.Combine(new CliCache(fixture.Root).FolderOf(new CliCache(fixture.Root).ReadPointer()!), CliCache.DllName));
        error.ToString().ShouldBeEmpty();
    }

    [Test]
    public void An_entry_that_cannot_have_or_start_the_cli_exits_9_with_one_line()
    {
        using var fixture = Checkout();
        var error = new StringWriter();
        var stray = fixture.PathOf("tools", "bin", "antiphon.exe");
        CliEntry.Run(["x"], stray, Options(new Builds(), error), (_, _, _) => throw new InvalidOperationException("must not run")).ShouldBe(9);
        error.ToString().ShouldBe("antiphon-cli: " + fixture.PathOf("tools") + " has no " + CliInputs.EntryProject + Environment.NewLine,
            "an exe outside <checkout>/scripts names the folder above its own, which has no CLI");

        error.GetStringBuilder().Clear();
        CliEntry.Run(["x"], Exe(fixture), Options(new Builds(), error),
            (_, _, _) => throw new System.ComponentModel.Win32Exception(2, "The system cannot find the file specified")).ShouldBe(9);
        error.ToString().ShouldStartWith("antiphon-cli: cannot start the CLI (dotnet exec ");
        error.ToString().Trim().Split('\n').ShouldHaveSingleItem();
    }

    // ---- the refresh ----

    [Test]
    public void A_refresh_publishes_and_installs_the_entry_once_per_cli_build_and_rebuilds_only_for_a_launcher_change()
    {
        using var fixture = Checkout();
        var builds = new Builds();
        var error = new StringWriter();
        var first = new string('a', 24);

        CliEntry.Refresh(fixture.Root, fixture.PathOf("bin-launcher", "antiphon-scripts", first), error, Options(builds, error));
        File.ReadAllText(Exe(fixture)).ShouldBe("entry 1");
        Record(fixture).ShouldBe(first + " ok");
        builds.Entry.ShouldBe(1);
        var request = builds.Requests.ShouldHaveSingleItem();
        request.Project.ShouldBe(Path.Combine(fixture.Root, "scripts", "lib", "Antiphon.CliLauncher", "Antiphon.CliLauncher.csproj"));
        request.Artifacts.ShouldBe(Path.Combine(fixture.Root, "bin-launcher", "antiphon-entry", "build"));

        // The same CLI build: nothing is read but the record.
        File.WriteAllText(Exe(fixture), "replaced by hand");
        CliEntry.Refresh(fixture.Root, first, error, Options(builds, error));
        File.ReadAllText(Exe(fixture)).ShouldBe("replaced by hand");

        // Another CLI build with the same launcher: the published entry is installed again, nothing is built.
        CliEntry.Refresh(fixture.Root, new string('b', 24), error, Options(builds, error));
        File.ReadAllText(Exe(fixture)).ShouldBe("entry 1");
        builds.Entry.ShouldBe(1);

        // A launcher edit: a new entry identity, built and installed.
        Write(fixture.Root, Launcher + "CliEntry.cs", "class E2 { }");
        CliEntry.Refresh(fixture.Root, new string('c', 24), error, Options(builds, error));
        File.ReadAllText(Exe(fixture)).ShouldBe("entry 2");
        builds.Entry.ShouldBe(2);
        builds.Cli.ShouldBe(0, "a refresh never builds the CLI");

        // The entry deleted (git clean): the same CLI build installs it again from the cache.
        File.Delete(Exe(fixture));
        CliEntry.Refresh(fixture.Root, new string('c', 24), error, Options(builds, error));
        File.ReadAllText(Exe(fixture)).ShouldBe("entry 2");
        builds.Entry.ShouldBe(2);
        Directory.GetFiles(fixture.PathOf("bin-launcher", "antiphon-entry"), "*.exe").ShouldBeEmpty("no tmp or trash file is left");
        error.ToString().ShouldBeEmpty();
    }

    [Test]
    public void A_failed_refresh_is_one_line_keeps_the_old_entry_and_is_not_tried_again_for_the_same_cli_build()
    {
        using var fixture = Checkout();
        var builds = new Builds { EntryExit = 1 };
        var error = new StringWriter();
        Write(fixture.Root, "scripts/antiphon.exe", "old entry");
        var build = new string('d', 24);

        CliEntry.Refresh(fixture.Root, build, error, Options(builds, error));

        error.ToString().ShouldStartWith("antiphon-cli: the PowerShell entry " + Exe(fixture) + " is not current (the PowerShell entry did not build (exit 1, log ");
        error.ToString().ShouldContain("error NETSDK1084");
        error.ToString().Trim().Split('\n').ShouldHaveSingleItem();
        File.ReadAllText(Exe(fixture)).ShouldBe("old entry");
        Record(fixture).ShouldBe(build + " failed");

        error.GetStringBuilder().Clear();
        CliEntry.Refresh(fixture.Root, build, error, Options(builds, error));
        builds.Entry.ShouldBe(1, "not tried again until the CLI is published again");
        error.ToString().ShouldBeEmpty();

        builds.EntryExit = 0;
        CliEntry.Refresh(fixture.Root, new string('e', 24), error, Options(builds, error));
        File.ReadAllText(Exe(fixture)).ShouldBe("entry 2");
        error.ToString().ShouldBeEmpty();
    }

    [Test]
    public void A_refresh_never_waits_for_another_one()
    {
        using var fixture = Checkout();
        var builds = new Builds();
        var error = new StringWriter();
        var root = fixture.PathOf("bin-launcher", "antiphon-entry");
        Directory.CreateDirectory(root);

        using (new FileStream(Path.Combine(root, "install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            CliEntry.Refresh(fixture.Root, new string('f', 24), error, Options(builds, error));

        builds.Entry.ShouldBe(0);
        File.Exists(Exe(fixture)).ShouldBeFalse();
        File.Exists(Path.Combine(root, CliEntry.CheckedName)).ShouldBeFalse("the refresh that holds the lock writes the record");
        error.ToString().ShouldBeEmpty();
    }

    [Test]
    public void Installing_over_the_same_bytes_writes_nothing()
    {
        using var fixture = Checkout();
        var source = fixture.PathOf("published.exe");
        File.WriteAllText(source, "same");
        Write(fixture.Root, "scripts/antiphon.exe", "same");
        var before = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Exe(fixture), before);
        Directory.CreateDirectory(fixture.PathOf("cache"));

        CliEntry.Install(source, Exe(fixture), fixture.PathOf("cache")).ShouldBeTrue();

        File.GetLastWriteTimeUtc(Exe(fixture)).ShouldBe(before);
    }

    // ---- the hook in the launch check ----

    [Test]
    public void The_launch_check_calls_the_entry_hook_for_the_build_the_command_runs_from()
    {
        using var fixture = Checkout();
        var builds = new Builds();
        var error = new StringWriter();
        var published = Path.GetDirectoryName(CliPublisher.Ensure(fixture.Root, Options(builds, error))!)!;
        var hooked = new List<(string Checkout, string Folder)>();
        CliGateContext Context(string? request, string folder) => new()
        {
            Request = request, BaseDirectory = folder, Publish = Options(builds, error), HoldLease = _ => { },
            RunChild = (_, _) => 0, Entry = (checkout, built) => hooked.Add((checkout, built)),
        };

        CliGate.Enter(Argv, Context(null, published)).ShouldBeNull();
        hooked.ShouldBeEmpty("without the variable nothing is checked");

        CliGate.Enter(Argv, Context(fixture.Root, published)).ShouldBeNull();
        hooked.ShouldBe([(fixture.Root, published)]);

        // A stale build publishes the current one and runs it unchecked as a child: the hook runs here, for that build.
        hooked.Clear();
        Write(fixture.Root, "scripts/lib/Antiphon.Scripts/Program.cs", "class Changed { }");
        CliGate.Enter(Argv, Context(fixture.Root, published)).ShouldBe(0);
        var current = new CliCache(fixture.Root);
        hooked.ShouldBe([(fixture.Root, current.FolderOf(current.ReadPointer()!))]);
        error.ToString().ShouldBeEmpty();
    }
}
