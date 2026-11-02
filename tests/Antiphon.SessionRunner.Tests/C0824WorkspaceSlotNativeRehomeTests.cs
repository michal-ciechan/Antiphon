using Antiphon.SessionRunner;
using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;
using static Antiphon.SessionRunner.Tests.C0824SlotScratch;

namespace Antiphon.SessionRunner.Tests;

/// <summary>CARD-0824 S1 CP-10 (linux-native). A real file the runner uid cannot unlink re-homes the slot.</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(10_000)]
public sealed class C0824WorkspaceSlotNativeRehomeTests
{
    [Test]
    [NativeLinuxOnly("CARD-0824 Linux-only by construction: Unix 0555 directory mode")]
    public async Task C0824_Real0555DirectoryRehomes(CancellationToken ct)
    {
        // V-36. git clean exits 1 on the 0555 directory; the slot re-homes and the old directory keeps the file.
        var status = File.ReadAllLines("/proc/self/status").FirstOrDefault(line => line.StartsWith("Uid:", StringComparison.Ordinal)) ?? "";
        var effective = status.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries).Skip(2).FirstOrDefault();
        if (Environment.UserName == "root" || effective == "0")
            Assert.Fail("V-36 cannot run as uid 0: a 0555 directory does not stop root from unlinking.");
        using var scratch = Create();
        var pool = scratch.Pool();
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        var readOnly = a.Path + "/scratch-ro/sub";
        Plant(a.Path, "scratch-ro/sub/f", "undeletable");
        File.SetUnixFileMode(readOnly, UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.GroupRead
            | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        try
        {
            (await Release(pool, a, TaskA)).Outcome.ShouldBe("quarantined", "re-homed");
            File.Exists(a.Path + "/scratch-ro/sub/f").ShouldBeTrue();
            var entry = pool.Ledger().QuarantinedDirectories.ShouldHaveSingleItem();
            entry.Class.ShouldBe("filesystem");
            entry.Error.ShouldContain("Permission denied");
            pool.Ledger().Slot("ws-01")!.Path.ShouldBe(scratch.Slot("ws-01-d2"));
            var b = await pool.LeaseAsync(scratch.RequestB(), ct);
            b.Path.ShouldBe(scratch.Slot("ws-01-d2"));
        }
        finally
        {
            File.SetUnixFileMode(readOnly, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }
}
