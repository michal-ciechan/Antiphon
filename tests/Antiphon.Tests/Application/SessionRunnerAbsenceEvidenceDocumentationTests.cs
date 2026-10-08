using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1153 S5: the five owner sentences of the plan, verbatim, in their owning documents
/// (session-runtime-invariants, ops-http, antiphon-api, agent-credentials), the runner route
/// map listing both POST routes without /api, and the credential requirement without a literal
/// key or fleet path. A documentation pin, not behavioural proof. Repo root via the
/// RepairSourceDocumentationTests.FindRepoRoot shape.
/// </summary>
[Category("Unit")]
public class SessionRunnerAbsenceEvidenceDocumentationTests
{
    [Test]
    public Task C1153_Owner_sentences_match_the_protocol() =>
        Card1153Pending.Skip("S5", nameof(C1153_Owner_sentences_match_the_protocol));
}
