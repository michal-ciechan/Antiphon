using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointSelectedOutputTests : CheckpointTestBase
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task selected_run_excludes_earlier_commit_unused_outputs(bool keepOutputs)
    {
        var repo = Repository();
        var manifest = Manifest();
        var sourceA = Observe(repo);
        var outputA = Path.Combine(repo, "sample", "bin-c1297-a");
        var sentinel = Path.Combine(outputA, "sentinel.txt");
        var driverA = Driver("EarlierOutputTests");
        driverA.When(CheckpointFixtures.IsBuild, (_, _) =>
        {
            SafeWrite.Text(sentinel, "earlier build sentinel", SafeWrite.Under(outputA)).ShouldBeNull();
            return Task.FromResult(new DriverResult(0, "", "") { Exited = CheckpointFixtures.AfterFileClock(sentinel) });
        });
        var earlier = await ExecuteAsync(repo, manifest, "CP-A", sourceA, true, driverA);
        AssertGreen(earlier, sourceA, "CP-A");
        var stampA = Path.Combine(outputA, "checkpoint-build-source.json");
        var stampBytes = File.ReadAllBytes(stampA);
        var sentinelBytes = File.ReadAllBytes(sentinel);

        CommitSelection(repo, "B");
        var sourceB = Observe(repo);
        sourceB.Commit.ShouldNotBe(sourceA.Commit);
        sourceB.Fingerprint.ShouldNotBe(sourceA.Fingerprint);
        var outputB = Path.Combine(repo, "sample", "bin-c1297-b");
        var dependencyB = Path.Combine(repo, "dependency", "bin-c1297-b");
        var driverB = Driver("SelectedOutputTests");
        driverB.When(CheckpointFixtures.IsBuild, (_, _) =>
        {
            SafeWrite.CreateFreshDirectory(dependencyB, SafeWrite.Under(dependencyB)).ShouldBeNull();
            var dependencySentinel = Path.Combine(dependencyB, "dependency.txt");
            SafeWrite.Text(dependencySentinel, "selected dependency", SafeWrite.Under(dependencyB)).ShouldBeNull();
            CheckpointBuildBinding.Expected(repo, "dependency", "bin-c1297-b/", [], sourceB)
                .Write(CheckpointFixtures.AfterFileClock(dependencySentinel)).ShouldBeNull();
            return Task.FromResult(new DriverResult(0, "", "")
            {
                Exited = CheckpointFixtures.AfterFileClock(dependencySentinel),
            });
        });
        var selected = await ExecuteAsync(repo, manifest, "CP-B", sourceB, keepOutputs, driverB);

        // These behavioral verdicts expose the defect before secondary inventory assertions.
        if (keepOutputs)
            selected.WaitExit.ShouldBe(0, "selected-retained-wait: " + selected.WaitText);
        else
            selected.ExecuteExit.ShouldBe(0, "selected-cleanup-exit: " + selected.Report.Reason);
        AssertGreen(selected, sourceB, "CP-B");
        selected.Report.OutputPaths.Order(StringComparer.Ordinal).ShouldBe(
            new[] { outputB, dependencyB }.Order(StringComparer.Ordinal), "selected-output-set");
        selected.Report.OutputNames.ShouldBe(["bin-c1297-b/"]);
        selected.Report.Builds.Single(build => build.Id == "bin-c1297-a").State.ShouldBe("unused");
        driverB.Calls.Count.ShouldBe(2, "only the selected build and row launch");
        driverB.Count(CheckpointFixtures.IsBuild).ShouldBe(1);
        driverB.Count(CheckpointFixtures.IsRun).ShouldBe(1);
        driverB.Calls.ShouldNotContain(call => call.Arguments.Any(arg =>
            arg.Contains("bin-c1297-a", StringComparison.Ordinal)
            || arg.Contains("EarlierOutputTests", StringComparison.Ordinal)));
        Directory.Exists(outputA).ShouldBeTrue("earlier-output-present");
        File.ReadAllBytes(stampA).ShouldBe(stampBytes, "earlier-stamp-unchanged");
        File.ReadAllBytes(sentinel).ShouldBe(sentinelBytes, "earlier-sentinel-unchanged");
        foreach (var path in new[] { outputB, dependencyB })
        {
            Directory.Exists(path).ShouldBe(keepOutputs);
            if (keepOutputs)
            {
                var binding = JsonSerializer.Deserialize<CheckpointBuildBinding>(
                    File.ReadAllBytes(Path.Combine(path, "checkpoint-build-source.json")), CheckpointApp.Json)!;
                binding.Commit.ShouldBe(sourceB.Commit);
                binding.Fingerprint.ShouldBe(sourceB.Fingerprint);
                binding.SourceState.ShouldBe("clean");
            }
        }
        selected.Report.CleanedOutputs.ShouldBe(!keepOutputs);
    }

    [Test]
    public async Task selected_retained_output_stamp_mismatch_is_still_refused()
    {
        var repo = Repository();
        var otherCommit = Observe(repo).Commit!;
        CommitSelection(repo, "B");
        var source = Observe(repo);
        var result = await ExecuteAsync(repo, Manifest(), "CP-B", source, true, Driver("SelectedOutputTests"));
        AssertGreen(result, source, "CP-B");
        var stamp = Path.Combine(repo, "sample", "bin-c1297-b", "checkpoint-build-source.json");
        var original = File.ReadAllBytes(stamp);
        try
        {
            foreach (var scenario in new[] { "commit", "fingerprint", "missing" })
            {
                var binding = JsonSerializer.Deserialize<CheckpointBuildBinding>(original, CheckpointApp.Json)!;
                if (scenario == "commit")
                    binding.Commit = otherCommit;
                else if (scenario == "fingerprint")
                    binding.Fingerprint = Convert.ToHexString(
                        System.Security.Cryptography.SHA256.HashData(original)).ToLowerInvariant();
                if (scenario == "fingerprint")
                    binding.Fingerprint.ShouldNotBe(source.Fingerprint, "independent wrong fingerprint");
                if (scenario == "missing")
                    File.Delete(stamp);
                else
                    File.WriteAllText(stamp, JsonSerializer.Serialize(binding, CheckpointApp.Json));

                var refused = await WaitAsync(result.Run, source.Commit!);
                refused.Exit.ShouldBe(2, "selected-" + scenario + "-refused");
                refused.Text.ShouldContain((scenario == "missing"
                    ? "receipt-missing: " : "receipt-source-mismatching: ") + stamp);
                File.WriteAllBytes(stamp, original);
                var restored = await WaitAsync(result.Run, source.Commit!);
                restored.Exit.ShouldBe(0, scenario + " restoration: " + restored.Text);
            }
        }
        finally
        {
            File.WriteAllBytes(stamp, original);
        }
    }

    private string Repository()
    {
        var repo = TempDir();
        CheckpointFixtureProcess.Git(repo, "init", "-b", "fixture");
        CheckpointFixtureProcess.Git(repo, "config", "user.name", "Checkpoint fixture");
        CheckpointFixtureProcess.Git(repo, "config", "user.email", "checkpoint@example.invalid");
        File.WriteAllText(Path.Combine(repo, ".gitignore"),
            "bin-*/\n.antiphon/\n" + TestRootGuard.MarkerName + "\n"
            + TestRootGuard.LockName + "\n" + TestRootGuard.RunsName + "\n");
        foreach (var project in new[] { "sample", "dependency" })
        {
            Directory.CreateDirectory(Path.Combine(repo, project));
            File.WriteAllText(Path.Combine(repo, project, project + ".csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        }
        CommitSelection(repo, "A");
        return repo;
    }

    private static void CommitSelection(string repo, string selection)
    {
        File.WriteAllText(Path.Combine(repo, "selection.txt"), selection);
        CheckpointFixtureProcess.Git(repo, "add", ".");
        CheckpointFixtureProcess.Git(repo, "commit", "-m", "Select " + selection);
    }

    private static SourceObservation Observe(string repo)
    {
        var observation = new SourceSnapshot().Capture(repo);
        observation.CaptureStatus.ShouldBe("known", observation.ErrorCode);
        observation.DirtyFiles.ShouldBe(0);
        observation.Commit.ShouldBe(CheckpointFixtureProcess.Git(repo, "rev-parse", "HEAD").Trim());
        return observation;
    }

    private static CheckpointManifest Manifest() => new()
    {
        Builds =
        [
            new BuildSpec { Id = "bin-c1297-a", Project = "sample", OutputPath = "bin-c1297-a/" },
            new BuildSpec { Id = "bin-c1297-b", Project = "sample", OutputPath = "bin-c1297-b/" },
        ],
        Checkpoints =
        [
            new CheckpointSpec
            {
                Id = "CP-A", After = ["all"], Build = "bin-c1297-a",
                Filter = "/*/*/EarlierOutputTests/*", Expect = ["EarlierOutputTests"], MinExecuted = 1, EstimatedMinutes = 1,
            },
            new CheckpointSpec
            {
                Id = "CP-B", After = ["all"], Build = "bin-c1297-b",
                Filter = "/*/*/SelectedOutputTests/*", Expect = ["SelectedOutputTests"], MinExecuted = 1, EstimatedMinutes = 1,
            },
        ],
    };

    private static FakeDriver Driver(string className)
    {
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            var trx = CheckpointFixtures.TrxFile(request);
            CheckpointFixtures.WriteResults(trx, ("Antiphon.Tests." + className + ".case", "Passed"));
            return Task.FromResult(new DriverResult(0, "", "") { Exited = CheckpointFixtures.AfterFileClock(trx) });
        });
        return driver;
    }

    private sealed record Execution(string Run, int ExecuteExit, RunState State, ReportModel Report, int WaitExit, string WaitText);

    private static async Task<Execution> ExecuteAsync(string repo, CheckpointManifest manifest, string row,
        SourceObservation source, bool keepOutputs, FakeDriver driver)
    {
        var runtime = new CheckpointApp.Runtime
        {
            Driver = driver, Slots = new FixedSlotClient("off"), EnvironmentLookup = _ => null,
        };
        var run = CheckpointApp.CreateRun(manifest, new RunRequest
        {
            Commit = source.Commit!, ExpectedSourceSha = source.Commit, Branch = "fixture",
            Rows = [row], KeepOutputs = keepOutputs, Slots = "off", Serial = true,
        }, repo, runtime);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var exit = await CheckpointApp.ExecuteAsync(run, deadline.Token, runtime);
        var report = JsonSerializer.Deserialize<ReportModel>(
            File.ReadAllBytes(Path.Combine(run, "report.json")), ReportWriter.Json)!;
        var state = new RunStateStore().TryRead(Path.Combine(run, "state.json"))!;
        // ExecuteAsync has joined all fixture work; only executor liveness is substituted.
        ExecutorOwnershipStore.Write(run, new ProcessIdentityProbe().Current() with { Pid = int.MaxValue, StartUtcTicks = 1 });
        var waited = await WaitAsync(run, source.Commit!);
        Console.WriteLine("C1297 nested receipt: " + ReportWriter.JsonText(report));
        return new Execution(run, exit, state, report, waited.Exit, waited.Text);
    }

    private static async Task<(int Exit, string Text)> WaitAsync(string run, string sha)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var output = new StringWriter();
        var exit = await new WaitCommand().WaitAsync(run, TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(60), output, deadline.Token, sha);
        Console.WriteLine("C1297 wait: " + output);
        return (exit, output.ToString());
    }

    private static void AssertGreen(Execution execution, SourceObservation source, string rowId)
    {
        execution.ExecuteExit.ShouldBe(0, execution.Report.Reason);
        execution.State.Phase.ShouldBe("done");
        execution.State.ExitCode.ShouldBe(0);
        execution.WaitExit.ShouldBe(0, execution.WaitText);
        execution.Report.ExitCode.ShouldBe(0);
        execution.Report.CleanupRefusals.ShouldBeEmpty();
        execution.Report.Source.Start.Commit.ShouldBe(source.Commit);
        execution.Report.Source.Start.Fingerprint.ShouldBe(source.Fingerprint);
        execution.Report.Source.State.ShouldBe("clean");
        ReportValidator.Validate(execution.Report, source.Commit!).ShouldBeNull();
        var row = execution.Report.Rows.ShouldHaveSingleItem();
        row.Id.ShouldBe(rowId);
        row.Executed.ShouldBe(1);
        row.Passed.ShouldBe(1);
        row.Failed.ShouldBe(0);
        row.Skipped.ShouldBe(0);
        row.Source.State.ShouldBe("clean");
        row.Source.BuildSource.ShouldBe("verified");
        row.Source.Start.Commit.ShouldBe(source.Commit);
    }
}
