using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1151 S4: the derived <c>AttentionKind.Overdue</c> boot row. Design V-11. Fixture: the
/// id-scoped <c>AttentionServiceTests.Scenario</c> and <c>BuildService</c> over the shared test
/// Postgres with an injected <c>FakeTimeProvider</c>; one Working task, one real UserPrompt, no
/// model row. The row is derived from current boot facts, never from a persisted Warning.
/// </summary>
[Category("Integration")]
public class BootStallAttentionTests
{
    /// <summary>
    /// V-11. preview-6m24s: Warning row, detection wording, actions exactly OpenDrawer/Reply/Cancel,
    /// no Escalate. detected-8m: same row, "detected" wording, no "kills"/"fails"/"retried" text
    /// anywhere in headline or evidence, no persisted Warning required. operator-20m: Error, the
    /// operator sentence (inspect; wait, reply, or explicitly cancel/retry), original prompt age and
    /// boot due time in evidence. past-ceiling: still the boot row, Error, the ceiling breach named
    /// as evidence, no "will fail it". model-reply-resolves: one AssistantText after the prompt
    /// removes the row even though historical Warning events remain.
    /// </summary>
    [Test]
    [Arguments("preview-6m24s")]
    [Arguments("detected-8m")]
    [Arguments("operator-20m")]
    [Arguments("past-ceiling")]
    [Arguments("model-reply-resolves")]
    public Task C1151_Attention_describes_detection_and_resolution(string moment) =>
        Card1151Pending.Skip("S4", nameof(C1151_Attention_describes_detection_and_resolution));
}
