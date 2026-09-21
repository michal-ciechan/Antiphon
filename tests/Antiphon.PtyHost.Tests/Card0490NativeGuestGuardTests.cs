using Antiphon.Card0490.NativeHarness;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.PtyHost.Tests;

[Category("PtyHost")]
public class Card0490NativeGuestGuardTests
{
    private static void RequireLinux()
    {
        if (!OperatingSystem.IsLinux())
            throw new SkipTestException("Guest guard probes execute on Linux.");
    }

    [Test]
    public async Task Guest_rejects_unlisted_method_project()
    {
        RequireLinux();
        var guestExecutionPermitted = NativeInputPolicy.AllowMethod("tests/Other", "/*/*/Nope/Nope");
        guestExecutionPermitted.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Guest_rejects_changed_input_digest()
    {
        RequireLinux();
        var guestExecutionPermitted = NativeInputPolicy.DigestsMatch("aaa", "bbb");
        guestExecutionPermitted.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Guest_rejects_unqualified_toolchain()
    {
        RequireLinux();
        var guestExecutionPermitted = NativeInputPolicy.AssetMapEquals(
            new Dictionary<string, string> { ["sdk"] = "10.0.204" },
            new Dictionary<string, string> { ["sdk"] = "9.0.0" });
        guestExecutionPermitted.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Guest_refuses_non_ext4_work_root()
    {
        throw new SkipTestException(
            "CARD-0490 D-12 is not yet implemented: guest ext4 probes require the real QEMU guest.");
    }

    [Test]
    public async Task Guest_refuses_prior_phase_workspace()
    {
        throw new SkipTestException(
            "CARD-0490 D-12 is not yet implemented: guest workspace probes require the real QEMU guest.");
    }

    [Test]
    public async Task Guest_build_cannot_reuse_seeded_application_output()
    {
        throw new SkipTestException(
            "CARD-0490 D-12 is not yet implemented: guest build probes require the real QEMU guest.");
    }

    [Test]
    public async Task Guest_rejects_unlisted_fixture_probe()
    {
        RequireLinux();
        var probeDispatchPermitted = NativeInputPolicy.AllowFixtureProbe("unknown-case");
        probeDispatchPermitted.ShouldBeFalse();
        await Task.CompletedTask;
    }
}
