using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

// Lexical launch admission lives in RunnerWorkspaceAdmission. Do not resolve traversal here.
internal static class RunnerWorkspaceCwd
{
    internal static bool IsAdmitted(string? cwd, string allowedCwd, bool windows) =>
        RunnerWorkspaceAdmission.IsAdmitted(cwd, allowedCwd, windows);
}
