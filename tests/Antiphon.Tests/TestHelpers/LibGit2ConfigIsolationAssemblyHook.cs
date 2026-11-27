using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// The assembly attribute stays in this project. The GitInProcess mirror links
/// <see cref="LibGit2ConfigIsolation"/> and calls the method from its own hook (CARD-1830).
/// </summary>
internal static class LibGit2ConfigIsolationAssemblyHook
{
    [Before(Assembly)]
    public static void IsolateLibGit2Config()
    {
        LibGit2ConfigIsolation.IsolateLibGit2Config();
        // libgit2 now reads no system file, so every git child of this process must not either: the code under test
        // starts git with this process's environment, and Git for Windows' system file (core.autocrlf=true) would
        // otherwise disagree with libgit2 on every status key check and send each status to git. A run that sets it
        // itself keeps its own value (GIT_CONFIG_NOSYSTEM=0 restores the system file, and with it the git status path).
        // Known divergence: a Windows production host whose system file sets core.autocrlf still takes the git path for
        // status (GitStatusConfigAgreement refuses there), so these tests exercise the in-process status path that the
        // Linux hosts and agreeing configs take; the refusal itself stays covered by the agreement tests.
        if (Environment.GetEnvironmentVariable("GIT_CONFIG_NOSYSTEM") is null)
            Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
    }
}
