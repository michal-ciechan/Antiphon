using Antiphon.Server.Application.Settings;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0727 MS-1. The two production runner ids, with D-2's values. Secrets may be equal:
/// the validator admits that, and a ticket stays bound to the id that registered it.
/// </summary>
internal static class RollingRunnerSettings
{
    public const string Server2 = "server2";
    public const string Server2Temp = "server2-temp";

    public static PhoneHomeRunnerSettings Pair(string secretA, string secretB, int leaseSeconds = 90) => new()
    {
        Enabled = true,
        LeaseSeconds = leaseSeconds,
        Runners = new Dictionary<string, PhoneHomeRunnerEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [Server2] = Entry("server2", secretA),
            [Server2Temp] = Entry("server2 (temp)", secretB),
        },
    };

    public static PhoneHomeRunnerEntry Entry(string display, string secret) => new()
    {
        Enabled = true,
        DisplayName = display,
        AllowDelegatedTasks = true,
        HostWorkspaceRoot = @"C:\src\Antiphon",
        RunnerWorkspace = "/work",
        RunnerRepository = "/work/repos/antiphon",
        RawExeAllowList = ["/bin/sh", "/bin/bash", "/usr/local/bin/pwsh"],
        MaxCapacity = 10,
        ChildGrokHome = "/state/grok",
        ChildClaudeHome = "/state/claude",
        ChildCodexHome = "/state/codex",
        CallbackOrigin = "https://antiphon.desktop.codeperf.net",
        SharedSecret = secret,
    };
}
