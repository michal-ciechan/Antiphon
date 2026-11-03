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
}
