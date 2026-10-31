using Antiphon.TestSupport.LiveClaude;
using Antiphon.Tests.Agents;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
public class LiveLaneExecutableTests
{
    private static LiveClaudeExecutableCases Fixture()
        => new(LiveClaudeCore.C680, (_, r, c, l) => ClaudeDialogWatchdogCanaryTests.PrepareExecutable(r, c, l));
    [Test]
    public void C680UsesAttestedExecutable()
    {
        using var f = Fixture();
        f.Selection("V-4");
    }
    [Test]
    public void C680RefusesMissingContext()
    {
        using var f = Fixture();
        f.Admission("V-5");
    }
    [Test]
    public void C680ImageMismatchRefuses()
    {
        using var f = Fixture();
        f.Images("V-6");
    }
}
