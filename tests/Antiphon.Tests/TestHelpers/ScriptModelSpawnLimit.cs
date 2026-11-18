using TUnit.Core.Interfaces;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1984: a wider lane for script-model methods whose children are bash, node, pwsh,
/// jq, git or coreutils on paths under a root the fixture created for that case. Apply
/// <c>[ParallelLimiter&lt;ScriptModelSpawnLimit&gt;]</c> instead of <see cref="ProcessSpawnLimit"/>
/// only when no port is bound, the test process changes no environment variable or directory,
/// and no child uses the shared checkout as its working directory and writes there. A
/// checkout-cwd child that writes the checkout stays on <see cref="ProcessSpawnLimit"/>.
/// Classify by what is spawned, never by name. Assembly-local like
/// <see cref="ProcessSpawnLimit"/>; it runs beside the strict lane and is not shared across
/// test projects. S1a pins <see cref="Resolve"/> at 1 so the lane witnesses fail closed.
/// </summary>
public sealed class ScriptModelSpawnLimit : IParallelLimit
{
    public const string OverrideVariable = "ANTIPHON_TEST_SCRIPT_MODEL_SPAWN_LIMIT";

    public const string SlotVariable = "ANTIPHON_BUILD_SLOT_MAXCPUCOUNT";

    // S1a stub. S1b reads the override, the slot and the processor count once, here.
    private static readonly int Resolved = 1;

    public int Limit => Resolved;

    /// <summary>
    /// S1a stub. Every input resolves to 1 so the width pin, not a live formula, is what fails.
    /// </summary>
    internal static int Resolve(int processorCount, string? overrideValue, string? slotCpu, bool isWindows)
    {
        _ = processorCount;
        _ = overrideValue;
        _ = slotCpu;
        _ = isWindows;
        return 1;
    }
}
