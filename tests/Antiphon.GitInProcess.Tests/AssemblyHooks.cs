using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-1729 decision 4. A <c>[Before(Assembly)]</c> in another assembly does not run here,
/// so this mirror owns the same boot, pty and sweep hooks, plus the libgit2 config isolation
/// its differential tests rely on. No test here touches the database, so there is no
/// TestDbFixture lifecycle.
/// </summary>
internal static class AssemblyHooks
{
    [Before(Assembly)]
    public static void Initialize()
    {
        ProductionRunnerGuard.PointEveryProgramBootAwayFromTheProductionRunner();
        PtyBackendEnvGuard.ClearInheritedPtyBackend();
        if (Environment.GetEnvironmentVariable("C804_ORPHAN_SWEEP_ROOT") is null)
            new CheckpointTempRootSweep(CheckpointCleanupDefaults.Context, Path.GetTempPath()).SweepOnce(Console.Out);
        LibGit2ConfigIsolation.IsolateLibGit2Config();
    }
}
