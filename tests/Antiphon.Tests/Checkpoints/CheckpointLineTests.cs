using System.Text.RegularExpressions;
using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointLineTests
{
    private static readonly Regex Pinned = new(
        @"^CHECKPOINT CP-1 commit=[0-9a-f]{40} build=(ok|reused) filter=.+ executed=\d+ passed=\d+ failed=\d+ skipped=\d+ trx=.+$",
        RegexOptions.CultureInvariant);

    [Test]
    public void tunit_line_matches_the_pinned_regex_with_slot_suffix()
    {
        var line = CheckpointLine.Format(new CheckpointLineModel
        {
            Name = "CP-1",
            Commit = new string('a', 40),
            Build = "ok",
            Filter = "/*/*/ExampleSurfaceTests/*",
            Executed = "3",
            Passed = "2",
            Failed = "1",
            Skipped = "0",
            Trx = "/tmp/run.trx",
            Slot = "granted",
            WaitedSeconds = 0,
            Reruns = 1,
        });
        Pinned.IsMatch(line).ShouldBeTrue(line);
        var slot = line.IndexOf("slot=", StringComparison.Ordinal);
        var waited = line.IndexOf("waited=", StringComparison.Ordinal);
        var reruns = line.IndexOf("reruns=", StringComparison.Ordinal);
        slot.ShouldBeGreaterThan(line.IndexOf("trx=", StringComparison.Ordinal));
        waited.ShouldBeGreaterThan(slot);
        reruns.ShouldBeGreaterThan(waited);
        line.ShouldContain("slot=granted waited=0s reruns=1");
    }

    [Test]
    public void command_line_carries_exit()
    {
        var line = CheckpointLine.Format(new CheckpointLineModel
        {
            Name = "CP-7",
            Commit = new string('b', 40),
            Build = "n/a",
            Filter = "dotnet pack",
            Command = true,
            ExitCode = 0,
            Slot = "unavailable",
            WaitedSeconds = 2,
        });
        line.ShouldContain("build=n/a");
        line.ShouldContain("executed=n/a");
        line.ShouldContain("trx=n/a");
        line.ShouldContain("exit=0");
        line.ShouldContain("slot=unavailable waited=2s");
    }

    [Test]
    public void timeout_line_carries_timeout()
    {
        var line = CheckpointLine.Format(new CheckpointLineModel
        {
            Name = "CP-1",
            Commit = new string('c', 40),
            Build = "ok",
            Filter = "/*/*/ExampleSurfaceTests/*",
            Timeout = "15m",
            Slot = "granted",
            WaitedSeconds = 0,
        });
        line.ShouldContain("executed=n/a");
        line.ShouldContain("trx=n/a");
        line.ShouldContain("timeout=15m");
    }
}
