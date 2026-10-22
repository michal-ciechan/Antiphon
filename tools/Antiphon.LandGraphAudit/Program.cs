using System.Runtime.CompilerServices;
using Microsoft.Build.Locator;

namespace Antiphon.LandGraphAudit;

/// <summary>
/// CARD-1483 S1: asks real MSBuild which files the land build reads. Evaluates every project of
/// the solution the land builds (plan D-2) with no restore and refuses any evaluated item, of any
/// item type, whose FullPath is a skippable docs file (D-3). Exit 0 approved, 1 refused, 2 the
/// evaluation failed (D-8). The tool writes nothing: its whole verdict is stdout and the exit code.
/// </summary>
public static class Program
{
    public const int Approved = 0;
    public const int Refused = 1;
    public const int EvaluationFailed = 2;

    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(300);
    private static readonly TimeSpan SolutionTimeout = TimeSpan.FromSeconds(120);

    public static int Main(string[] args)
    {
        string? repo = null;
        string? solution = null;
        var probe = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--repo" when i + 1 < args.Length: repo = args[++i]; break;
                case "--solution" when i + 1 < args.Length: solution = args[++i]; break;
                case "--api-probe": probe = true; break;
                default: return Fail($"unknown or incomplete argument '{args[i]}'");
            }
        }

        if (repo is null || (solution is null && !probe))
            return Fail("usage: --repo <dir> --solution <file.sln> [--api-probe]");

        using var watchdog = new Timer(_ =>
        {
            Console.Out.WriteLine($"evaluation-failed: the audit exceeded {ToolTimeout.TotalSeconds:0} s");
            Environment.Exit(EvaluationFailed);
        }, null, ToolTimeout, Timeout.InfiniteTimeSpan);

        try
        {
            repo = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repo));
            if (!Directory.Exists(repo))
                return Fail($"repository '{repo}' does not exist");

            // global.json is resolved from the audited repo, exactly as the land's dotnet build would.
            var instance = MSBuildLocator.QueryVisualStudioInstances(new VisualStudioInstanceQueryOptions
            {
                DiscoveryTypes = DiscoveryType.DotNetSdk,
                WorkingDirectory = repo,
            }).FirstOrDefault();
            if (instance is null)
                return Fail($"MSBuildLocator found no .NET SDK for '{repo}'");
            MSBuildLocator.RegisterInstance(instance);

            if (probe)
                return Evaluation.Probe(instance.Version.ToString(), Console.Out);

            var audit = Task.Run(() => Evaluation.Audit(repo, solution!, instance.MSBuildPath, Console.Out));
            if (!audit.Wait(SolutionTimeout))
                return Fail($"evaluating {solution} exceeded {SolutionTimeout.TotalSeconds:0} s");
            return audit.Result;
        }
        catch (Exception ex)
        {
            return Fail(Describe(ex));
        }
    }

    internal static int Fail(string message)
    {
        Console.Out.WriteLine($"evaluation-failed: {OneLine(message)}");
        return EvaluationFailed;
    }

    internal static string Describe(Exception ex) =>
        ex is AggregateException { InnerExceptions.Count: 1 } aggregate
            ? Describe(aggregate.InnerExceptions[0])
            : $"{ex.GetType().Name}: {ex.Message}";

    internal static string OneLine(string text) =>
        string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}

/// <summary>
/// Every Microsoft.Build type lives here, behind NoInlining entry points, so none is loaded before
/// MSBuildLocator has registered the SDK's copy.
/// </summary>
internal static class Evaluation
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Probe(string sdkVersion, TextWriter output) => ApiProbe.Run(sdkVersion, output);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Audit(string repo, string solution, string sdkDirectory, TextWriter output) =>
        SolutionAudit.Run(repo, solution, sdkDirectory, output);
}
