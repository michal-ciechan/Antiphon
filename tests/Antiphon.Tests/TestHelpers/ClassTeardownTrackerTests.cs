using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

// Run ae84478d g80 (rc/2026-11-17, Windows): AgentTaskLandNotificationParkFollowUpTests' After(Class)
// was still disposing its harness when the assembly's last test (C458_DownThenUpPreservesLegacyWorkspace,
// another class) finished 240 ms later and ran After(Assembly). TestDbFixture was disposed first, so the
// class's clone drop threw ObjectDisposedException. TUnit 1.44 decrements the class and assembly
// counters together before After(Class) runs, so the assembly teardown must wait for open classes itself.
[Category("Unit")]
public sealed class ClassTeardownTrackerTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    [Test]
    public async Task Assembly_teardown_waits_for_an_open_class_teardown()
    {
        var tracker = new ClassTeardownTracker();
        tracker.Enter("Slow.ClassTeardown");
        tracker.Enter("Fast.Class");
        tracker.Exit("Fast.Class");
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var teardown = TestDbFixtureAssemblyHook.DisposeAfterClassTeardownsAsync(
            tracker,
            () =>
            {
                disposed.TrySetResult();
                return Task.CompletedTask;
            },
            Bound);

        await Task.Delay(TimeSpan.FromMilliseconds(300));
        disposed.Task.IsCompleted.ShouldBeFalse(
            "the fixture was disposed while Slow.ClassTeardown's After(Class) was still open");

        tracker.Exit("Slow.ClassTeardown");
        var open = await teardown.WaitAsync(Bound);
        disposed.Task.IsCompleted.ShouldBeTrue();
        open.ShouldBeEmpty();
    }

    [Test]
    public async Task A_class_that_never_closes_releases_the_teardown_at_the_bound_and_is_named()
    {
        var tracker = new ClassTeardownTracker();
        tracker.Enter("Never.Closes");
        var disposed = false;

        var open = await TestDbFixtureAssemblyHook.DisposeAfterClassTeardownsAsync(
            tracker,
            () =>
            {
                disposed = true;
                return Task.CompletedTask;
            },
            TimeSpan.FromMilliseconds(200)).WaitAsync(Bound);

        disposed.ShouldBeTrue();
        open.ShouldBe(["Never.Closes"]);
    }

    [Test]
    public async Task An_idle_tracker_disposes_at_once()
    {
        var tracker = new ClassTeardownTracker();
        var disposed = false;

        var open = await TestDbFixtureAssemblyHook.DisposeAfterClassTeardownsAsync(
            tracker,
            () =>
            {
                disposed = true;
                return Task.CompletedTask;
            },
            Bound).WaitAsync(TimeSpan.FromSeconds(2));

        disposed.ShouldBeTrue();
        open.ShouldBeEmpty();
    }
}
