using System.Globalization;
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

    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(EvaluationBudget.ToolCeilingSeconds);

    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.ProcessExit += static (_, _) => CanaryRepository.DeleteActive();
        if (PrintBudget(args, out var budgetExit)) return budgetExit;

        string? repo = null;
        string? solution = null;
        string? canaryRecord = null;
        string? touch = null;
        var probe = false;
        var witness = false;
        int? passBudgetMs = null;
        int? copyBudgetMs = null;
        int? holdPass1Ms = null;
        int? holdCopyMs = null;
        int? holdPass2Ms = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--repo" when i + 1 < args.Length: repo = args[++i]; break;
                case "--solution" when i + 1 < args.Length: solution = args[++i]; break;
                case "--api-probe": probe = true; break;
                case "--canary-record" when i + 1 < args.Length: canaryRecord = args[++i]; break;
                case "--witness-clocks": witness = true; break;
                case "--touch-canary" when i + 1 < args.Length: touch = args[++i]; break;
                case "--pass-budget-ms" when TakeMilliseconds(args, ref i, out var passMs): passBudgetMs = passMs; break;
                case "--copy-budget-ms" when TakeMilliseconds(args, ref i, out var copyMs): copyBudgetMs = copyMs; break;
                case "--hold-pass1-ms" when TakeMilliseconds(args, ref i, out var hold1): holdPass1Ms = hold1; break;
                case "--hold-copy-ms" when TakeMilliseconds(args, ref i, out var holdCopy): holdCopyMs = holdCopy; break;
                case "--hold-pass2-ms" when TakeMilliseconds(args, ref i, out var hold2): holdPass2Ms = hold2; break;
                default: return Fail($"unknown or incomplete argument '{args[i]}'");
            }
        }

        if (repo is null || (solution is null && !probe))
            return Fail("usage: --repo <dir> --solution <file.sln> [--api-probe]");
        WitnessRequest? clocks = null;
        if (witness)
        {
            if (probe || passBudgetMs is null || copyBudgetMs is null || holdPass1Ms is null || holdCopyMs is null || holdPass2Ms is null
                || passBudgetMs < 1 || copyBudgetMs < 1)
                return Fail("witness clocks require --repo, --solution, --pass-budget-ms, --copy-budget-ms, --hold-pass1-ms, --hold-copy-ms and --hold-pass2-ms");
            clocks = new WitnessRequest(
                TimeSpan.FromMilliseconds(passBudgetMs.Value),
                TimeSpan.FromMilliseconds(copyBudgetMs.Value),
                TimeSpan.FromMilliseconds(holdPass1Ms.Value),
                TimeSpan.FromMilliseconds(holdCopyMs.Value),
                TimeSpan.FromMilliseconds(holdPass2Ms.Value),
                touch);
        }
        else if (passBudgetMs is not null || copyBudgetMs is not null || holdPass1Ms is not null || holdCopyMs is not null || holdPass2Ms is not null || touch is not null)
            return Fail("phase witness arguments require --witness-clocks");

        if (!string.IsNullOrEmpty(canaryRecord))
            CanaryRepository.RecordPath = Path.GetFullPath(canaryRecord);

        using var watchdog = new Timer(_ =>
        {
            CanaryRepository.DeleteActive();
            Console.Out.WriteLine($"evaluation-failed: the audit exceeded {ToolTimeout.TotalSeconds:0} s");
            Console.Out.Flush();
            Environment.Exit(EvaluationFailed);
        }, null, ToolTimeout, Timeout.InfiniteTimeSpan);

        try
        {
            repo = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repo));
            if (!Directory.Exists(repo))
                return Fail($"repository '{repo}' does not exist");

            // This static gate runs before loading MSBuild. A matching pin is only permission
            // to continue to the unchanged evaluation checks, never sufficient for approval.
            if (!probe && clocks is null)
            {
                var pinRefusals = new SortedSet<string>(StringComparer.Ordinal);
                TargetPins.Check(repo, pinRefusals);
                if (pinRefusals.Count > 0)
                {
                    foreach (var refusal in pinRefusals) Console.Out.WriteLine(refusal);
                    return Refused;
                }
            }

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

            return Evaluation.Audit(repo, solution!, instance.MSBuildPath, Console.Out, clocks);
        }
        catch (Exception ex)
        {
            return Fail(Describe(ex));
        }
        finally
        {
            CanaryRepository.DeleteActive();
        }
    }

    private static bool TakeMilliseconds(string[] args, ref int index, out int value)
    {
        value = 0;
        if (index + 1 >= args.Length) return false;
        return int.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 0;
    }

    private static bool PrintBudget(string[] args, out int exitCode)
    {
        exitCode = Approved;
        if (args.Length == 0 || args[0] != "--print-budget") return false;
        int? measuredPass = null;
        int? measuredCopy = null;
        bool? windows = null;
        for (var i = 1; i < args.Length; i++)
        {
            if (i + 1 >= args.Length)
            {
                exitCode = Fail($"unknown or incomplete argument '{args[i]}'");
                return true;
            }
            var value = args[++i];
            switch (args[i - 1])
            {
                case "--measured-pass-p95-ms" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var pass):
                    measuredPass = pass; break;
                case "--measured-copy-p95-ms" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var copy):
                    measuredCopy = copy; break;
                case "--windows" when value is "true" or "false":
                    windows = value == "true"; break;
                default:
                    exitCode = Fail($"unknown or incomplete argument '{args[i - 1]}'");
                    return true;
            }
        }

        var passMs = measuredPass ?? EvaluationBudget.MeasuredLoadedPassP95Milliseconds;
        var copyMs = measuredCopy ?? EvaluationBudget.MeasuredCopyP95Milliseconds;
        var passSeconds = EvaluationBudget.PassSeconds(passMs, windows ?? OperatingSystem.IsWindows());
        var copySeconds = EvaluationBudget.CopySeconds(copyMs);
        var total = EvaluationBudget.TotalSeconds(passSeconds, EvaluationBudget.Passes, copySeconds);
        var grace = EvaluationBudget.StartupGraceSeconds;
        Console.Out.WriteLine($"pass={passSeconds.ToString(CultureInfo.InvariantCulture)}");
        Console.Out.WriteLine($"copy={copySeconds.ToString(CultureInfo.InvariantCulture)}");
        Console.Out.WriteLine($"total={total.ToString(CultureInfo.InvariantCulture)}");
        Console.Out.WriteLine($"grace={grace.ToString(CultureInfo.InvariantCulture)}");
        Console.Out.WriteLine($"ceiling={(total + grace).ToString(CultureInfo.InvariantCulture)}");
        return true;
    }

    internal static int Fail(string message)
    {
        Console.Out.WriteLine($"evaluation-failed: {OneLine(message)}");
        Console.Out.Flush();
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
    public static int Audit(string repo, string solution, string sdkDirectory, TextWriter output, WitnessRequest? witness = null) =>
        SolutionAudit.Run(repo, solution, sdkDirectory, output, witness);
}
