using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

// CARD-1241: files outside the write roots are stamped before the build slot, re-checked once it is
// granted and verified after its release; inside the slot the build stamps its write roots and what
// the ledger does not hold.
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointConfinementLedgerTests : CheckpointTestBase
{
    private const int IgnoredFiles = 2_000;
    private static readonly string[] Arguments = ["build", "App/App.csproj", "--property:OutputPath=bin-probe/"];
    private static readonly string Sha = new('a', 40);

    [Test]
    [Timeout(300_000)]
    public Task ignored_tree_outside_write_roots_is_stamped_before_the_slot_not_inside_it(CancellationToken cancellationToken)
    {
        // red: Prepare ignores the ledger and stamps the 2,000-file ignored tree inside the slot again.
        var root = BeginRepo();
        var ledger = Capture(root);
        ledger.Files.Keys.Count(path => path.StartsWith("node_modules/", StringComparison.Ordinal)).ShouldBe(IgnoredFiles);
        ledger.Files.ShouldContainKey("App/Marker.cs");
        ledger.Files.ShouldContainKey("notes.txt");
        ledger.Files.ShouldNotContainKey("App/obj/stale.txt");

        var inSlot = Prepare(root, ledger, cancellationToken);
        inSlot.Untracked.Keys.ShouldNotContain(path => path.StartsWith("node_modules/", StringComparison.Ordinal));
        inSlot.Untracked.ShouldNotContainKey("notes.txt");
        inSlot.Untracked.ShouldContainKey("App/obj/stale.txt");
        inSlot.Content.ShouldBeEmpty();

        // Without a ledger the build keeps today's full stamp, so the fixture tree is reachable.
        var unledgered = Prepare(root, null, cancellationToken);
        unledgered.Untracked.Keys.Count(path => path.StartsWith("node_modules/", StringComparison.Ordinal)).ShouldBe(IgnoredFiles);
        unledgered.Content.ShouldContainKey("App/Marker.cs");
        return Task.CompletedTask;
    }

    [Test]
    [Timeout(300_000)]
    public Task tracked_file_under_a_write_root_is_stamped_inside_the_slot_not_in_the_ledger(CancellationToken cancellationToken)
    {
        // red: the ledger takes tracked paths under the write roots too (Review 06ff09f8 D2 scope), or the
        // in-slot capture drops them, so a tracked obj/ file is stamped in neither place or in both.
        var root = BeginRepo();
        Write(root, "App/obj/tracked.txt", "force-added under a write root\n");
        Git(root, "add", "-f", "App/obj/tracked.txt");
        Git(root, "-c", "user.email=card-1241@example.invalid", "-c", "user.name=card-1241",
            "-c", "commit.gpgsign=false", "commit", "-m", "tracked under obj", "--no-verify");
        var ledger = Capture(root);
        ledger.Tracked.ShouldContain("App/obj/tracked.txt");
        ledger.Files.ShouldNotContainKey("App/obj/tracked.txt");
        ledger.Files.ShouldContainKey("App/Marker.cs");

        var inSlot = Prepare(root, ledger, cancellationToken);
        inSlot.Content.Keys.ToArray().ShouldBe(new[] { "App/obj/tracked.txt" });
        return Task.CompletedTask;
    }

    [Test]
    [Timeout(300_000)]
    public Task changed_file_under_an_output_root_is_still_refused_inside_the_slot(CancellationToken cancellationToken)
    {
        // red: the in-slot baseline drops write-root files, so the changed obj/ file passes.
        var root = BeginRepo();
        var ledger = Capture(root);
        var baseline = Prepare(root, ledger, cancellationToken);
        File.WriteAllText(Path.Combine(root, "App", "obj", "stale.txt"), "rewritten by the build");

        var settled = BuildConfinement.Settle(baseline);
        settled.Kind.ShouldBe("tampered", settled.Detail);
        settled.Paths.ShouldContain("App/obj/stale.txt");
        settled.Detail.ShouldContain("TAMPERED App/obj/stale.txt", Case.Sensitive);
        BuildConfinement.VerifyLedger(ledger).Kind.ShouldBeNull("the write root is checked inside the slot, not by the ledger");
        return Task.CompletedTask;
    }

    [Test]
    [Timeout(300_000)]
    public Task untracked_source_file_added_during_the_build_is_still_refused(CancellationToken cancellationToken)
    {
        // red: Settle treats a path absent from the in-slot baseline as covered by the ledger.
        var root = BeginRepo();
        var ledger = Capture(root);
        var baseline = Prepare(root, ledger, cancellationToken);
        File.WriteAllText(Path.Combine(root, "App", "Extra.cs"), "namespace App; public static class Extra { }\n");

        var settled = BuildConfinement.Settle(baseline);
        settled.Kind.ShouldBe("tampered", settled.Detail);
        settled.Paths.ToArray().ShouldBe(new[] { "App/Extra.cs" }, settled.Detail);
        return Task.CompletedTask;
    }

    [Test]
    [Timeout(120_000)]
    public Task non_output_file_changed_before_slot_release_is_refused()
    {
        // red: the ledger is not re-stamped after release, so ignored, untracked and tracked changes pass.
        var root = BeginRepo();
        var ledger = Capture(root);
        File.WriteAllText(Path.Combine(root, "node_modules", "pkg", "f0007.js"), "changed\n");
        File.Delete(Path.Combine(root, "notes.txt"));
        File.AppendAllText(Path.Combine(root, "App", "Marker.cs"), "// changed\n");

        var verified = BuildConfinement.VerifyLedger(ledger);
        verified.Kind.ShouldBe("tampered");
        verified.Paths.ToArray().ShouldBe(new[] { "App/Marker.cs", "node_modules/pkg/f0007.js", "notes.txt" }, verified.Detail);
        verified.Detail.ShouldContain("restore --source=" + ledger.Head + " --staged --worktree -- 'App/Marker.cs'", Case.Sensitive);
        return Task.CompletedTask;
    }

    [Test]
    [Timeout(300_000)]
    public async Task scheduler_stamps_non_output_files_before_the_slot_and_verifies_after_release(CancellationToken cancellationToken)
    {
        // red: the ledger is taken inside the lease (capture sees Held) or verified before release (f0002 passes).
        // The build itself writes f0001: a write during the slot wait is now refused before launch (next test).
        var root = BeginRepo();
        var slots = new RecordingSlotClient(
            onAcquire: () => { },
            onRelease: () => File.WriteAllText(Path.Combine(root, "node_modules", "pkg", "f0002.js"), "changed at release\n"));
        var driver = new LedgerDriver(slots, () => File.WriteAllText(Path.Combine(root, "node_modules", "pkg", "f0001.js"), "changed by the build\n"));
        var (run, exit) = await RunScheduledAsync(root, driver, slots, cancellationToken);

        driver.LedgerCaptures.ToArray().ShouldBe(new[] { false }, "the ledger is stamped once, before the slot is held");
        driver.BuildLedgers.ToArray().ShouldBe(new[] { true }, "the build receives the ledger");
        exit.ShouldNotBe(0);
        var report = JsonSerializer.Deserialize<ReportModel>(File.ReadAllText(Path.Combine(run, "report.json")), ReportWriter.Json)!;
        var build = report.Builds.Single();
        build.State.ShouldBe("failed");
        build.SlotReason.ShouldBe("refused: tampered");
        var log = File.ReadAllText(Path.Combine(run, "builds", "bin-c1241", "build.log"));
        log.ShouldContain("TAMPERED node_modules/pkg/f0001.js", Case.Sensitive);
        log.ShouldContain("TAMPERED node_modules/pkg/f0002.js", Case.Sensitive);
        report.Rows.Single().State.ShouldNotBe("green");
    }

    [Test]
    [Timeout(300_000)]
    public async Task file_changed_during_the_slot_wait_and_restored_by_the_build_is_refused_before_launch(CancellationToken cancellationToken)
    {
        // red (Review 06ff09f8 D1): A -> B while the build waits for its slot, then the build writes A back; the
        // after-release check compares A with the pre-slot A and the build succeeds over the user's newer B.
        var root = BeginRepo();
        var user = Path.Combine(root, "node_modules", "pkg", "f0003.js");
        var original = File.ReadAllText(user);
        var slots = new RecordingSlotClient(
            onAcquire: () => File.WriteAllText(user, "newer user bytes written during the slot wait\n"),
            onRelease: () => { });
        var driver = new LedgerDriver(slots, () => File.WriteAllText(user, original));
        var (run, exit) = await RunScheduledAsync(root, driver, slots, cancellationToken);

        driver.LedgerCaptures.ToArray().ShouldBe(new[] { false });
        driver.BuildLedgers.ShouldBeEmpty("the build is refused once the slot is granted, before it starts");
        File.ReadAllText(user).ShouldBe("newer user bytes written during the slot wait\n");
        exit.ShouldNotBe(0);
        var report = JsonSerializer.Deserialize<ReportModel>(File.ReadAllText(Path.Combine(run, "report.json")), ReportWriter.Json)!;
        var build = report.Builds.Single();
        build.State.ShouldBe("failed");
        build.SlotReason.ShouldBe("refused: tampered");
        var log = File.ReadAllText(Path.Combine(run, "builds", "bin-c1241", "build.log"));
        log.ShouldContain("TAMPERED node_modules/pkg/f0003.js", Case.Sensitive);
        report.Rows.Single().State.ShouldNotBe("green");
    }

    [Test]
    [Timeout(300_000)]
    public Task unchanged_ledger_entries_are_not_hashed_when_the_slot_is_granted()
    {
        // red: admission hashes each ledger entry again instead of the entries whose stat stamp changed.
        var root = BeginRepo();
        AgeWorktree(root);
        var ledger = Capture(root);
        ledger.Files.Count.ShouldBeGreaterThan(IgnoredFiles);

        var unchanged = BuildConfinement.AdmitLedger(ledger);
        unchanged.Settlement.Kind.ShouldBeNull(unchanged.Settlement.Detail);
        unchanged.Hashed.ShouldBe(0, "no ledger entry changed, so none is hashed inside the slot");

        File.WriteAllText(Path.Combine(root, "node_modules", "pkg", "f0007.js"), "changed while waiting\n");
        var changed = BuildConfinement.AdmitLedger(ledger);
        changed.Hashed.ShouldBe(1, "only the entry whose stat stamp changed is hashed");
        changed.Settlement.Kind.ShouldBe("tampered");
        changed.Settlement.Paths.ToArray().ShouldBe(new[] { "node_modules/pkg/f0007.js" }, changed.Settlement.Detail);
        return Task.CompletedTask;
    }

    [Test]
    [Timeout(300_000)]
    public Task entry_modified_just_before_the_ledger_is_hashed_when_the_slot_is_granted()
    {
        // red: an entry modified within the racy window is skipped on an equal stat stamp, although a second
        // write in the same timestamp tick would leave size, inode and timestamps equal.
        var root = BeginRepo();
        AgeWorktree(root);
        File.WriteAllText(Path.Combine(root, "notes.txt"), "user notes written just before the ledger\n");
        var ledger = Capture(root);

        var admitted = BuildConfinement.AdmitLedger(ledger);
        admitted.Hashed.ShouldBe(1, "only the recently modified entry is hashed");
        admitted.Settlement.Kind.ShouldBeNull(admitted.Settlement.Detail);
        return Task.CompletedTask;
    }

    private static async Task<(string Run, int Exit)> RunScheduledAsync(
        string root, LedgerDriver driver, RecordingSlotClient slots, CancellationToken cancellationToken)
    {
        var source = new SourceObservation(Sha, 0, new string('1', 64), DateTimeOffset.UtcNow, "known");
        var manifest = new CheckpointManifest
        {
            Builds = [new BuildSpec { Id = "bin-c1241", Project = "App/App.csproj", OutputPath = "bin-c1241/" }],
            Checkpoints = [new CheckpointSpec
            {
                Id = "CP-1", After = ["all"], Build = "bin-c1241",
                Filter = "/*/*/ExampleSurfaceTests/*", MinExecuted = 1, EstimatedMinutes = 1,
            }],
        };
        var run = CheckpointApp.CreateRun(manifest, new RunRequest
        {
            Commit = Sha, Branch = "fixture", KeepOutputs = true, Slots = "off",
        }, root, new CheckpointApp.Runtime { SourceCapture = _ => source });

        var exit = await CheckpointApp.ExecuteAsync(run, cancellationToken, new CheckpointApp.Runtime
        {
            Driver = driver, Slots = slots, SourceCapture = _ => source,
        });
        return (run, exit);
    }

    // Moves the modification time of the worktree files outside .git an hour back, outside the racy window.
    private static void AgeWorktree(string root)
    {
        var old = DateTime.UtcNow.AddHours(-1);
        var git = Path.Combine(root, ".git") + Path.DirectorySeparatorChar;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            if (!file.StartsWith(git, StringComparison.Ordinal))
                File.SetLastWriteTimeUtc(file, old);
    }

    private string BeginRepo()
    {
        var root = TempDir();
        Git(root, "init", "-b", "card1241");
        Write(root, ".gitignore", "bin-*/\nobj/\nnode_modules/\n.antiphon/\n.checkpoint-test-root.json\n");
        Write(root, "App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <DefaultItemExcludes>$(DefaultItemExcludes);bin-*/**</DefaultItemExcludes>
              </PropertyGroup>
            </Project>
            """);
        Write(root, "App/Marker.cs", "namespace App; public static class Marker { public static int Value => 1; }\n");
        Git(root, "add", "-A");
        Git(root, "-c", "user.email=card-1241@example.invalid", "-c", "user.name=card-1241",
            "-c", "commit.gpgsign=false", "commit", "-m", "fixture", "--no-verify");
        for (var i = 0; i < IgnoredFiles; i++)
            Write(root, $"node_modules/pkg/f{i:D4}.js", "module.exports = " + i + ";\n");
        Write(root, "App/obj/stale.txt", "pre-existing intermediate\n");
        Write(root, "notes.txt", "user notes, untracked\n");
        return root;
    }

    private static BuildConfinement.Ledger Capture(string root)
    {
        var captured = BuildConfinement.CaptureLedger(root, Arguments);
        captured.Refusal.ShouldBeNull(captured.Detail);
        return captured.Value.ShouldNotBeNull();
    }

    private static BuildConfinement.Baseline Prepare(string root, BuildConfinement.Ledger? ledger, CancellationToken cancellationToken)
    {
        var prepared = BuildConfinement.Prepare(root, Arguments, null, cancellationToken, ledger);
        prepared.Refusal.ShouldBeNull(prepared.Detail);
        return prepared.Baseline.ShouldNotBeNull();
    }

    private static void Write(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void Git(string root, params string[] args) => _ = CheckpointFixtureProcess.Git(root, args);

    private sealed class RecordingSlotClient(Action onAcquire, Action onRelease) : IBuildSlotClient
    {
        public bool Held { get; private set; }

        public Task<SlotSession> ProbeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SlotSession("granted", 4));

        public Task<SlotLease> AcquireAsync(SlotSession session, string label, CancellationToken cancellationToken)
        {
            onAcquire();
            Held = true;
            return Task.FromResult(new SlotLease
            {
                State = "granted",
                MaxCpuCount = 4,
                ReleaseAsync = _ =>
                {
                    Held = false;
                    onRelease();
                    return Task.CompletedTask;
                },
            });
        }
    }

    // Uses the real ledger capture around a fake build; the build runs no Prepare/Settle, so changes reach the report through the scheduler's ledger checks.
    private sealed class LedgerDriver(RecordingSlotClient slots, Action onBuild) : IConfinedDriver
    {
        private readonly FakeDriver _inner = new();
        public List<bool> LedgerCaptures { get; } = [];
        public List<bool> BuildLedgers { get; } = [];

        public BuildConfinement.LedgerCapture CaptureLedger(DriverRequest request)
        {
            LedgerCaptures.Add(slots.Held);
            return BuildConfinement.CaptureLedger(request.WorkingDirectory, request.Arguments);
        }

        public Task<DriverResult> RunAsync(DriverRequest request, CancellationToken cancellationToken)
        {
            if (CheckpointFixtures.IsBuild(request))
            {
                BuildLedgers.Add(request.Ledger is not null);
                onBuild();
            }
            return _inner.RunAsync(request, cancellationToken);
        }

        public void Kill(bool entireProcessTree) => _inner.Kill(entireProcessTree);
    }
}
