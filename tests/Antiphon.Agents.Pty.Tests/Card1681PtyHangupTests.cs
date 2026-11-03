using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-1681. The Linux pty master reads EIO once the last slave closes. Only an EIO after the
/// root has exited is end of output; an EIO while it lives, or any other read error, stays a
/// custody drain failure.
/// </summary>
[Category("Unit")]
public class Card1681PtyHangupTests
{
    private const int Eio = 5;

    [Test]
    public void C1681_Eio_after_root_exit_is_end_of_output() =>
        PtyAgentRunner.IsHangupAfterRootExit(new IOException("Input/output error", Eio), rootExited: true)
            .ShouldBeTrue();

    [Test]
    public void C1681_Eio_while_root_alive_stays_failure() =>
        PtyAgentRunner.IsHangupAfterRootExit(new IOException("Input/output error", Eio), rootExited: false)
            .ShouldBeFalse();

    [Test]
    [Arguments(9)]  // EBADF
    [Arguments(28)] // ENOSPC
    [Arguments(0)]
    [Arguments(-2147024891)]
    public void C1681_Other_read_errors_after_root_exit_stay_failures(int hresult) =>
        PtyAgentRunner.IsHangupAfterRootExit(new IOException("read failed", hresult), rootExited: true)
            .ShouldBeFalse();

    [Test]
    public void C1681_Eio_shaped_subclass_stays_failure() =>
        PtyAgentRunner.IsHangupAfterRootExit(new EndOfStreamException("eio-shaped") { HResult = Eio },
            rootExited: true).ShouldBeFalse();

    [Test]
    [Arguments(null, true)]
    [Arguments("4242 (sh) Z 1 4242 4242 34816 4242 4227084 0 0 0 0 0 0 0 0 20 0 1 0", true)]
    [Arguments("4242 (sh) X 1 4242 4242 34816 4242 4227084 0 0 0 0 0 0 0 0 20 0 1 0", true)]
    [Arguments("4242 (sh) R 1 4242 4242 34816 4242 4194308 0 0 0 0 0 0 0 0 20 0 1 0", true)]
    [Arguments("4242 (sh) S 1 4242 4242 34816 4242 4194560 0 0 0 0 0 0 0 0 20 0 1 0", false)]
    [Arguments("4242 (a) Z 9) S 1 4242 4242 34816 4242 4194560 0 0 0 0 0 0 0 0 20 0 1 0", false)]
    [Arguments("4242 (sh) S 1 4242", false)]
    [Arguments("garbage", false)]
    public void C1681_Proc_stat_exit_is_gone_zombie_dead_or_exiting(string? stat, bool exited) =>
        PtyAgentRunner.ProcStatShowsExit(stat).ShouldBe(exited);

    // CARD-1617 review D-1: the root-exit composition behind the hangup decision. Either signal
    // alone is enough; an unreadable stat is not "exited" (the drain fails closed).
    private const string LiveStat = "4242 (sh) S 1 4242 4242 34816 4242 4194560 0 0 0 0 0 0 0 0 20 0 1 0";
    private const string ZombieStat = "4242 (sh) Z 1 4242 4242 34816 4242 4227084 0 0 0 0 0 0 0 0 20 0 1 0";

    [Test]
    public void C1681_Root_exited_when_exit_observed_with_live_stat() =>
        PtyAgentRunner.RootExited(exitObserved: true, () => LiveStat).ShouldBeTrue();

    [Test]
    public void C1681_Root_exited_when_exit_observed_without_reading_stat()
    {
        var reads = 0;
        PtyAgentRunner.RootExited(exitObserved: true, () => { reads++; throw new IOException("unreadable", Eio); })
            .ShouldBeTrue();
        reads.ShouldBe(0);
    }

    [Test]
    [Arguments("4242 (sh) Z 1 4242 4242 34816 4242 4227084 0 0 0 0 0 0 0 0 20 0 1 0")]
    [Arguments("4242 (sh) X 1 4242 4242 34816 4242 4227084 0 0 0 0 0 0 0 0 20 0 1 0")]
    [Arguments("4242 (sh) x 1 4242 4242 34816 4242 4227084 0 0 0 0 0 0 0 0 20 0 1 0")]
    [Arguments("4242 (sh) R 1 4242 4242 34816 4242 4194308 0 0 0 0 0 0 0 0 20 0 1 0")]
    [Arguments("4242 (a b) S (c) Z 1 4242 4242 34816 4242 4194560 0 0 0 0 0 0 0 0 20 0 1 0")]
    public void C1681_Root_exited_from_stat_alone(string stat) =>
        PtyAgentRunner.RootExited(exitObserved: false, () => stat).ShouldBeTrue();

    [Test]
    [Arguments("4242 (sh) S 1 4242 4242 34816 4242 4194560 0 0 0 0 0 0 0 0 20 0 1 0")]
    [Arguments("4242 (x (y) Z) S 1 4242 4242 34816 4242 4194560 0 0 0 0 0 0 0 0 20 0 1 0")]
    public void C1681_Root_live_stat_is_not_exited(string stat)
    {
        var reads = 0;
        PtyAgentRunner.RootExited(exitObserved: false, () => { reads++; return stat; }).ShouldBeFalse();
        reads.ShouldBe(1);
    }

    [Test]
    [Arguments("io")]
    [Arguments("access")]
    public void C1681_Root_unreadable_stat_is_not_exited(string kind) =>
        PtyAgentRunner.RootExited(exitObserved: false, () => throw StatError(kind)).ShouldBeFalse();

    [Test]
    [Arguments("file")]
    [Arguments("dir")]
    public void C1681_Root_missing_stat_is_gone(string kind) =>
        PtyAgentRunner.RootExited(exitObserved: false, () => throw StatError(kind)).ShouldBeTrue();

    [Test]
    public void C1681_Root_without_pid_reader_is_not_exited() =>
        PtyAgentRunner.RootExited(exitObserved: false, readStat: null).ShouldBeFalse();

    [Test]
    public void C1681_Root_stat_reader_reads_real_files()
    {
        var dir = Path.Combine(Path.GetTempPath(), "c1681-stat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var live = Path.Combine(dir, "live");
            var zombie = Path.Combine(dir, "zombie");
            File.WriteAllText(live, LiveStat);
            File.WriteAllText(zombie, ZombieStat);
            PtyAgentRunner.RootExited(false, PtyAgentRunner.ProcStatReader(live)).ShouldBeFalse();
            PtyAgentRunner.RootExited(false, PtyAgentRunner.ProcStatReader(zombie)).ShouldBeTrue();
            // A directory read is UnauthorizedAccessException: unreadable, so not exited.
            PtyAgentRunner.RootExited(false, PtyAgentRunner.ProcStatReader(dir)).ShouldBeFalse();
            // A missing file, or a missing directory, means the process is gone.
            PtyAgentRunner.RootExited(false, PtyAgentRunner.ProcStatReader(Path.Combine(dir, "absent")))
                .ShouldBeTrue();
            PtyAgentRunner.RootExited(false, PtyAgentRunner.ProcStatReader(Path.Combine(dir, "absent", "stat")))
                .ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static Exception StatError(string kind) => kind switch
    {
        "io" => new IOException("Input/output error", Eio),
        "access" => new UnauthorizedAccessException("Permission denied"),
        "file" => new FileNotFoundException("gone"),
        "dir" => new DirectoryNotFoundException("gone"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
