using TUnit.Core.Interfaces;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0208: at most one process-spawning test runs at a time in this assembly.
/// Apply <c>[ParallelLimiter&lt;ProcessSpawnLimit&gt;]</c> to every class that starts a real
/// OS child or pty-host. Other tests are unaffected.
/// The limiter is assembly-local — it cannot cap a concurrent Antiphon.Tests or
/// Antiphon.Agents.Pty.Tests run.
/// </summary>
public sealed class ProcessSpawnLimit : IParallelLimit
{
    public int Limit => 1;
}
