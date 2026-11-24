using System.IO.Abstractions;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Runs the delegate command (Antiphon.Nightly.DelegateCli, which replaced scripts/delegate.ps1 in unit 28 of the 2026-11-22
/// plan) in process as the CLI does, on a test transport (normally <see cref="RecordingOperatorHttp"/>): the parse and the run
/// on a host whose output and error are captured, a usage error answered as exit 2 with its message on the error text. git
/// answers "not a repository", so the checkout sent as workingDirectory is the working directory. Nothing leaves the process
/// unless the transport does. <see cref="Application.DelegateScriptRunner"/> is the twin that runs over real HTTP.
/// </summary>
internal static class DelegateCommandRun
{
    /// <summary>The fake API base; the transport answers instead.</summary>
    public const string Api = "http://127.0.0.1:17202";

    /// <summary>The working directory the commands run in: not a repository and no workspace marker.</summary>
    public static readonly string WorkingDirectory = Path.Combine(Path.GetTempPath(), "delegate-cli-no-checkout");

    /// <summary>The environment with the fake API and the given task token; nothing else set.</summary>
    public static DelegateCliEnvironment Environment(string taskToken = "", string? workingDirectory = null) => new()
    {
        Api = Api,
        TaskToken = taskToken,
        CapabilityStore = Path.Combine(WorkingDirectory, "no-capabilities"),
        OperatorTokenFile = Path.Combine(WorkingDirectory, "no-operator-token"),
        WorkingDirectory = workingDirectory ?? WorkingDirectory,
    };

    /// <summary>The command line after "delegate", run against http (also the version probe's transport).</summary>
    public static BoardCommandRun.Result Run(NightlyReportHttp http, IEnumerable<string> args, DelegateCliEnvironment? environment = null,
        IFileSystem? files = null, Func<byte[], byte[]>? unprotect = null)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        try
        {
            var options = DelegateCliOptions.Parse(args.ToArray(), environment ?? Environment());
            var host = new NightlyHost
            {
                Clock = TimeProvider.System,
                Git = new FakeGit("fatal: not a git repository (or any of the parent directories): .git", 128).Run,
                Launcher = new RecordingLauncher(),
                Processes = new FakeProcessTable(),
                Probe = new FakeHostProbe(),
                Out = output,
                Error = error,
                Trace = TextWriter.Null,
            };
            var exit = DelegateCli.Run(options, host, files ?? new FileSystem(), http, http, unprotect);
            return new BoardCommandRun.Result(exit, output.ToString(), error.ToString());
        }
        catch (NightlyUsageException e)
        {
            error.WriteLine("usage: " + e.Message);
            return new BoardCommandRun.Result(2, output.ToString(), error.ToString());
        }
    }

    /// <summary>A create answer: a queued worker task with the given extra JSON members (comma-led, or empty).</summary>
    public static string Created(string extra = "") =>
        """{"id":"11111111-1111-1111-1111-111111111111","shortId":"11111111","status":"Queued","modelLevel":"High","warning":null,"agentKind":"ClaudeCode" """
        + extra + "}";
}
