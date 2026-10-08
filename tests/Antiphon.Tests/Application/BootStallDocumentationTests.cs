using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1151 S4: the four owner sentences of the plan, verbatim, in
/// <c>docs/session-runtime-invariants.md</c> and <c>docs/orchestration-loop.md</c>, each pinned
/// to the named test; and the absence of any remaining "kills the session"/"retried once"
/// promise in the changed delegate boot-stall sections. A documentation pin, not behavioural
/// proof. Repo root via the <c>RepairSourceDocumentationTests.FindRepoRoot</c> shape.
/// </summary>
[Category("Unit")]
public class BootStallDocumentationTests
{
    [Test]
    public Task C1151_Docs_describe_detection_and_only_compaction_exception() =>
        Card1151Pending.Skip("S4", nameof(C1151_Docs_describe_detection_and_only_compaction_exception));
}
