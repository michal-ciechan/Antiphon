using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class AgentPinnedInstructionCompositionTests
{
    [Test]
    public void V04_SupportedSnapshot()
    {
        // The dormant core must ship its capture contract before any caller can select it.
        InstructionBundles.All.Keys.ShouldContain("standing-instructions");
        var protocol = InstructionBundles.Get("standing-instructions");
        protocol.Text.ShouldContain("KB save alone is insufficient");
        protocol.Text.ShouldContain("explicit user standing intent");
        InstructionBundles.Attachable.Select(b => b.Key).ShouldNotContain("standing-instructions");
        InstructionBundleComposer.Compose().Text.ShouldBeEmpty();
    }
}
