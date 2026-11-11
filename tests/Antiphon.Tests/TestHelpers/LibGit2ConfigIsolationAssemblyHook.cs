using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// The assembly attribute stays in this project. The GitInProcess mirror links
/// <see cref="LibGit2ConfigIsolation"/> and calls the method from its own hook (CARD-1830).
/// </summary>
internal static class LibGit2ConfigIsolationAssemblyHook
{
    [Before(Assembly)]
    public static void IsolateLibGit2Config() =>
        LibGit2ConfigIsolation.IsolateLibGit2Config();
}
