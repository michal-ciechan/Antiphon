using TUnit.Core.Interfaces;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1747: a separate lane for checkpoint-tool test classes whose children are
/// <c>dotnet</c>, <c>pwsh</c>, <c>git</c> or <c>node</c> in a fixture-owned root, and never
/// ConPTY, a fake agent or a session runtime. Apply
/// <c>[ParallelLimiter&lt;CheckpointToolSpawnLimit&gt;]</c> instead of
/// <see cref="ProcessSpawnLimit"/>. A class that starts a child with the shared checkout as
/// its working directory stays on <see cref="ProcessSpawnLimit"/>, because that checkout's
/// build outputs and git index are process-wide. Assembly-local, like
/// <see cref="ProcessSpawnLimit"/>; it is not shared across test projects.
/// Width is 2. Each slots-off checkpoint build uses <c>-maxcpucount:4</c>
/// (<c>RowRunner</c> / the unleased slot fallback), so two occupants are eight MSBuild nodes,
/// not a processor-count fan-out. A live grant on the shared host was <c>maxcpucount=6</c>
/// with four holders; width 2 stays inside two of the tool's own 4-node builds and does not
/// take the host slot. <c>ANTIPHON_TEST_CHECKPOINT_TOOL_SPAWN_LIMIT</c> (1-4)
/// overrides the width. <c>1</c> serialises this lane for bisecting, but it still runs beside
/// the strict lane. There is no cache: the value is read once when this type initializes.
/// </summary>
public sealed class CheckpointToolSpawnLimit : IParallelLimit
{
    public const string OverrideVariable = "ANTIPHON_TEST_CHECKPOINT_TOOL_SPAWN_LIMIT";

    public const int DefaultLimit = 2;

    public const int MinimumLimit = 1;

    public const int MaximumLimit = 4;

    private static readonly int Resolved =
        Resolve(Environment.ProcessorCount, Environment.GetEnvironmentVariable(OverrideVariable));

    public int Limit => Resolved;

    /// <summary>
    /// <paramref name="processorCount"/> is accepted so a caller can show the default does not
    /// grow with the machine. The safe width is the slot cap, not the core count.
    /// </summary>
    internal static int Resolve(int processorCount, string? overrideValue)
    {
        _ = processorCount;
        return int.TryParse(overrideValue?.Trim(), out var requested)
            ? Math.Clamp(requested, MinimumLimit, MaximumLimit)
            : DefaultLimit;
    }
}
