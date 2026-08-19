using TUnit.Core.Interfaces;

namespace Antiphon.PtyHost.Tests;

/// <summary>
/// CARD-0050 S5: at most one process-spawning test runs at a time in this assembly.
/// Apply <c>[ParallelLimiter&lt;ProcessSpawnLimit&gt;]</c> to every class that starts a host.
/// </summary>
public sealed class ProcessSpawnLimit : IParallelLimit
{
    public int Limit => 1;
}
