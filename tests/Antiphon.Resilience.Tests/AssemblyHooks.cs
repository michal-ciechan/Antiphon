using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-1729 decision 4. A <c>[Before(Assembly)]</c> in another assembly does not run here,
/// so this mirror owns the same boot, pty, sweep, and database hooks.
/// </summary>
internal static class AssemblyHooks
{
    [Before(Assembly)]
    public static async Task InitializeAsync(AssemblyHookContext context)
    {
        ProductionRunnerGuard.PointEveryProgramBootAwayFromTheProductionRunner();
        PtyBackendEnvGuard.ClearInheritedPtyBackend();
        if (Environment.GetEnvironmentVariable("C804_ORPHAN_SWEEP_ROOT") is null)
            new CheckpointTempRootSweep(CheckpointCleanupDefaults.Context, Path.GetTempPath()).SweepOnce(Console.Out);
        await TestDbFixture.InitializeAsync(context);
    }

    [After(Assembly)]
    public static async Task DisposeAsync()
    {
        await TestDbFixture.DisposeAsync();
    }
}
