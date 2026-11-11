using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// The assembly attribute stays in this project. The resilience mirror links
/// <see cref="PtyBackendEnvGuard"/> and calls the method from its own hook (CARD-1829).
/// </summary>
internal static class PtyBackendEnvGuardAssemblyHook
{
    [Before(Assembly)]
    public static void ClearInheritedPtyBackend() =>
        PtyBackendEnvGuard.ClearInheritedPtyBackend();
}
