using Antiphon.TestSupport.LiveClaude;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

[Category("Unit")]
public class LiveClaudeExecutableWiringTests
{
    private static LiveClaudeExecutableCases Fixture(string uid = LiveClaudeCore.Submit)
        => new(uid, (u, r, c, l) => ClSession.PrepareCore(u, r, c, l));
    [Test]
    public void ClSessionUsesAttestedExecutable()
    {
        using var f = Fixture();
        f.Selection("V-1");
    }
    [Test]
    public void CoreCallersRefuseMissingContext()
    {
        foreach (var uid in LiveClaudeCore.All.Where(u => u != LiveClaudeCore.C680))
        {
            using var f = Fixture(uid);
            f.Admission("V-2");
        }
    }
    [Test]
    public void ExecutedImageMismatchRefuses()
    {
        using var f = Fixture();
        f.Images("V-3");
    }
}
