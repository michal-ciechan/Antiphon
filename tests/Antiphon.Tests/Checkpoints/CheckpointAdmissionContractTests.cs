using System.Net;
using System.Reflection;
using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
[ParallelLimiter<CheckpointToolSpawnLimit>]
public sealed class CheckpointAdmissionContractTests : CheckpointTestBase
{
    [Test]
    [Timeout(60_000)]
    public void C1616_cleanup_does_not_spawn_driver_discovery_for_index_queries(CancellationToken cancellationToken)
    {
        var root = TempDir();
        CheckpointFixtureProcess.Git(root, "init", "-q");
        CheckpointFixtureProcess.Git(root, "config", "filter.c1616.clean", "c1616-driver-must-not-run");
        CheckpointFixtureProcess.Git(root, "config", "diff.c1616.textconv", "c1616-driver-must-not-run");
        var starts = new List<string[]>();
        var hardened = true;
        var runner = new CheckpointNativeGitRunner((start, timeout, git) =>
            CheckpointProcessRunner.RunObserved(start, timeout, git, child =>
            {
                var arguments = child.ArgumentList.ToArray();
                starts.Add(arguments);
                hardened &= arguments.Contains("core.fsmonitor=false")
                    && arguments.Contains("diff.external=")
                    && arguments.Any(arg => arg.StartsWith("core.hooksPath=", StringComparison.Ordinal))
                    && child.Environment["GIT_OPTIONAL_LOCKS"] == "0"
                    && child.Environment["GIT_CONFIG_NOSYSTEM"] == "1";
            }, cancellationToken));
        var context = new CheckpointCleanupContext(new CheckpointNativeIo(), runner);
        var write = new CheckpointWrite(context);
        var ledger = new CheckpointOutputLedger(context);
        var output = Path.Combine(root, "bin-c1616");
        write.CreateFreshDirectory(output, write.Under(root)).ShouldBeNull();
        for (var i = 0; i < 8; i++)
            write.Text(Path.Combine(output, "payload-" + i), "owned", write.Under(output)).ShouldBeNull();
        ledger.Seal(output).ShouldBeNull();
        starts.Clear();
        ledger.Remove(output).ShouldBeNull();
        Directory.Exists(output).ShouldBeFalse();
        var queries = starts.Count(args => args.Contains("ls-files"));
        var discoveries = starts.Count(args => args.Contains("--get-regexp"));
        var total = starts.Count;
        Console.WriteLine($"C1616 cleanup payloads=8 indexQueries={queries} driverDiscoveryProcesses={discoveries} totalProcesses={total}");
        queries.ShouldBeGreaterThanOrEqualTo(8, "per-entry tracked-file revalidation stays live");
        hardened.ShouldBeTrue("all index queries retain Git hardening");

        // A later index update must still veto removal; there is no cached Git verdict.
        var tracked = Path.Combine(root, "bin-c1616-tracked");
        write.CreateFreshDirectory(tracked, write.Under(root)).ShouldBeNull();
        var keep = Path.Combine(tracked, "keep.txt");
        write.Text(keep, "keep", write.Under(tracked)).ShouldBeNull();
        ledger.Seal(tracked).ShouldBeNull();
        ledger.Check(tracked).ShouldBeNull();
        CheckpointFixtureProcess.Git(root, "add", "--", keep);
        ledger.Remove(tracked).ShouldBe("tracked");
        File.ReadAllText(keep).ShouldBe("keep");

        var changed = Path.Combine(root, "bin-c1616-changed");
        write.CreateFreshDirectory(changed, write.Under(root)).ShouldBeNull();
        var tampered = Path.Combine(changed, "keep.txt");
        write.Text(tampered, "owned", write.Under(changed)).ShouldBeNull();
        ledger.Seal(changed).ShouldBeNull();
        File.WriteAllText(tampered, "changed");
        ledger.Remove(changed).ShouldContain("output-ledger-mismatch");
        File.ReadAllText(tampered).ShouldBe("changed");
        discoveries.ShouldBe(0, "an index-only query cannot invoke named filter/textconv drivers");
        total.ShouldBe(queries, "one hardened child per index query");

        // Unrecognized option shapes still take the original driver-discovery path.
        starts.Clear();
        var optioned = runner.Run(new("git", root,
            ["-C", root, "ls-files", "--stage", "-z", "--", keep], 10_000));
        optioned.Exit.ShouldBe(0);
        optioned.Stdout.ShouldContain("keep.txt");
        starts.Count(args => args.Contains("--get-regexp")).ShouldBe(1);
        starts.Count.ShouldBe(2);
    }

    [Test]
    [Timeout(60_000)]
    public void Driver_settings_answer_is_reused_only_within_its_lifetime(CancellationToken cancellationToken)
    {
        var root = TempDir();
        CheckpointFixtureProcess.Git(root, "init", "-q");
        CheckpointFixtureProcess.Git(root, "config", "filter.cache.clean", "cache-driver-must-not-run");
        var starts = new List<string[]>();
        CheckpointProcessRunner.Result Status(int lifetime)
        {
            var start = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = root };
            foreach (var argument in new[] { "-C", root, "status", "--porcelain" }) start.ArgumentList.Add(argument);
            return CheckpointProcessRunner.RunObserved(start, 30_000, true,
                child => starts.Add(child.ArgumentList.ToArray()), lifetime, cancellationToken);
        }

        // Default (0): every call asks git again.
        Status(0).Exit.ShouldBe(0);
        Status(0).Exit.ShouldBe(0);
        starts.Count(args => args.Contains("--get-regexp")).ShouldBe(2);

        // Within a lifetime the answer is reused, and the driver is still disabled on the reused call.
        starts.Clear();
        Status(60_000).Exit.ShouldBe(0);
        Status(60_000).Exit.ShouldBe(0);
        starts.Count(args => args.Contains("--get-regexp")).ShouldBe(1);
        starts.Count(args => args.Contains("status") && args.Contains("filter.cache.clean=")).ShouldBe(2);

        // A different directory is a different answer.
        starts.Clear();
        var other = TempDir();
        CheckpointFixtureProcess.Git(other, "init", "-q");
        CheckpointFixtureProcess.Git(other, "config", "filter.other.clean", "other-driver-must-not-run");
        var start = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = other };
        foreach (var argument in new[] { "-C", other, "status", "--porcelain" }) start.ArgumentList.Add(argument);
        CheckpointProcessRunner.RunObserved(start, 30_000, true,
            child => starts.Add(child.ArgumentList.ToArray()), 60_000, cancellationToken).Exit.ShouldBe(0);
        starts.Count(args => args.Contains("--get-regexp")).ShouldBe(1);
    }

    [Test]
    [Timeout(60_000)]
    public void Repository_found_from_its_files_names_what_rev_parse_names(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // A temp path through a link is declined by design (git climbs the real path): nothing to compare there.
        for (var up = new DirectoryInfo(TempDir()); up != null; up = up.Parent)
            if ((up.Attributes & FileAttributes.ReparsePoint) != 0)
                Skip.Test("the temp directory's path holds a link");
        // The same directory, whatever its spelling (git prints the long, real path; a short 8.3 name or another
        // spelling of the same folder still agrees): a marker written through one is seen through the other.
        static void SameDirectory(string mine, string git, string label)
        {
            var marker = ".same-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(Path.Combine(mine, marker), "");
            try { File.Exists(Path.Combine(git, marker)).ShouldBeTrue(label + ": " + mine + " vs " + git); }
            finally { File.Delete(Path.Combine(mine, marker)); }
        }
        void Agrees(string directory, string label)
        {
            var found = CheckpointProcessRunner.FindRepository(directory);
            found.ShouldNotBeNull(label);
            var lines = CheckpointFixtureProcess.Git(directory, "rev-parse", "--show-toplevel", "--absolute-git-dir")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            SameDirectory(found[0], lines[0], label + " top level");
            SameDirectory(found[1], lines[1], label + " git dir");
        }

        var root = Directory.CreateDirectory(Path.Combine(TempDir(), "repo")).FullName;
        CheckpointFixtureProcess.Git(root, "init", "-q", "-b", "main");
        File.WriteAllText(Path.Combine(root, "a.txt"), "a\n");
        CheckpointFixtureProcess.Git(root, "add", "a.txt");
        CheckpointFixtureProcess.Git(root, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "seed");
        Agrees(root, "checkout root");
        var sub = Directory.CreateDirectory(Path.Combine(root, "sub", "deeper")).FullName;
        if (OperatingSystem.IsWindows()) Agrees(sub, "subdirectory");
        else CheckpointProcessRunner.FindRepository(sub).ShouldBeNull("off Windows git stops at filesystem boundaries the files cannot show");

        // A linked worktree: a .git file naming its git dir under the main repository's worktrees/.
        var linked = Path.Combine(TempDir(), "linked");
        CheckpointFixtureProcess.Git(root, "worktree", "add", "-q", linked);
        Agrees(linked, "linked worktree");

        // What the files cannot settle as git does is git's to answer.
        CheckpointProcessRunner.FindRepository(Path.Combine(root, ".git")).ShouldBeNull("a git dir has no top level");
        CheckpointProcessRunner.FindRepository(Path.Combine(sub, "..")).ShouldBeNull("a .. segment");
        CheckpointProcessRunner.FindRepository(TempDir()).ShouldBeNull("no repository");
        var bare = Directory.CreateDirectory(Path.Combine(TempDir(), "bare.git")).FullName;
        CheckpointFixtureProcess.Git(bare, "init", "-q", "--bare");
        CheckpointProcessRunner.FindRepository(bare).ShouldBeNull("a bare repository");
        var gitConfig = Path.Combine(root, ".git", "config");
        var beforeWorktree = File.ReadAllText(gitConfig);
        CheckpointFixtureProcess.Git(root, "config", "core.worktree", sub);
        try { CheckpointProcessRunner.FindRepository(root).ShouldBeNull("core.worktree moves the top level"); }
        finally { File.WriteAllText(gitConfig, beforeWorktree); }
        Agrees(root, "after core.worktree is unset");

        // A bare setting in any form git reads as true (valueless, on its section header's line): no work tree.
        var config = Path.Combine(root, ".git", "config");
        var original = File.ReadAllText(config);
        foreach (var (extra, label) in new[] { ("[core]\n\tbare\n", "a valueless bare line"), ("[core] bare = true\n", "bare on the header line") })
        {
            try
            {
                File.WriteAllText(config, original + extra);
                CheckpointProcessRunner.FindRepository(root).ShouldBeNull(label);
            }
            finally { File.WriteAllText(config, original); }
        }
        Agrees(root, "after the bare lines are gone");

        // A .git file git's read_gitfile would reject ("gitdir:" with no space) is git's to report.
        var odd = Directory.CreateDirectory(Path.Combine(TempDir(), "odd")).FullName;
        File.WriteAllText(Path.Combine(odd, ".git"), "gitdir:" + Path.Combine(root, ".git") + "\n");
        CheckpointProcessRunner.FindRepository(odd).ShouldBeNull("gitdir: without its space");
    }

    [Test]
    [Timeout(60_000)]
    public void Driver_lookup_answers_a_driverless_config_from_its_files(CancellationToken cancellationToken)
    {
        var root = TempDir();
        CheckpointFixtureProcess.Git(root, "init", "-q");
        var sub = Directory.CreateDirectory(Path.Combine(root, "sub")).FullName;
        var starts = new List<string[]>();
        int Lookups(string directory, string[]? leading = null, string executable = "git")
        {
            starts.Clear();
            var start = new System.Diagnostics.ProcessStartInfo(executable) { WorkingDirectory = directory };
            foreach (var argument in (leading ?? []).Concat(["status", "--porcelain"])) start.ArgumentList.Add(argument);
            CheckpointProcessRunner.RunObserved(start, 30_000, true,
                child => starts.Add(child.ArgumentList.ToArray()), 0, cancellationToken).Exit.ShouldBe(0);
            return starts.Count(args => args.Contains("--get-regexp"));
        }

        // Hardened git reads only the repository's own config: with no driver word in it, git would print nothing.
        Lookups(root).ShouldBe(0, "a driverless config is read from its file");
        starts.Count.ShouldBe(1, "only the read itself starts");
        Lookups(sub).ShouldBe(0, "found from a subdirectory too");
        Lookups(sub, ["-C", root]).ShouldBe(0, "a plain -C into a driverless repository");

        // What the files cannot settle the way git would is git's to answer.
        var gitPath = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, OperatingSystem.IsWindows() ? "git.exe" : "git"))
            .First(File.Exists);
        Lookups(root, executable: gitPath).ShouldBe(1, "a path to an executable is not plain git");
        Lookups(sub, ["--no-pager"]).ShouldBe(1, "another global option before the subcommand");
        Lookups(sub, ["-C", ".."]).ShouldBe(1, "a .. segment, which git resolves after links");
        var callerDrivers = Path.Combine(TempDir(), "caller.gitconfig");
        File.WriteAllText(callerDrivers, "[filter \"cli\"]\n\tclean = cli-driver-must-not-run\n");
        Lookups(root, ["-c", "include.path=" + callerDrivers]).ShouldBe(1, "a caller's -c include");
        var read = starts.Single(args => args.Contains("status"));
        Array.IndexOf(read, "filter.cli.clean=").ShouldBeGreaterThan(Array.IndexOf(read, "include.path=" + callerDrivers),
            "the disabling pair comes after the caller's include, so the include cannot switch the driver back on");
        var junk = Directory.CreateDirectory(Path.Combine(sub, "junk", ".git")).FullName;
        Directory.CreateDirectory(Path.Combine(junk, "objects"));
        Directory.CreateDirectory(Path.Combine(junk, "refs"));
        File.WriteAllText(Path.Combine(junk, "HEAD"), "not a head\n");
        File.WriteAllText(Path.Combine(junk, "config"), "[core]\n\tbare = false\n");
        Lookups(Path.Combine(sub, "junk")).ShouldBe(1, "a .git with an invalid HEAD, which git skips");
        if (!OperatingSystem.IsWindows())
        {
            var link = Path.Combine(TempDir(), "linked");
            Directory.CreateSymbolicLink(link, root);
            // The fixture teardown refuses a temp root holding a link, so the link goes before it runs.
            try { Lookups(link).ShouldBe(1, "a link on the path, which git climbs by its real path"); }
            finally { Directory.Delete(link); }
        }

        // Any mention of a driver word or an include asks git, even where git would still find no driver.
        CheckpointFixtureProcess.Git(root, "config", "remote.origin.url", "https://example.invalid/diff-tools.git");
        Lookups(root).ShouldBe(1, "a URL naming diff is a doubt git settles");
        CheckpointFixtureProcess.Git(root, "config", "--unset", "remote.origin.url");
        Lookups(root).ShouldBe(0);
        var included = Path.Combine(TempDir(), "drivers.gitconfig");
        File.WriteAllText(included, "[filter \"inc\"]\n\tclean = included-driver-must-not-run\n");
        CheckpointFixtureProcess.Git(root, "config", "include.path", included);
        Lookups(root).ShouldBe(1, "an include can name drivers the file does not show");
        starts.Count(args => args.Contains("status") && args.Contains("filter.inc.clean=")).ShouldBe(1,
            "git's answer disables the included driver");

        // A stub .git git would skip (no HEAD, objects or refs) is not taken as the repository: git decides.
        var clean = TempDir();
        CheckpointFixtureProcess.Git(clean, "init", "-q");
        var stub = Directory.CreateDirectory(Path.Combine(clean, "stub", ".git")).FullName;
        File.WriteAllText(Path.Combine(stub, "config"), "[core]\n\tbare = false\n");
        Lookups(Path.Combine(clean, "stub")).ShouldBe(1, "a stub .git leaves the answer to git");

        // The lookup reads the repository the read names with -C, not the working directory's.
        starts.Clear();
        var target = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = clean };
        foreach (var argument in new[] { "-C", root, "status", "--porcelain" }) target.ArgumentList.Add(argument);
        CheckpointProcessRunner.RunObserved(target, 30_000, true,
            child => starts.Add(child.ArgumentList.ToArray()), 0, cancellationToken).Exit.ShouldBe(0);
        starts.Count(args => args.Contains("--get-regexp")).ShouldBe(1, "-C names a repository with an include");
        starts.Count(args => args.Contains("status") && args.Contains("filter.inc.clean=")).ShouldBe(1,
            "the -C repository's driver is disabled even from a driverless working directory");
    }

    [Test]
    [Timeout(60_000)]
    public void Driver_settings_scope_asks_once_per_snapshot_and_again_in_the_next(CancellationToken cancellationToken)
    {
        var root = TempDir();
        CheckpointFixtureProcess.Git(root, "init", "-q");
        CheckpointFixtureProcess.Git(root, "config", "filter.scope.clean", "scope-driver-must-not-run");
        var starts = new List<string[]>();
        void Status()
        {
            var start = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = root };
            foreach (var argument in new[] { "-C", root, "status", "--porcelain" }) start.ArgumentList.Add(argument);
            CheckpointProcessRunner.RunObserved(start, 30_000, true,
                child => starts.Add(child.ArgumentList.ToArray()), 0, cancellationToken).Exit.ShouldBe(0);
        }
        int Lookups() => starts.Count(args => args.Contains("--get-regexp"));

        // One snapshot: the answer is reused, and the driver is still disabled on every read.
        using (CheckpointProcessRunner.DriverSettingsScope()) { Status(); Status(); Status(); }
        Lookups().ShouldBe(1);
        starts.Count(args => args.Contains("status") && args.Contains("filter.scope.clean=")).ShouldBe(3);

        // The next snapshot (after a build that may have changed config) asks again and sees the change.
        starts.Clear();
        CheckpointFixtureProcess.Git(root, "config", "diff.scope.textconv", "scope-textconv-must-not-run");
        using (CheckpointProcessRunner.DriverSettingsScope()) { Status(); Status(); }
        Lookups().ShouldBe(1);
        starts.Count(args => args.Contains("status") && args.Contains("diff.scope.textconv=")).ShouldBe(2);

        // A scope opened inside an active one joins it: one lookup, and closing the inner one keeps the outer.
        starts.Clear();
        using (CheckpointProcessRunner.DriverSettingsScope())
        {
            Status();
            using (CheckpointProcessRunner.DriverSettingsScope()) Status();
            Status();
        }
        Lookups().ShouldBe(1);

        // The same checkout spelled with the other separator is the same answer.
        starts.Clear();
        using (CheckpointProcessRunner.DriverSettingsScope())
        {
            Status();
            var start = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = root.Replace('\\', '/') + "/" };
            foreach (var argument in new[] { "-C", root, "status", "--porcelain" }) start.ArgumentList.Add(argument);
            CheckpointProcessRunner.RunObserved(start, 30_000, true,
                child => starts.Add(child.ArgumentList.ToArray()), 0, cancellationToken).Exit.ShouldBe(0);
        }
        Lookups().ShouldBe(1);

        // Outside any scope every read asks again (the default).
        starts.Clear();
        Status(); Status();
        Lookups().ShouldBe(2);
    }

    [Test]
    public async Task C1616_admitted_build_deadline_preserves_terminal_receipt()
    {
        await AssertAdmittedBuildTimeoutAsync(cancelBudget: false);
    }

    [Test]
    public async Task C1616_admitted_build_budget_cancellation_is_not_slot_refusal()
    {
        await AssertAdmittedBuildTimeoutAsync(cancelBudget: true);
    }

    private async Task AssertAdmittedBuildTimeoutAsync(bool cancelBudget)
    {
        var root = TempDir();
        Directory.CreateDirectory(Path.Combine(root, "tests", "Proof"));
        File.WriteAllText(Path.Combine(root, "tests", "Proof", "p.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var manifest = new CheckpointManifest
        {
            Builds = [new BuildSpec { Id = "bin-c1616-deadline", Project = "tests/Proof", OutputPath = "bin-c1616-deadline/" }],
            Checkpoints = [new CheckpointSpec { Id = "CP-1", Build = "bin-c1616-deadline", Filter = "/*/*/Proof/*", After = ["S1"] }],
        };
        var clock = new ControlledTimeProvider();
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, async (_, token) =>
        {
            if (cancelBudget)
            {
                // Expire the run budget after admission, reaching BuildOneAsync's OCE catch.
                clock.Advance(TimeSpan.FromMinutes(1));
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            return new DriverResult(ExitCodes.Timeout, "", "", Killed: true, TimedOut: true);
        });
        var runtime = new CheckpointApp.Runtime { EnvironmentLookup = _ => null,
            SourceCapture = _ => CheckpointFixtures.CleanSource(), TimeProvider = clock,
            Slots = new FixedSlotClient("off"), Driver = driver, Output = TextWriter.Null };
        var run = CheckpointApp.CreateRun(manifest,
            new RunRequest { Commit = new string('a', 40), Slots = "off", TotalTimeoutMinutes = 1, KeepOutputs = true }, root, runtime);
        (await CheckpointApp.ExecuteAsync(run, CancellationToken.None, runtime)).ShouldBe(5);
        driver.Count(CheckpointFixtures.IsBuild).ShouldBe(1);
        driver.Count(CheckpointFixtures.IsRun).ShouldBe(0);
        driver.InFlight.ShouldBe(0);
        var state = new RunStateStore().TryRead(Path.Combine(run, "state.json"))!;
        var reason = cancelBudget ? "execution-budget-exceeded" : "build-deadline-exceeded";
        state.Builds.Single().TerminalReason.ShouldBe(reason);
        ReportWriter.RequiredReceiptRefusal(run, state).ShouldBeNull();
        var report = ReceiptJson.Read<ReportModel>(Path.Combine(run, "report.json"), ReportWriter.Json);
        report.ExitCode.ShouldBe(5);
        report.Verdict.ShouldBe("RED");
        var row = report.Rows.Single();
        row.State.ShouldBe(cancelBudget ? "skipped" : reason);
        row.Executed.ShouldBeNull("an admitted build timeout has no test execution counts");
        row.Passed.ShouldBeNull();
        row.Failed.ShouldBeNull();
        row.Skipped.ShouldBeNull();
        if (!cancelBudget)
        {
            row.ExitCode.ShouldBe(5);
            row.TerminalReason.ShouldBe(reason);
        }
        ReportValidator.Validate(report, new string('a', 40)).ShouldBe("report_failed");
        (await Antiphon.Checkpoints.Program.RunAsync(["validate", "--evidence", Path.Combine(run, "report.json"),
            "--expected-source-sha", new string('a', 40), "--repo-root", root],
            new CheckpointApp.Runtime { Output = TextWriter.Null })).ShouldBe(2);
        var log = File.ReadAllText(Path.Combine(run, "executor.log"));
        foreach (var phase in new[] { "evidence", "output-discovery", "output-cleanup", "report-publication" })
            log.ShouldContain($"FINALIZATION phase={phase} seconds=");
    }

    [Test]
    public async Task C1616_admission_timeout_publishes_readable_nonexecuted_receipt()
    {
        await using var broker = await BuildSlotBrokerFixture.StartAsync(budget: 1);
        const string endpoint = "http://slots.test/build-slots";
        const string start = "2026-10-30T00:00:00Z";
        var holder = new BuildSlotClient(broker.Handler, endpoint, pid: 100, processStartUtc: start);
        var session = await holder.ProbeAsync(CancellationToken.None);
        await using var held = await holder.AcquireAsync(session, "held-control", CancellationToken.None);
        held.State.ShouldBe("granted");
        // The 1 s admission deadline runs on the same controlled clock as the retry delay. A loaded
        // host (Windows rc b8cbfb24) answered the first queued acquire after a real 1.5 s; real time
        // must not decide whether the deadline fires before the busy answer is observed.
        var clock = new ControlledTimeProvider();
        var queued = new BuildSlotClient(new SlowFirstAcquire(broker.Recording(), TimeSpan.FromMilliseconds(1500)),
            endpoint, pid: 101, processStartUtc: start,
            wait: TimeSpan.FromSeconds(1), timeProvider: clock,
            delay: (_, _) => { clock.Advance(TimeSpan.FromSeconds(1)); return Task.CompletedTask; });
        var root = TempDir();
        Directory.CreateDirectory(Path.Combine(root, "tests", "Proof"));
        var manifest = new CheckpointManifest
        {
            Builds = [new BuildSpec { Id = "bin-c1616", Project = "tests/Proof", OutputPath = "bin-c1616/" }],
            Checkpoints = [new CheckpointSpec { Id = "CP-1", Build = "bin-c1616", Filter = "/*/*/Proof/*", After = ["S1"] }],
        };
        var driver = new FakeDriver();
        var runtime = new CheckpointApp.Runtime { EnvironmentLookup = _ => null,
            SourceCapture = _ => CheckpointFixtures.CleanSource(), Slots = queued, Driver = driver, Output = TextWriter.Null };
        var run = CheckpointApp.CreateRun(manifest, new RunRequest(), root, runtime);
        (await CheckpointApp.ExecuteAsync(run, CancellationToken.None, runtime)).ShouldBe(4);
        driver.Starts.ShouldBeEmpty("admission timeout must precede every build and test driver");
        var state = new RunStateStore().TryRead(Path.Combine(run, "state.json"))!;
        state.Phase.ShouldBe("done");
        state.CreatedOutputPaths.ShouldBeEmpty();
        ReportWriter.RequiredReceiptRefusal(run, state).ShouldBeNull("terminal admission receipt must pass the production reader");
        var report = ReceiptJson.Read<ReportModel>(Path.Combine(run, "report.json"), ReportWriter.Json);
        report.ExitCode.ShouldBe(4);
        report.Verdict.ShouldBe("RED");
        report.OutputNames.ShouldBeEmpty();
        report.OutputPaths.ShouldBeEmpty();
        var row = report.Rows.Single();
        row.State.ShouldBe("slot-timeout");
        row.Executed.ShouldBe(0);
        row.ExitCode.ShouldBe(4);
        row.SlotReason.ShouldBe("build_slot_busy");
        row.TerminalReason.ShouldBe("slot-admission-deadline-exceeded");
        Should.Throw<InvalidOperationException>(() => ReportMerger.Merge([report]), "unexecuted evidence cannot qualify");
        broker.Broker.List().Occupied.ShouldBe(1, "the held control must still own its slot");
    }

    [Test]
    public async Task C1531_inputs_resolve_without_disabling_bounds()
    {
        var root = TempDir();
        var manifestPath = Path.Combine(root, "manifest.yaml");
        File.WriteAllText(manifestPath, """
            schemaVersion: 1
            build:
              slotWaitMinutes: 2
            timeouts:
              hardWallMinutes: 25
            checkpoints:
              - id: CP-1
                after: [S1]
                command: "echo proof"
            """);
        foreach (var name in new[] { "total-timeout", "hard-wall-timeout" })
        foreach (var invalid in new[] { "", " ", "0", "-1m", "banana", "999999999999999999999999999999999999999d" })
            (await Antiphon.Checkpoints.Program.MainAsync(["start", manifestPath, "--" + name, invalid],
                runtimeFactory: _ => new CheckpointApp.Runtime { RepoRoot = root, EnvironmentLookup = _ => null }))
                .ShouldBe(2, name + " must reject " + invalid);
        var options = new Antiphon.Checkpoints.Program.ArgSet();
        options.Positionals.Add(manifestPath);
        options.Add("total-timeout", "10m");
        options.Add("hard-wall-timeout", "30m");
        var selection = ((CheckpointManifest Manifest, RunRequest Request))typeof(Antiphon.Checkpoints.Program)
            .GetMethod("LoadSelection", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [options, root, null, CancellationToken.None])!;
        selection.Request.TotalTimeoutMinutes.ShouldBe(10);
        selection.Request.HardWallTimeoutMinutes.ShouldBe(30);
        selection.Manifest.Timeouts.HardWallMinutes.ShouldBe(25);
        selection.Manifest.Build.SlotWaitMinutes.ShouldBe(2);
        var roundTrip = JsonSerializer.Deserialize<RunRequest>(JsonSerializer.Serialize(selection.Request))!;
        roundTrip.HardWallTimeoutMinutes.ShouldBe(30);
        foreach (var scalar in new[] { "null", "", "\"\"", "0", "-1", "bad", "9999999999999999999" })
            Should.Throw<ManifestValidationException>(() => ManifestLoader.LoadYaml("timeouts:\n  hardWallMinutes: " + scalar, root, validate: false));
        ManifestLoader.LoadYaml("timeouts: {}", root, validate: false).Timeouts.HardWallMinutes.ShouldBeNull();

        var clock = new ControlledTimeProvider();
        var handler = new HeldHttp();
        var client = new BuildSlotClient(handler, "http://slots.test/build-slots",
            wait: TimeSpan.FromMinutes(selection.Manifest.Build.SlotWaitMinutes), timeProvider: clock,
            pid: 123, processStartUtc: "2026-01-01T00:00:00Z");
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var acquire = client.AcquireAsync(new SlotSession("enabled", 1), "proof", watchdog.Token);
        try
        {
            await handler.Entered.Task.WaitAsync(watchdog.Token);
            clock.Advance(TimeSpan.FromMinutes(2));
            await using var lease = await acquire;
            lease.ExitCode.ShouldBe(4);
            lease.WaitedSeconds.ShouldBe(120);
            lease.SlotReason.ShouldBe("slot-admission-deadline-exceeded");
            handler.Exited.ShouldBeTrue();
        }
        finally { watchdog.Cancel(); await acquire; }

        var appClock = new ControlledTimeProvider();
        var appSlots = new HeldSlots();
        var appDriver = new FakeDriver();
        var runtime = new CheckpointApp.Runtime { EnvironmentLookup = _ => null,
            SourceCapture = _ => CheckpointFixtures.CleanSource(), TimeProvider = appClock,
            Slots = appSlots, Driver = appDriver, Output = TextWriter.Null };
        var appRun = CheckpointApp.CreateRun(selection.Manifest, roundTrip, root, runtime);
        using var appStop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var execution = CheckpointApp.ExecuteAsync(appRun, appStop.Token, runtime);
        try
        {
            await appSlots.Entered.Task.WaitAsync(appStop.Token);
            await CheckpointFixtures.WaitUntil(() => appClock.Events.LastOrDefault(e => e.Action == "change")?.DueTime == Timeout.InfiniteTimeSpan);
            appClock.Advance(TimeSpan.FromMinutes(2));
            (await execution).ShouldBe(4);
            var state = new RunStateStore().TryRead(Path.Combine(appRun, "state.json"))!;
            state.Budget!.HardWallLimitSeconds.ShouldBe(1800, "the serialized CLI wall override must reach Execute");
            state.Budget.ExecutionLimitSeconds.ShouldBe(600);
            state.Budget.AdmissionLimitSeconds.ShouldBe(120);
            state.Rows.Single().WaitedSeconds.ShouldBe(120);
            appDriver.Starts.ShouldBeEmpty();
        }
        finally { appStop.Cancel(); await execution; }
    }

    [Test]
    public async Task C1531_receipts_keep_wait_execution_and_counts()
    {
        var root = TempDir();
        var clock = new ControlledTimeProvider();
        var slots = new HeldSlots();
        var driver = new FakeDriver();
        var manifest = new CheckpointManifest
        {
            Checkpoints = [new CheckpointSpec { Id = "CP-1", Command = "echo proof", After = ["S1"] }],
        };
        var runtime = new CheckpointApp.Runtime { EnvironmentLookup = _ => null, SourceCapture = _ => CheckpointFixtures.CleanSource(),
            Driver = driver, Slots = slots, TimeProvider = clock, Output = TextWriter.Null };
        var run = CheckpointApp.CreateRun(manifest, new RunRequest { TotalTimeoutMinutes = 10, HardWallTimeoutMinutes = 30, KeepOutputs = true }, root, runtime);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var execute = CheckpointApp.ExecuteAsync(run, cancel.Token, runtime);
        try
        {
            await slots.Entered.Task.WaitAsync(cancel.Token);
            await CheckpointFixtures.WaitUntil(() => clock.Events.LastOrDefault(e => e.Action == "change")?.DueTime == Timeout.InfiniteTimeSpan);
            clock.Advance(TimeSpan.FromMinutes(3));
            cancel.Cancel();
            (await execute).ShouldBe(130);
            var report = JsonSerializer.Deserialize<ReportModel>(File.ReadAllText(Path.Combine(run, "report.json")), ReportWriter.Json)!;
            report.Rows.Single().WaitedSeconds.ShouldBe(180);
            report.Rows.Single().ExecutedSeconds.ShouldBe(0);
            report.Budget!.AdmissionOnlySeconds.ShouldBe(180);
            report.ExitCode.ShouldBe(130);
            File.ReadAllText(Path.Combine(run, "report.md")).ShouldContain("CHECKPOINT-TIMING CP-1 waited=180s executed=0.000s wall=180.000s");
            driver.Starts.ShouldBeEmpty();
        }
        finally { cancel.Cancel(); await execute; }

        // Actual TRX failure/count projection is separate from elapsed seconds.
        var builtRoot = TempDir();
        Directory.CreateDirectory(Path.Combine(builtRoot, "tests", "Proof"));
        var builtManifest = new CheckpointManifest
        {
            Builds = [new BuildSpec { Id = "bin-proof", Project = "tests/Proof", OutputPath = "bin-proof/" }],
            Checkpoints = [new CheckpointSpec { Id = "CP-1", Build = "bin-proof", Filter = "/*/*/Proof/*", After = ["S1"] }],
        };
        var failed = new FakeDriver();
        failed.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), ("N.Proof.assertion", "Failed"), ("N.Proof.control", "Passed"));
            return Task.FromResult(new DriverResult(1, "", ""));
        });
        var failedRuntime = new CheckpointApp.Runtime { EnvironmentLookup = _ => null, SourceCapture = _ => CheckpointFixtures.CleanSource(),
            Driver = failed, Slots = new FixedSlotClient("off"), Output = TextWriter.Null };
        var failedRun = CheckpointApp.CreateRun(builtManifest, new RunRequest { KeepOutputs = true }, builtRoot, failedRuntime);
        (await CheckpointApp.ExecuteAsync(failedRun, CancellationToken.None, failedRuntime)).ShouldBe(1);
        var failure = JsonSerializer.Deserialize<ReportModel>(File.ReadAllText(Path.Combine(failedRun, "report.json")), ReportWriter.Json)!;
        failure.Rows.Single().Executed.ShouldBe(2);
        failure.Rows.Single().Failed.ShouldBe(1);
        failure.Rows.Single().Line.ShouldContain("executed=2 passed=1 failed=1");
        failure.Rows.Single().ExecutedSeconds.ShouldNotBeNull();
    }

    [Test]
    public void C1531_legacy_receipts_remain_unknown_not_zero()
    {
        foreach (var schema in new[] { 2, 3 })
        {
            var legacy = JsonSerializer.Deserialize<ReportModel>($"{{\"schemaVersion\":{schema},\"exitCode\":5,\"cleanupRefusals\":[\"still-present\"],\"rows\":[{{\"id\":\"CP-1\",\"exitCode\":5,\"waitedSeconds\":180}}]}}", ReportWriter.Json)!;
            legacy.Rows.Single().ExecutedSeconds.ShouldBeNull();
            legacy.Rows.Single().WallSeconds.ShouldBeNull();
            legacy.Budget.ShouldBeNull();
            var restored = JsonSerializer.Deserialize<ReportModel>(ReportWriter.JsonText(legacy), ReportWriter.Json)!;
            restored.Rows.Single().ExecutedSeconds.ShouldBeNull();
            restored.Rows.Single().WaitedSeconds.ShouldBe(180);
            restored.ExitCode.ShouldBe(5);
            ReportValidator.Validate(restored, new string('a', 40)).ShouldBe("cleanup_refused");
        }
        var now = DateTimeOffset.UtcNow;
        ReportModel Attempt(int exit, double execution)
        {
            var source = new SourceEvidence { Start = CheckpointFixtures.CleanSource(), End = CheckpointFixtures.CleanSource(), State = "clean", BuildSource = "verified" };
            return new ReportModel { Commit = new string('a', 40), Source = source, EndedAt = now.AddMinutes(execution), ExitCode = exit,
                Rows = [new ReportRow { Id = "CP-1", Source = source, ExitCode = exit, ExecutedSeconds = execution, WallSeconds = execution + 180, WaitedSeconds = 180 }] };
        }
        var merged = ReportMerger.Merge([Attempt(5, 1), Attempt(0, 2)]);
        merged.Attempts.Count.ShouldBe(2);
        merged.Attempts[0].ExitCode.ShouldBe(5);
        merged.Attempts[0].Rows.Single().ExecutedSeconds.ShouldBe(1);
        merged.Rows.Single().ExecutedSeconds.ShouldBe(2);
        foreach (var invalid in new[] { -1d, double.NaN, double.PositiveInfinity })
        {
            var malformed = Attempt(0, 2);
            malformed.Budget = new RunBudgetEvidence { AdmissionLimitSeconds = invalid };
            ReportValidator.Validate(malformed, new string('a', 40)).ShouldBe("invalid_timing");
        }
    }

    private sealed class HeldHttp : HttpMessageHandler
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Exited { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return new HttpResponseMessage(HttpStatusCode.OK); }
            finally { Exited = true; }
        }
    }
    /// <summary>Answers the first acquire POST only after a real delay, as a loaded host does.</summary>
    private sealed class SlowFirstAcquire(HttpMessageHandler inner, TimeSpan delay) : DelegatingHandler(inner)
    {
        private int _acquires;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/build-slots"
                && Interlocked.Increment(ref _acquires) == 1)
                await Task.Delay(delay, ct);
            return await base.SendAsync(request, ct);
        }
    }
    private sealed class HeldSlots : IBuildSlotClient
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<SlotSession> ProbeAsync(CancellationToken ct) => Task.FromResult(new SlotSession("enabled", 1));
        public async Task<SlotLease> AcquireAsync(SlotSession session, string label, CancellationToken ct)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new SlotLease();
        }
    }
}
