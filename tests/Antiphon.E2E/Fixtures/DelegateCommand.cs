using Antiphon.Nightly;

namespace Antiphon.E2E.Fixtures;

/// <summary>
/// Runs the delegate command (Antiphon.Nightly.DelegateCli, which replaced scripts/delegate.ps1 in unit 28 of the 2026-11-22
/// plan) in process against a real server, as the CLI does: the parse, then the run on the production host and HTTP
/// transports. Only the API, the task token and the working directory are set; the capability and operator token paths do
/// not exist, and this process's X_LLM_* variables are not forwarded. A usage error answers exit 2.
/// </summary>
internal static class DelegateCommand
{
    public static Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(string api, string? taskToken, string? workingDirectory,
        params string[] args) => Task.Run(() =>
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var missing = Path.Combine(Path.GetTempPath(), "delegate-e2e-unset-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = DelegateCliOptions.Parse(args, new DelegateCliEnvironment
            {
                Api = api,
                TaskToken = taskToken ?? "",
                CapabilityStore = Path.Combine(missing, "capabilities"),
                OperatorTokenFile = Path.Combine(missing, "operator-token"),
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
            });
            var exit = DelegateCli.Run(options, NightlyHost.Production(stdout, stderr), new System.IO.Abstractions.FileSystem(),
                NightlyReport.HttpTransport(timeout: DelegateCli.Deadline), NightlyReport.HttpTransport(timeout: options.ProbeTimeout), unprotect: null);
            return (exit, stdout.ToString(), stderr.ToString());
        }
        catch (NightlyUsageException e)
        {
            return (2, stdout.ToString(), stderr + "usage: " + e.Message + Environment.NewLine);
        }
    });
}
