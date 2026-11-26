using Antiphon.TestSupport.LiveClaude;
using TUnit.Core;
using Antiphon.Agents.Pty.Tests;
using static Antiphon.Agents.Pty.Tests.LiveClaudeExecutableWiringTests;

namespace Antiphon.Agents.Pty.Tests.Invariants;

[Category("Unit")]
[Category("RepositoryInvariant")]
[Timeout(30_000)]
public sealed class LiveClaudeExecutableWiringInvariantTests
{
    [Test]
    public void Session_callers_use_the_attested_executable()
    {
        AuditPtyCallers();
    }
}
