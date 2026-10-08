using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1151 S5: the brief owner boundary (CARD-1150 S2). Design V-10. Fixture: the isolated
/// PostgreSQL schema and the <c>BridgeQueueHarness</c>/<c>OpenSweep</c> shapes of this partial
/// class, a Working prompt-only task at nine minutes, the real <c>TickAsync</c> repeated through
/// detection (8 min) and operator escalation (20 min) on a <c>FakeTimeProvider</c>. Helper names
/// in this file carry the <c>BootStall</c> prefix so they cannot collide with the S2 repair's
/// <c>Brief*</c> partials.
/// </summary>
public partial class DelegationDispatchRecoveryBoundaryTests
{
    /// <summary>
    /// V-10. inline: the Sent delegation brief row. spilled: the Sent row plus its spill file.
    /// pending-ui-followup: a Pending Ui row queued behind the Sent brief. Decisive: SHA-256 of
    /// Body, RemoteSpillBody and the spill file bytes, plus Id, Sequence, DeliveryAttempts,
    /// Status and the task's Attempt/DispatchedAt/ConcurrencyToken, equal before the first
    /// detection tick and after the escalation tick; zero new SessionQueuedMessages rows on the
    /// delegate session; runner Inputs 0; no ensure/send call recorded.
    /// </summary>
    [Test]
    [Arguments("inline")]
    [Arguments("spilled")]
    [Arguments("pending-ui-followup")]
    public Task C1151_Brief_and_spill_remain_byte_identical(string shape) =>
        Card1151Pending.Skip("S5", nameof(C1151_Brief_and_spill_remain_byte_identical));
}
