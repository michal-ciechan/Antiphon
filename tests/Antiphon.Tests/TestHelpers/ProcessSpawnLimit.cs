using TUnit.Core.Interfaces;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0050 S5: at most one process-spawning test runs at a time in this assembly.
/// Apply <c>[ParallelLimiter&lt;ProcessSpawnLimit&gt;]</c> to every class that starts a child
/// (ConPTY, pwsh probe, fakeclaude/fakegrok, session runtime). Checkpoint-tool classes
/// whose children run in a fixture-owned root use <see cref="CheckpointToolSpawnLimit"/>
/// (CARD-1747). Script-model methods whose children are bash, node, pwsh, jq, git or
/// coreutils on a fixture-owned root, with no port, no test-process environment or directory
/// change, and no checkout-cwd child that writes the checkout, use
/// <see cref="ScriptModelSpawnLimit"/> (CARD-1984). A checkout-cwd child that writes there
/// stays here. Other tests are unaffected. Exception: the Codex CLI observation classes and the C1213 queued-branch tests in DelegateBranchDeliveryTests hold
/// one slot and run independent worlds (git children only) side by side inside it, at most
/// Worlds.Degree at once (ANTIPHON_TEST_CODEX_CLI_WORLDS=1 runs them one at a time).
/// The limiter is per process — it cannot cap a concurrent Antiphon.Agents.Pty.Tests run.
/// Do not co-schedule those two projects at full width (CLAUDE.md); Limit=1 here is the
/// in-process half of that.
/// </summary>
public sealed class ProcessSpawnLimit : IParallelLimit
{
    public int Limit => 1;
}
