using System.IO.Abstractions;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Runs the card command (Antiphon.Nightly.CardCli, which replaced scripts/card.ps1 in unit 27 of the 2026-11-22 plan) in
/// process as the CLI does: the parse and the run on a host whose output and error are captured, a usage error answered as
/// exit 2 with its message on the error text. git answers "not a repository", so the checkout sent as cwd is the working
/// directory; diagnose's sleeps advance a fake clock. Nothing leaves the process unless the transport does.
/// </summary>
internal static class CardCommandRun
{
    /// <summary>The fake API base the options name; the transport answers instead.</summary>
    public const string Api = "http://127.0.0.1:17202";

    /// <summary>The working directory the commands run in: not a repository and no workspace marker.</summary>
    public static readonly string WorkingDirectory = Path.Combine(Path.GetTempPath(), "card-cli-no-checkout");

    /// <summary>A host on the given writers: git outside a repository unless git is given, a fake clock whose sleeps advance it.</summary>
    public static NightlyHost Host(TextWriter output, TextWriter error, FakeTimeProvider? clock = null, NightlyGit? git = null)
    {
        var time = clock ?? new FakeTimeProvider(new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));
        return new NightlyHost
        {
            Clock = time,
            Git = git ?? new FakeGit("fatal: not a git repository (or any of the parent directories): .git", 128).Run,
            Launcher = new RecordingLauncher(),
            Processes = new FakeProcessTable(),
            Probe = new FakeHostProbe(),
            Out = output,
            Error = error,
            Trace = TextWriter.Null,
            Sleep = time.Advance,
        };
    }

    /// <summary>The command line after "card", run against http; taskToken is ANTIPHON_TASK_TOKEN, api ANTIPHON_API.</summary>
    public static BoardCommandRun.Result Run(NightlyReportHttp http, IEnumerable<string> args, string? taskToken = null, string api = Api,
        IFileSystem? files = null, string? workingDirectory = null, FakeTimeProvider? clock = null, NightlyGit? git = null)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        try
        {
            var options = CardCliOptions.Parse(args.ToArray(), api, taskToken, workingDirectory ?? WorkingDirectory);
            var exit = CardCli.Run(options, Host(output, error, clock, git), files ?? new FileSystem(), http);
            return new BoardCommandRun.Result(exit, output.ToString(), error.ToString());
        }
        catch (NightlyUsageException e)
        {
            error.WriteLine("usage: " + e.Message);
            return new BoardCommandRun.Result(2, output.ToString(), error.ToString());
        }
    }
}
