using TUnit.Core.Interfaces;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1421: a wider lane for classes whose only child processes are <c>git</c>, run in
/// repositories the fixture owns. Apply <c>[ParallelLimiter&lt;GitProcessSpawnLimit&gt;]</c>
/// instead of <see cref="ProcessSpawnLimit"/> only when the class and every helper it uses start
/// no pty, pwsh, shell, cmd, dotnet worker, fake agent, web host or session runtime; a class
/// that mixes git with any other child stays on the strict lane. Classify by what is spawned,
/// never by name. Assembly-local like <see cref="ProcessSpawnLimit"/>; it is not shared across
/// test projects. Width is <c>Clamp(ProcessorCount / 2, 2, 4)</c>;
/// <c>ANTIPHON_TEST_GIT_SPAWN_LIMIT</c> (1-8) overrides it. <c>1</c> serialises this lane for
/// bisecting, but it still runs beside the strict lane, so it is not the pre-CARD-1421 order.
/// </summary>
public sealed class GitProcessSpawnLimit : IParallelLimit
{
    public const string OverrideVariable = "ANTIPHON_TEST_GIT_SPAWN_LIMIT";

    private static readonly int Resolved =
        Resolve(Environment.ProcessorCount, Environment.GetEnvironmentVariable(OverrideVariable));

    public int Limit => Resolved;

    internal static int Resolve(int processorCount, string? overrideValue) =>
        int.TryParse(overrideValue?.Trim(), out var requested)
            ? Math.Clamp(requested, 1, 8)
            : Math.Clamp(processorCount / 2, 2, 4);
}
