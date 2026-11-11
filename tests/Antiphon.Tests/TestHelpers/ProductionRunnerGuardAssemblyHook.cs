using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// The assembly attribute stays in this project. The resilience mirror links
/// <see cref="ProductionRunnerGuard"/> and calls the method from its own hook,
/// so the attribute cannot live on the shared method (CARD-1829).
/// </summary>
internal static class ProductionRunnerGuardAssemblyHook
{
    [Before(Assembly)]
    public static void PointEveryProgramBootAwayFromTheProductionRunner() =>
        ProductionRunnerGuard.PointEveryProgramBootAwayFromTheProductionRunner();
}
