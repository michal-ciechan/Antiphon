using TUnit.Core.Interfaces;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1984: a wider lane for script-model methods whose children are bash, node, pwsh,
/// jq, git or coreutils on paths under a root the fixture created for that case. Apply
/// <c>[ParallelLimiter&lt;ScriptModelSpawnLimit&gt;]</c> instead of <see cref="ProcessSpawnLimit"/>
/// only when no port is bound, the test process changes no environment variable or directory,
/// and no child uses the shared checkout as its working directory and writes there. A
/// checkout-cwd child that writes the checkout stays on <see cref="ProcessSpawnLimit"/>.
/// No file written in this process is exec'd: a concurrent spawn holds a forked copy of the
/// write handle until its own exec, and the exec of that file then fails with ETXTBSY. Create
/// executables with <see cref="FixtureExecutable"/> (CARD-1984 review B-1, 2026-11-18).
/// Classify by what is spawned, never by name. Assembly-local like
/// <see cref="ProcessSpawnLimit"/>; it runs beside the strict lane and is not shared across
/// test projects. Width is read once, when this type initializes.
/// </summary>
public sealed class ScriptModelSpawnLimit : IParallelLimit
{
    public const string OverrideVariable = "ANTIPHON_TEST_SCRIPT_MODEL_SPAWN_LIMIT";

    public const string SlotVariable = "ANTIPHON_BUILD_SLOT_MAXCPUCOUNT";

    private static readonly int Resolved = Resolve(
        Environment.ProcessorCount,
        Environment.GetEnvironmentVariable(OverrideVariable),
        Environment.GetEnvironmentVariable(SlotVariable),
        OperatingSystem.IsWindows());

    public int Limit => Resolved;

    /// <summary>
    /// Override (1-8, clamped) wins. Otherwise Windows is 1. Otherwise a parsed slot cpu count
    /// is clamped to 1-6. Otherwise the width is <c>Clamp(processorCount / 4, 2, 6)</c>.
    /// An empty or non-numeric override falls through.
    /// </summary>
    internal static int Resolve(int processorCount, string? overrideValue, string? slotCpu, bool isWindows)
    {
        if (int.TryParse(overrideValue?.Trim(), out var requested))
            return Math.Clamp(requested, 1, 8);
        if (isWindows)
            return 1;
        if (int.TryParse(slotCpu?.Trim(), out var slot))
            return Math.Clamp(slot, 1, 6);
        return Math.Clamp(processorCount / 4, 2, 6);
    }
}
