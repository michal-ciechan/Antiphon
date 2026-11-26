using TUnit.Core.Interfaces;

namespace Antiphon.LiveClaude.Tests;

/// <summary>
/// CARD-0050 S5: at most one process-spawning test runs at a time in this assembly. Every live class here starts a real
/// Claude (ConPTY or the session runtime) and carries <c>[ParallelLimiter&lt;ProcessSpawnLimit&gt;]</c>, as it did in
/// Antiphon.Tests and Antiphon.Agents.Pty.Tests before the move.
/// </summary>
public sealed class ProcessSpawnLimit : IParallelLimit
{
    public int Limit => 1;
}
