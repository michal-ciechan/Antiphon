using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointDiskAdmissionTests : CheckpointTestBase
{
    [Test]
    public async Task RowRechecksSpaceAfterAdmissionAndReleasesTheLease()
    {
        var root = TempDir();
        Directory.CreateDirectory(Path.Combine(root, "sample"));
        var driver = RecordingDriver();
        var slot = new DiskDropsAtGrant();
        using var output = new StringWriter();
        var exit = await Antiphon.Checkpoints.Program.RunAsync([
            "row", "--repo-root", root, "--name", "CP-1", "--project", "sample",
            "--output-path", "bin-disk/", "--filter", "/*/*/Test/*",
        ], new CheckpointApp.Runtime
        {
            Output = output,
            Driver = driver,
            Slots = slot,
            SourceCapture = _ => CheckpointFixtures.CleanSource(),
            DiskFreeBytes = slot.FreeBytes,
        });
        driver.Calls.ShouldBeEmpty("disk refusal precedes the build and the test host");
        exit.ShouldBe(ExitCodes.DiskLow, output.ToString());
        output.ToString().ShouldContain("reason=workspace_disk_low");
        output.ToString().ShouldContain("slot=granted waited=840s");
        slot.Released.ShouldBe(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SchedulerRechecksSpaceAfterAdmissionAndReleasesTheLease(bool command)
    {
        var root = TempDir();
        Directory.CreateDirectory(Path.Combine(root, "sample"));
        var manifest = new CheckpointManifest();
        if (!command)
            manifest.Builds.Add(new BuildSpec { Id = "bin-disk", Project = "sample", OutputPath = "bin-disk/" });
        manifest.Checkpoints.Add(command
            ? new CheckpointSpec { Id = "CP-1", Command = "fixture-command" }
            : new CheckpointSpec { Id = "CP-1", Build = "bin-disk", Filter = "/*/*/Test/*" });
        var driver = RecordingDriver();
        var slot = new DiskDropsAtGrant();
        var result = await new RunScheduler(driver, new FakePlatform()).RunAsync(new SchedulerRequest
        {
            Manifest = manifest,
            Rows = manifest.Checkpoints,
            RunDirectory = root,
            WorkingDirectory = root,
            State = new RunState(),
            Slots = slot,
            DiskFreeBytes = slot.FreeBytes,
        }, CancellationToken.None);
        var row = result.Rows.ShouldHaveSingleItem();
        driver.Calls.ShouldBeEmpty("disk refusal precedes the scheduled driver");
        result.ExitCode.ShouldBe(ExitCodes.DiskLow, row.Line);
        row.TerminalReason.ShouldBe("workspace_disk_low");
        row.Slot.ShouldBe("granted");
        row.WaitedSeconds.ShouldBe(840);
        slot.Released.ShouldBe(1);
    }

    private static FakeDriver RecordingDriver()
    {
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), ("Antiphon.Tests.Sample.alpha", "Passed"));
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        return driver;
    }

    private sealed class DiskDropsAtGrant : IBuildSlotClient
    {
        private bool _admitted;
        public int Released { get; private set; }
        public long? FreeBytes(string path) => _admitted ? (20L << 30) - 1 : 20L << 30;

        public Task<SlotSession> ProbeAsync(CancellationToken cancellationToken) => Task.FromResult(new SlotSession("ok", 4));
        public Task<SlotLease> AcquireAsync(SlotSession session, string label, CancellationToken cancellationToken)
        {
            _admitted = true;
            return Task.FromResult(new SlotLease
            {
                State = "granted", WaitedSeconds = 840,
                ReleaseAsync = _ => { Released++; return Task.CompletedTask; },
            });
        }
    }
}
