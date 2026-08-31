using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0289 — the three provider switches are independent (each carries its own evidence) and
/// are pinned together here so they cannot drift silently. A future provider that wants to
/// diverge (Codex one day mapping Frontier to <c>ultra</c>) deletes its row from this test
/// rather than unwinding a shared abstraction.
/// </summary>
[Category("Unit")]
public class TierReasoningEffortAgreementTests
{
    [Test]
    public void every_tier_maps_to_the_same_effort_string_on_codex_grok_and_claude()
    {
        foreach (var level in Enum.GetValues<AgentModelLevel>())
        {
            var codex = CodexLaunchArgs.ReasoningEffort(level);
            var grok = GrokLaunchArgs.ReasoningEffort(level);
            var claude = ClaudeLaunchArgs.Effort(level);
            grok.ShouldBe(codex, $"Grok drifted from Codex at {level}");
            claude.ShouldBe(codex, $"Claude drifted from Codex at {level}");
        }
    }
}
