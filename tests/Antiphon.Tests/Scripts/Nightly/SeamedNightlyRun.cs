using System.Collections;
using System.Globalization;
using Antiphon.Nightly;
using Antiphon.Tests.Application;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// The nightly tests run (NightlyTests.Run) in this process on a driver's seams: the C# twin of the C487 harness's seamed
/// run (New-C487NightlyTestsHost, New-C487NightlyTestsBindings and Invoke-C487NightlyTests of
/// scripts/lib/c487-nightly-tests.ps1, which unit 62 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md
/// deleted with scripts/lib/c487-harness.ps1). The callers ran it in a pwsh child on a seams file; each seam is a delegate
/// here, bound as the harness bound it:
///   StartProcess -> the launcher (DelegateLauncher: a record is read as NightlyLaunchResult.FromRecord reads it, its
///   LogText becomes the launch's log); HostPlatform, DockerInfo, DiskFree, ToolchainVersions -> the probe (under
///   StartProcess without DockerInfo the Docker row is 'live docker skipped under seams-default'; an unset seam asks the
///   real probe); git and the clock are the real ones (no seam named them); E2EPrerequisites, PublicationScope,
///   ExecutedContext -> the bindings of the same names; the evidence recorder is suppressed, as under the process seam
///   (unless ANTIPHON_RC_RUN_EVIDENCE is 'record'); the active-chunk identity is the heartbeat's; the class-cost file is
///   this checkout's scripts/lib/nightly-class-costs.json unless ClassCostPath names one.
/// Evidence writes are not deferred: the harness deferred them and flushed before every launch, so a launch saw the same
/// files either way, and the process-wide write-behind would leak into the cases running beside this one.
/// </summary>
internal sealed class SeamedNightlyRun
{
    /// <summary>The StartProcess seam: answers each launch with a record (NightlyLaunchResult, PlainObject or dictionary).</summary>
    public required Func<NightlyLaunch, object> StartProcess { get; init; }

    public Func<string>? HostPlatform { get; init; }

    public Func<NightlyProbeRow>? DockerInfo { get; init; }

    public Func<NightlyProbeRow>? DiskFree { get; init; }

    public Func<string, string>? ToolchainVersions { get; init; }

    public Func<string, string, IDictionary, string, object>? E2EPrerequisites { get; init; }

    public Func<string, TestPhasePublicationScope>? PublicationScope { get; init; }

    public Action<TestPhaseJournalContext>? ExecutedContext { get; init; }

    /// <summary>The environment the run reads (null: this process's), as a pwsh child's $env: changes were seen.</summary>
    public Func<string, string?>? Variables { get; init; }

    /// <summary>The class-cost file ("": this checkout's scripts/lib/nightly-class-costs.json).</summary>
    public string ClassCostPath { get; init; } = "";

    /// <summary>The run's output and error lines.</summary>
    public StringWriter Output { get; } = new();

    /// <summary>The seams' answer to the DockerInfo and DiskFree probes of the C487 drivers: Docker 'test'/'seam', 50 GB free of 10 GB.</summary>
    public static NightlyProbeRow DockerSeam() => new() { ExitCode = 0, Version = "test", Detail = "seam" };

    public static NightlyProbeRow DiskSeam() => new() { ExitCode = 0, FreeBytes = 50L << 30, MinimumFreeBytes = 10L << 30 };

    /// <summary>A launch record with the fields the seams returned: exit, timeout, children exited and cleanup complete, pid.</summary>
    public static NightlyLaunchResult Answer(int exitCode = 0, bool timedOut = false, int pid = 1) =>
        new() { ExitCode = exitCode, TimedOut = timedOut, ChildrenExited = true, CleanupComplete = true, Pid = pid, LogText = "" };

    /// <summary>The value of a flag in an argv ("" when it is absent or last).</summary>
    public static string Argument(IReadOnlyList<string>? arguments, string name)
    {
        var items = arguments ?? [];
        for (var i = 0; i + 1 < items.Count; i++)
            if (items[i] == name) return items[i + 1];
        return "";
    }

    /// <summary>The host of the run (Invoke-C487NightlyTests' New-C487NightlyTestsHost over these seams).</summary>
    public NightlyHost Host()
    {
        var output = TextWriter.Synchronized(Output);
        var docker = DockerInfo ?? (() => new NightlyProbeRow { ExitCode = 0, Version = "test", Detail = "live docker skipped under seams-default" });
        return new NightlyHost
        {
            Clock = TimeProvider.System,
            Git = NightlyNativeGit.Run,
            Launcher = new DelegateLauncher(StartProcess, null, null, output),
            Processes = new NightlyProcessTable(),
            Probe = new DelegateHostProbe(HostPlatform, docker, DiskFree, ToolchainVersions, null),
            Out = output,
            Error = output,
            Trace = TextWriter.Null,
            Variables = Variables is null ? Environment.GetEnvironmentVariable : name => Variables(name),
        };
    }

    /// <summary>The bindings of the run (New-C487NightlyTestsBindings).</summary>
    public NightlyTestsBindings Bindings(NightlyHost host)
    {
        var cost = ClassCostPath.Length > 0 ? ClassCostPath : Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "nightly-class-costs.json");
        return new NightlyTestsBindings
        {
            RcEvidence = new RcRunEvidenceRecorder(host.Variables("ANTIPHON_RC_RUN_EVIDENCE"), true, false,
                line => host.Out.WriteLine("[" + host.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "] " + line)),
            E2EPrerequisites = E2EPrerequisites,
            ActiveChunk = QualificationHeartbeat.ActiveChunkWriter(text => host.Error.WriteLine("WARNING: " + text)),
            PublicationScope = PublicationScope,
            ExecutedContext = ExecutedContext,
            ClassCostPath = cost,
        };
    }

    /// <summary>
    /// The run of these options: its result and the process exit the pwsh child had (the result's ProcessExitCode; 1 when
    /// the run threw, its message then on the output).
    /// </summary>
    public (NightlyTestsResult? Result, int ExitCode) Run(NightlyTestsOptions options)
    {
        var host = Host();
        try
        {
            var result = NightlyTests.Run(options, host, Bindings(host));
            return (result, result.ProcessExitCode);
        }
        catch (Exception ex)
        {
            host.Error.WriteLine("nightly-tests threw: " + ex);
            return (null, 1);
        }
    }
}
