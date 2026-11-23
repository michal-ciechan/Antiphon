using Antiphon.Nightly;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Runs a board API command (unit 24 of the 2026-11-22 plan: capability, model-availability, specialist-routing,
/// pipeline-definition, project, schedule, dispatch-concurrency, routing-pin) in process as the CLI does: the parse and the
/// run on a production host whose output and error are captured, a usage error answered as exit 2 with its message on the
/// error text. Nothing leaves the process unless the transport does.
/// </summary>
internal static class BoardCommandRun
{
    /// <summary>The fake API base the options name; a recording transport answers instead.</summary>
    public const string Api = "http://127.0.0.1:17202/";

    internal sealed record Result(int Exit, string Output, string Error)
    {
        public string All => Output + Error;
    }

    public static Result Run(Func<NightlyHost, int> command)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        try
        {
            var exit = command(NightlyHost.Production(output, error));
            return new Result(exit, output.ToString(), error.ToString());
        }
        catch (NightlyUsageException e)
        {
            error.WriteLine("usage: " + e.Message);
            return new Result(2, output.ToString(), error.ToString());
        }
    }
}
