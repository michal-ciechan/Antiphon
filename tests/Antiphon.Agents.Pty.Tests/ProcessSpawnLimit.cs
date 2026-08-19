using TUnit.Core.Interfaces;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0050 S5: at most one process-spawning test runs at a time in this assembly.
/// Apply <c>[ParallelLimiter&lt;ProcessSpawnLimit&gt;]</c> to every class that starts a child
/// (ConPTY, pwsh, fakeclaude/fakegrok, headed Claude). Other tests are unaffected.
/// The limiter is per process — it cannot cap a concurrent Antiphon.Tests run.
/// Do not co-schedule those two projects at full width (CLAUDE.md); Limit=1 here is the
/// in-process half of that.
/// </summary>
public sealed class ProcessSpawnLimit : IParallelLimit
{
    public int Limit => 1;
}
