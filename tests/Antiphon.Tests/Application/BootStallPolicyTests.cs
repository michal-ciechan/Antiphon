using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1151 S1/S3: the pure boot disposition (<c>NotBoot</c>, <c>DetectOnly</c>,
/// <c>SafeAbsentFailure</c>) over a typed observation record. Design V-2. No database, no runner:
/// the observation is built by hand, the way <c>AbsentLaunchPolicyTests</c> drives
/// <c>AbsentLaunchPolicy.IsNeverAttempted</c>. Each argument first admits an independently built
/// pristine observation (every positive condition present) and then flips exactly the named
/// condition; the flipped observation must select <c>DetectOnly</c>, never
/// <c>SafeAbsentFailure</c>, and the evidence order (identity, Working, inventory,
/// terminal/transcript, obligations/workspace, final recheck) must be visible in the reason.
/// </summary>
[Category("Unit")]
public class BootStallPolicyTests
{
    /// <summary>
    /// V-2. One positive condition removed per argument. Decisive: the pristine observation is
    /// <c>SafeAbsentFailure</c>; the flipped one is <c>DetectOnly</c> with the named condition in
    /// its reason; <c>model-reply-present</c> is <c>NotBoot</c>, never either of the others.
    /// </summary>
    [Test]
    [Arguments("working-read-failed")]
    [Arguments("working-true")]
    [Arguments("session-row-missing")]
    [Arguments("generation-mismatch")]
    [Arguments("runner-ownership-unknown")]
    [Arguments("inventory-unavailable")]
    [Arguments("inventory-listed")]
    [Arguments("terminal-evidence-missing")]
    [Arguments("transcript-incomplete")]
    [Arguments("workspace-not-quiet")]
    [Arguments("launch-owner-pending")]
    [Arguments("attempt-mismatch")]
    [Arguments("api-recovery-unresolved")]
    [Arguments("commit-recovery-pending")]
    [Arguments("model-reply-present")]
    public Task C1151_Whitelist_requires_positive_evidence(string missing) =>
        Card1151Pending.Skip("S1", nameof(C1151_Whitelist_requires_positive_evidence));
}
