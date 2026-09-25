using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0672 V-8 (D-3): every hold sentence the dispatcher writes maps to one ledger class, and the
/// ledger sums stints between consecutive Held rows.
/// </summary>
[Category("Unit")]
public sealed class DispatchHoldLedgerTests
{
    private static readonly DateTime T0 = new(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);

    public static IEnumerable<Func<(string Detail, DispatchHoldClass Expected)>> HoldSentences()
    {
        yield return () => (DispatchHoldDetails.LeaseHeldByLand("abcd1234", "a land", "ef012345", T0), DispatchHoldClass.Lease);
        yield return () => (DispatchHoldDetails.LeaseHeldByOwner(Guid.NewGuid(), "land", T0), DispatchHoldClass.Lease);
        yield return () => (DispatchHoldDetails.LeaseFenced("unfinished repository child journal"), DispatchHoldClass.Lease);
        yield return () => (DispatchHoldDetails.LeaseOccupiedUnknown, DispatchHoldClass.Lease);
        yield return () => (DispatchHoldDetails.RemoteMirrorRequested("server2", T0), DispatchHoldClass.RemotePrep);
        yield return () => (DispatchHoldDetails.RemotePrepBackoff("server2", 2, T0), DispatchHoldClass.RemotePrep);
        yield return () => (DispatchHoldDetails.RunnerAtCapacity("server2", 2, 2), DispatchHoldClass.Runner);
        yield return () => (DispatchHoldDetails.RunnerUnavailable("server2", "not connected"), DispatchHoldClass.Runner);
        yield return () => (DispatchHoldDetails.ConcurrencyCap(6), DispatchHoldClass.Cap);
        yield return () => ("waiting for ClaudeCode capacity (any alias); capacity wait abcd1234 not yet granted.", DispatchHoldClass.Cap);
        yield return () => (DispatchHoldDetails.PinnedAgentParkedOn("pool-a", "abcd1234", AgentTaskStatus.Blocked), DispatchHoldClass.Agent);
        yield return () => (DispatchHoldDetails.StandingAgentBusy("interp", "abcd1234", AgentTaskStatus.Working), DispatchHoldClass.Agent);
        yield return () => ("abcd1234 is landing", DispatchHoldClass.Landing);
        yield return () => ("routing pin not before 2099-01-01T00:00:00Z; dispatch paused (wait).", DispatchHoldClass.Routing);
        yield return () => ("fable is held; dispatch paused for that model.", DispatchHoldClass.Routing);
        yield return () => ("Held: running task abcd1234 \"a writer\" is already writing in this shared checkout (Shared/Shared; no intersecting scope).", DispatchHoldClass.Scope);
        yield return () => (DispatchHoldDetails.Escalation(DispatchHoldDetails.WarningPrefix, 400, T0, T0,
            DispatchHoldDetails.RunnerAtCapacity("server2", 1, 1), 2, 6, ["abcd1234"]), DispatchHoldClass.Runner);
        yield return () => ("something nobody writes", DispatchHoldClass.Other);
    }

    [Test]
    [MethodDataSource(nameof(HoldSentences))]
    public void C672_ClassOf_maps_every_hold_sentence(string detail, DispatchHoldClass expected)
    {
        DispatchHoldDetails.ClassOf(detail).ShouldBe(expected, detail);
    }

    [Test]
    public void A_task_title_quoted_in_a_scope_hold_does_not_reclass_it()
    {
        var detail = "Held: running task abcd1234 \"repository mutation lease is landing\" is already writing in this shared checkout (Shared/Shared).";
        DispatchHoldDetails.ClassOf(detail).ShouldBe(DispatchHoldClass.Scope);
    }

    [Test]
    public void FromRows_sums_stints_ignores_the_previous_stint_and_runs_the_open_one_to_now()
    {
        var lease = DispatchHoldDetails.LeaseOccupiedUnknown;
        var prep = DispatchHoldDetails.RemoteMirrorRequested("server2", T0);
        var runner = DispatchHoldDetails.RunnerAtCapacity("server2", 1, 1);
        var floor = T0.AddSeconds(-10);
        var rows = new[]
        {
            (T0.AddSeconds(-500), DispatchHoldDetails.ConcurrencyCap(6)), // before the floor: ignored
            (T0.AddSeconds(150), lease),
            (T0, lease),
            (T0.AddSeconds(120), prep),
            (T0.AddSeconds(300), runner),
        };

        var ledger = DispatchHoldLedger.FromRows(rows, floor, T0.AddSeconds(340));

        ledger.SecondsFor(DispatchHoldClass.Lease).ShouldBe(270);
        ledger.SecondsFor(DispatchHoldClass.RemotePrep).ShouldBe(30);
        ledger.SecondsFor(DispatchHoldClass.Runner).ShouldBe(40);
        ledger.SecondsFor(DispatchHoldClass.Cap).ShouldBe(0);
        ledger.OtherSeconds.ShouldBe(0);
        ledger.Dominant.ShouldBe(DispatchHoldClass.Lease);
        ledger.Describe().ShouldBe("leaseWait=270s; prepWait=30s; runnerWait=40s; capWait=0s; otherWait=0s; class=lease");
    }

    [Test]
    public void FromRows_other_sums_every_unnamed_class_and_names_the_dominant_one()
    {
        var rows = new[]
        {
            (T0, "abcd1234 is landing"),
            (T0.AddSeconds(100), DispatchHoldDetails.PinnedAgentParkedOn("pool-a", "abcd1234", AgentTaskStatus.Working)),
            (T0.AddSeconds(150), DispatchHoldDetails.ConcurrencyCap(6)),
        };

        var ledger = DispatchHoldLedger.FromRows(rows, DateTime.MinValue, T0.AddSeconds(170));

        ledger.OtherSeconds.ShouldBe(150);
        ledger.SecondsFor(DispatchHoldClass.Cap).ShouldBe(20);
        ledger.Dominant.ShouldBe(DispatchHoldClass.Landing);
        ledger.DominantName.ShouldBe("landing");
    }

    [Test]
    public void Lease_wins_a_tie_and_an_empty_ledger_names_none()
    {
        var tie = DispatchHoldLedger.FromRows(
            [(T0, DispatchHoldDetails.RunnerAtCapacity("server2", 1, 1)), (T0.AddSeconds(60), DispatchHoldDetails.LeaseOccupiedUnknown)],
            DateTime.MinValue, T0.AddSeconds(120));
        tie.SecondsFor(DispatchHoldClass.Runner).ShouldBe(60);
        tie.SecondsFor(DispatchHoldClass.Lease).ShouldBe(60);
        tie.Dominant.ShouldBe(DispatchHoldClass.Lease);

        var empty = DispatchHoldLedger.FromRows([], DateTime.MinValue, T0);
        empty.Dominant.ShouldBeNull();
        empty.Describe().ShouldEndWith("class=none");
    }
}
