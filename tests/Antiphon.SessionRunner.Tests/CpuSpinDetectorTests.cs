using Antiphon.SessionRunner;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// Pure-logic coverage of the CPU spin detector: a kill verdict must mean "continuously at/above
/// the hot threshold for the whole sustained window" — never a single spike, never a window that
/// silently survives a cool interval or a recycled PID.
/// </summary>
public class CpuSpinDetectorTests
{
    private static readonly Guid Session = Guid.NewGuid();
    private static readonly DateTime T0 = new(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);

    private static CpuSpinDetector NewDetector() =>
        new(hotCpuPercent: 50, sustainedDuration: TimeSpan.FromSeconds(10));

    [Test]
    public void First_sample_only_establishes_the_baseline()
    {
        var detector = NewDetector();

        detector.Observe(Session, TimeSpan.FromSeconds(100), T0).ShouldBeFalse();
    }

    [Test]
    public void Continuously_hot_for_the_sustained_window_triggers()
    {
        var detector = NewDetector();

        detector.Observe(Session, TimeSpan.Zero, T0).ShouldBeFalse();
        // 5 CPU-seconds over 5 wall-seconds = 100% of a core; hot since T0 but only 5s of it.
        detector.Observe(Session, TimeSpan.FromSeconds(5), T0.AddSeconds(5)).ShouldBeFalse();
        // Another hot interval: 10s continuously hot — the window is met.
        detector.Observe(Session, TimeSpan.FromSeconds(10), T0.AddSeconds(10)).ShouldBeTrue();
    }

    [Test]
    public void Usage_exactly_at_the_threshold_counts_as_hot()
    {
        var detector = NewDetector();

        detector.Observe(Session, TimeSpan.Zero, T0).ShouldBeFalse();
        // 2.5 CPU-seconds over 5 wall-seconds = exactly 50%.
        detector.Observe(Session, TimeSpan.FromSeconds(2.5), T0.AddSeconds(5)).ShouldBeFalse();
        detector.Observe(Session, TimeSpan.FromSeconds(5.0), T0.AddSeconds(10)).ShouldBeTrue();
    }

    [Test]
    public void A_cool_interval_resets_the_window()
    {
        var detector = NewDetector();

        detector.Observe(Session, TimeSpan.Zero, T0).ShouldBeFalse();
        detector.Observe(Session, TimeSpan.FromSeconds(5), T0.AddSeconds(5)).ShouldBeFalse();   // hot
        // 0.5 CPU-seconds over 5 wall-seconds = 10% — cool; the hot streak is broken.
        detector.Observe(Session, TimeSpan.FromSeconds(5.5), T0.AddSeconds(10)).ShouldBeFalse();
        // Hot again, but the window restarted at the cool sample — 5s of heat is not 10.
        detector.Observe(Session, TimeSpan.FromSeconds(10.5), T0.AddSeconds(15)).ShouldBeFalse();
        detector.Observe(Session, TimeSpan.FromSeconds(15.5), T0.AddSeconds(20)).ShouldBeTrue();
    }

    [Test]
    public void Cpu_time_going_backwards_resets_instead_of_counting_a_recycled_pid()
    {
        var detector = NewDetector();

        detector.Observe(Session, TimeSpan.FromSeconds(60), T0).ShouldBeFalse();
        // A different process under the same PID reports LESS cumulative CPU — never a verdict.
        detector.Observe(Session, TimeSpan.FromSeconds(2), T0.AddSeconds(5)).ShouldBeFalse();
        // The new baseline works normally from here.
        detector.Observe(Session, TimeSpan.FromSeconds(7), T0.AddSeconds(10)).ShouldBeFalse();
        detector.Observe(Session, TimeSpan.FromSeconds(12), T0.AddSeconds(15)).ShouldBeTrue();
    }

    [Test]
    public void Forget_drops_the_window()
    {
        var detector = NewDetector();

        detector.Observe(Session, TimeSpan.Zero, T0).ShouldBeFalse();
        detector.Observe(Session, TimeSpan.FromSeconds(5), T0.AddSeconds(5)).ShouldBeFalse();

        detector.Forget(Session);

        // Post-forget the same cadence needs the full window again: baseline + 10 hot seconds.
        detector.Observe(Session, TimeSpan.FromSeconds(10), T0.AddSeconds(10)).ShouldBeFalse();
        detector.Observe(Session, TimeSpan.FromSeconds(15), T0.AddSeconds(15)).ShouldBeFalse();
        detector.Observe(Session, TimeSpan.FromSeconds(20), T0.AddSeconds(20)).ShouldBeTrue();
    }

    [Test]
    public void ForgetAllExcept_prunes_only_unwatched_sessions()
    {
        var detector = NewDetector();
        var other = Guid.NewGuid();
        detector.Observe(Session, TimeSpan.Zero, T0);
        detector.Observe(other, TimeSpan.Zero, T0);
        detector.Observe(Session, TimeSpan.FromSeconds(5), T0.AddSeconds(5));
        detector.Observe(other, TimeSpan.FromSeconds(5), T0.AddSeconds(5));

        detector.ForgetAllExcept(new HashSet<Guid> { Session });

        // The kept session's streak survived (10s hot => verdict)…
        detector.Observe(Session, TimeSpan.FromSeconds(10), T0.AddSeconds(10)).ShouldBeTrue();
        // …the pruned one restarted from a baseline.
        detector.Observe(other, TimeSpan.FromSeconds(10), T0.AddSeconds(10)).ShouldBeFalse();
    }
}
