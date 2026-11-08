using TUnit.Core.Interfaces;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0050 S5: at most one process-spawning test runs at a time in this assembly.
/// Apply <c>[ParallelLimiter&lt;ProcessSpawnLimit&gt;]</c> to every class that starts a child
/// (ConPTY, pwsh probe, fakeclaude/fakegrok, session runtime). Checkpoint-tool classes
/// whose children run in a fixture-owned root use <see cref="CheckpointToolSpawnLimit"/>
/// (CARD-1747). A class whose child uses the shared checkout stays here. Other tests are unaffected.
/// The limiter is per process — it cannot cap a concurrent Antiphon.Agents.Pty.Tests run.
/// Do not co-schedule those two projects at full width (CLAUDE.md); Limit=1 here is the
/// in-process half of that.
/// </summary>
public sealed class ProcessSpawnLimit : IParallelLimit
{
    public int Limit => 1;
}
