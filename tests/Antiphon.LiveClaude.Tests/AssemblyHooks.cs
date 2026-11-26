using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.LiveClaude.Tests;

/// <summary>
/// CARD-1729 decision 4: a <c>[Before(Assembly)]</c> in another assembly does not run here, so this project owns the boot
/// and pty hooks Antiphon.Tests and Antiphon.Agents.Pty.Tests ran for these classes. The database starts lazily on the
/// first isolated schema (only C680 asks for one, and only once its live context is admitted), so the hook only disposes
/// it.
/// </summary>
internal static class AssemblyHooks
{
    [Before(Assembly)]
    public static void Initialize()
    {
        ProductionRunnerGuard.PointEveryProgramBootAwayFromTheProductionRunner();
        PtyBackendEnvGuard.ClearInheritedPtyBackend();
    }

    [After(Assembly)]
    public static Task DisposeAsync() => TestDbFixture.DisposeAsync();
}
