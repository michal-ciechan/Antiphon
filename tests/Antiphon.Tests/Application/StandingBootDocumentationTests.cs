using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1156 S6: the owner sentences, verbatim, in <c>docs/session-runtime-invariants.md</c>,
/// <c>docs/orchestration-loop.md</c> and <c>docs/agent-kinds.md</c>, each pinned to its named
/// test; the retired CARD-0312 restart/latch promise absent from the changed sections; and the
/// CARD-1151 <c>AlwaysOnExceptionSentence</c> replaced by the detection-only sentence in both
/// owners. A documentation pin, not behavioural proof. Design V-13 in
/// <c>docs/superpowers/plans/2026-10-08-card-1156-alwayson-boot-watchdog-test-design.md</c>.
/// </summary>
[Category("Unit")]
public class StandingBootDocumentationTests
{
    [Test]
    public Task C1156_Docs_name_detection_clocks_custody_and_compaction_exception() =>
        Card1156Pending.Skip("S6", nameof(C1156_Docs_name_detection_clocks_custody_and_compaction_exception));
}
