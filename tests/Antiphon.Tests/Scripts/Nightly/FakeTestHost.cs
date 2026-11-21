using Antiphon.Nightly;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S01: fake test hosts for a RecordingLauncher. NativePass is the twin of Write-C487NativePassSeams
/// (scripts/lib/c487-harness.ps1): instead of running a test assembly it writes, from the launch's argv, what a passing
/// native TUnit run leaves behind.
/// </summary>
public static class FakeTestHost
{
    /// <summary>The log text every fake launch reports (C487_INVOCATION).</summary>
    public const string InvocationLog = "C487_INVOCATION fake";

    /// <summary>
    /// A passing native test host. From the argv: --results-directory and --report-trx-filename name the TRX, which
    /// holds the rows (a filename with a directory separator is refused: exit 5, "trx-filename-not-basename"); beside it
    /// &lt;name&gt;.discovery.json holds the nodes unless omitDiscovery. --diagnostic-output-directory gets log.diag: the
    /// discovery document under --list-tests, otherwise the execution document of the rows. -JsonResultPath gets the npm
    /// runner's one-pass JSON. Every other launch passes (exit 0, pid 2, children exited).
    /// </summary>
    public static Func<NightlyLaunch, NightlyLaunchResult> NativePass(IReadOnlyList<NightlyTrxRow> rows, IReadOnlyList<NightlyTestNode> nodes, bool omitDiscovery = false) =>
        launch =>
        {
            var args = launch.Arguments ?? [];
            string Value(string flag)
            {
                for (var i = 0; i + 1 < args.Count; i++)
                    if (args[i] == flag) return args[i + 1];
                return "";
            }
            var trx = Value("--report-trx-filename");
            var results = Value("--results-directory");
            var diagnostics = Value("--diagnostic-output-directory");
            var listTests = args.Contains("--list-tests");
            if (trx.Length > 0 && (trx.Contains('/') || trx.Contains('\\')))
                return Result(5, InvocationLog + " trx-filename-not-basename");
            if (trx.Length > 0)
            {
                if (results.Length > 0) trx = Path.Combine(results, trx);
                else if (!Path.IsPathRooted(trx) && !string.IsNullOrEmpty(launch.WorkingDirectory)) trx = Path.Combine(launch.WorkingDirectory, trx);
                NightlyFixture.WriteTrx(trx, rows);
                if (!omitDiscovery) NightlyFixture.WriteDiscovery(Path.ChangeExtension(trx, ".discovery.json"), nodes);
            }
            if (diagnostics.Length > 0)
            {
                var log = Path.Combine(diagnostics, "log.diag");
                if (listTests) NightlyFixture.WriteDiscovery(log, nodes);
                else NightlyFixture.WriteExecution(log, rows.Select(row => new NightlyTestNode(row.Id, row.ClassName, row.MethodName,
                    State: string.IsNullOrWhiteSpace(row.Outcome) ? "Passed" : row.Outcome, ClassName: row.ClassName)));
            }
            var json = Value("-JsonResultPath");
            if (json.Length > 0)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(json))!);
                File.WriteAllText(json, "{ \"numPassedTests\": 1, \"numFailedTests\": 0 }");
            }
            return Result(0, InvocationLog);
        };

    private static NightlyLaunchResult Result(int exitCode, string log) =>
        new() { ExitCode = exitCode, TimedOut = false, Pid = 2, ChildrenExited = true, LogText = log };
}
