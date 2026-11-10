using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Build.Locator;

namespace Antiphon.LandGraphAudit;

/// <summary>
/// CARD-1483 S1: asks real MSBuild which files the land build reads. Evaluates every project of
/// the solution the land builds (plan D-2) with no restore and refuses any evaluated item, of any
/// item type, whose FullPath is a skippable docs file (D-3). Exit 0 approved, 1 refused, 2 the
/// evaluation failed (D-8). The tool writes nothing into the repository: its whole verdict is stdout
/// and the exit code.
/// The differential (CanaryRepository, EvaluationSnapshot.Difference) evaluates the solution a second
/// time in a disposable sibling byte copy. The copy set is the tracked files, the untracked files Git
/// does not ignore and every docs/**/*.md (rewritten to the canary text), never the root README.md and
/// never a git-ignored file, so the second pass sees a clean checkout. Its properties, imports and item
/// rows must equal the repository pass. The only tolerated row is an item the repository pass has and
/// the copy lacks entirely whose FullPath is an existing in-repository file, reached through no link,
/// outside the copy set: an ignored file a local run left in a default glob (E2E TestOutput/). An item
/// gated on an ignored file still refuses, because the copy evaluates the clean state. Rows hold names,
/// never bytes, and the expression policy admits no filesystem function, so content is out of scope.
/// </summary>
public static class Program
{
    public const int Approved = 0;
    public const int Refused = 1;
    public const int EvaluationFailed = 2;

    /// <summary>API probe only. Not an evaluation and not an approval. The audit script treats every non-zero code as refusal.</summary>
    public const int ProbeOnly = 3;

    /// <summary>The first argument is a command word this build does not know: the tool and its caller differ.</summary>
    public const int UnknownCommand = 64;

    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(EvaluationBudget.ToolCeilingSeconds);

    public static int Main(string[] args)
    {
        // The emulation pre-check (scripts/audit-land-verification-precheck.ps1 runs it). It loads no MSBuild,
        // evaluates nothing and is not an approval: the graph script still requires the evaluation below.
        if (args.Length > 0 && args[0] == "precheck") return Precheck(args);
        // The graph audit (scripts/audit-land-verification-graph.ps1 runs it): LandGraphAuditGate, with the pre-check
        // in this process and the evaluation below as a child process of this one. Dispatched before MSBuild is located;
        // its exit 0 is the gate's approval after the child's evaluation approved, never an evaluation of its own.
        if (args.Length > 0 && args[0] == "audit") return Audit(args);
        // A command word this build does not know means the calling script is newer than this build (or
        // older): say so with its own exit code instead of an argument error that reads like a usage fault.
        if (args.Length > 0 && args[0].Length > 0 && !args[0].StartsWith('-'))
        {
            Console.Error.WriteLine($"unknown command '{args[0]}': this Antiphon.LandGraphAudit build does not match the "
                + "calling script; rebuild it (scripts/lib/land-audit-tool.ps1 rebuilds the default tool automatically)");
            return UnknownCommand;
        }
        AppDomain.CurrentDomain.ProcessExit += static (_, _) => CanaryRepository.DeleteActive();
        if (PrintBudget(args, out var budgetExit)) return budgetExit;
        // --print-pins is not an evaluation and not an approval. Exit 0 here only prints rows.
        if (PrintPins(args, out var pinsExit)) return pinsExit;

        string? repo = null;
        string? solution = null;
        string? canaryRecord = null;
        var probe = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--repo" when i + 1 < args.Length: repo = args[++i]; break;
                case "--solution" when i + 1 < args.Length: solution = args[++i]; break;
                case "--api-probe": probe = true; break;
                case "--canary-record" when i + 1 < args.Length: canaryRecord = args[++i]; break;
                default: return Fail($"unknown or incomplete argument '{args[i]}'");
            }
        }

        if (repo is null || (solution is null && !probe))
            return Fail("usage: --repo <dir> --solution <file.sln>");

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
            if (!probe)
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
            {
                // Members are reported on stderr. Exit 0 is reserved for a finished graph evaluation.
                var probed = Evaluation.Probe(instance.Version.ToString(), Console.Error);
                if (probed == Approved)
                {
                    Console.Error.WriteLine("api-probe: not a graph evaluation");
                    Console.Error.Flush();
                    return ProbeOnly;
                }

                return probed;
            }

            return Evaluation.Audit(repo, solution!, instance.MSBuildPath, Console.Out);
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

    /// <summary>
    /// <c>precheck --repo &lt;dir&gt; (--check | --update) [--manifest-path &lt;file&gt;]</c>: the exit code and the text of
    /// <see cref="LandGraphPrecheck.Run"/>, written as UTF-8. 0 approved or matched, 1 mismatch, refusal or bad
    /// usage, 2 git failure.
    /// </summary>
    private static int Precheck(string[] args)
    {
        using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        using var stderr = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true };
        string? repo = null;
        string? manifest = null;
        var check = false;
        var update = false;
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--repo" when i + 1 < args.Length: repo = args[++i]; break;
                case "--manifest-path" when i + 1 < args.Length: manifest = args[++i]; break;
                case "--check": check = true; break;
                case "--update": update = true; break;
                default:
                    stderr.WriteLine($"unknown or incomplete argument '{args[i]}'");
                    return LandGraphPrecheck.Refused;
            }
        }

        if (repo is null)
        {
            stderr.WriteLine("usage: precheck --repo <dir> (--check | --update) [--manifest-path <file>]");
            return LandGraphPrecheck.Refused;
        }

        return LandGraphPrecheck.Run(repo, check, update, manifest, stdout, stderr);
    }

    /// <summary>
    /// <c>audit --repo &lt;dir&gt; --tool-path &lt;dll&gt; [--check] [--update] [--land-refresh]
    /// [--evaluation-budget-seconds &lt;n&gt;] [--head &lt;sha&gt;] [--parent-pid &lt;pid&gt;]</c>: the graph script's
    /// body, <see cref="LandGraphAuditGate.Run"/> (0 approved, 1 refused), writing to this process's own streams.
    /// The script passes its switches as given (the gate refuses bad combinations with the script's messages),
    /// the resolved tool DLL as the evaluator, and its own process id: when that process ends, this one ends too,
    /// as the script's own body did.
    /// </summary>
    private static int Audit(string[] args)
    {
        // pwsh reads a redirected child and writes its own output in the console code page; so does the gate.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string? repo = null;
        string? toolPath = null;
        string? head = null;
        var check = false;
        var update = false;
        var landRefresh = false;
        var budget = 0;
        int? parent = null;
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--repo" when i + 1 < args.Length: repo = args[++i]; break;
                case "--tool-path" when i + 1 < args.Length: toolPath = args[++i]; break;
                case "--head" when i + 1 < args.Length: head = args[++i]; break;
                case "--evaluation-budget-seconds" when i + 1 < args.Length
                    && int.TryParse(args[i + 1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var seconds):
                    budget = seconds;
                    i++;
                    break;
                case "--parent-pid" when i + 1 < args.Length
                    && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid):
                    parent = pid;
                    i++;
                    break;
                case "--check": check = true; break;
                case "--update": update = true; break;
                case "--land-refresh": landRefresh = true; break;
                default:
                    Console.Error.WriteLine($"audit refused: unknown or incomplete argument '{args[i]}'");
                    return LandGraphAuditGate.Refused;
            }
        }

        if (repo is null || toolPath is null)
        {
            Console.Error.WriteLine("audit refused: usage: audit --repo <dir> --tool-path <dll> (--check | --update) [--land-refresh]");
            return LandGraphAuditGate.Refused;
        }

        if (parent is int parentId) EndWithParent(parentId);
        return LandGraphAuditGate.Run(new LandGraphAuditGate.Options(repo, check, update, landRefresh, toolPath, budget, head),
            Console.Out, Console.Error);
    }

    // The audit ran inside the script's process before: once that process was gone, no later step ran.
    private static void EndWithParent(int parentId)
    {
        Process parent;
        try
        {
            parent = Process.GetProcessById(parentId);
        }
        catch (ArgumentException)
        {
            Environment.Exit(LandGraphAuditGate.Refused);
            return;
        }

        new Thread(() =>
        {
            try { parent.WaitForExit(); }
            catch (Exception) { }
            Environment.Exit(LandGraphAuditGate.Refused);
        }) { IsBackground = true }.Start();
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
        var copySeconds = EvaluationBudget.CopySeconds(copyMs, windows ?? OperatingSystem.IsWindows());
        var total = EvaluationBudget.TotalSeconds(passSeconds, EvaluationBudget.Passes, copySeconds);
        var grace = EvaluationBudget.StartupGraceSeconds;
        Console.Out.WriteLine($"pass={passSeconds.ToString(CultureInfo.InvariantCulture)}");
        Console.Out.WriteLine($"copy={copySeconds.ToString(CultureInfo.InvariantCulture)}");
        Console.Out.WriteLine($"total={total.ToString(CultureInfo.InvariantCulture)}");
        Console.Out.WriteLine($"grace={grace.ToString(CultureInfo.InvariantCulture)}");
        Console.Out.WriteLine($"ceiling={(total + grace).ToString(CultureInfo.InvariantCulture)}");
        return true;
    }

    private static bool PrintPins(string[] args, out int exitCode)
    {
        exitCode = Approved;
        if (args.Length == 0 || args[0] != "--print-pins") return false;
        string? repo = null;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--repo" && i + 1 < args.Length)
            {
                repo = args[++i];
                continue;
            }

            exitCode = Fail($"unknown or incomplete argument '{args[i]}'");
            return true;
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            exitCode = Fail("usage: --print-pins --repo <dir>");
            return true;
        }

        repo = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repo));
        if (!Directory.Exists(repo))
        {
            exitCode = Fail($"repository '{repo}' does not exist");
            return true;
        }

        TargetPins.Print(repo, Console.Out);
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
    public static int Audit(string repo, string solution, string sdkDirectory, TextWriter output) =>
        SolutionAudit.Run(repo, solution, sdkDirectory, output);
}
