using System.Collections;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Antiphon.Nightly;
using Microsoft.Extensions.Time.Testing;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S19: one nightly run's world for NightlyRun.Run in-process, the twin of the scripts/test-nightly-run.ps1
/// fixture: New-RunFx (an owned clone, a state root, a private coordination root), Write-C487Seams (a trace file the host
/// and the fakes append to: "GIT ..." for each git call, "EXEC ..." for each launch; a git answering the fixture sha; every
/// launch passing; every pid alive) and Invoke-FxRun (the run's defaults: -NoReport, scheduled master). The report is a
/// stand-in that records its call ("EXEC antiphon-scripts nightly-report ...", as the StartProcess seam saw the report hop)
/// and answers ReportExit. This process's start time is set, so the shared native lock names one (the self hop reads it).
/// </summary>
public sealed class NightlyRunWorld : IDisposable
{
    /// <summary>The fixture sha (the git seam's answer).</summary>
    public static readonly string Sha = FakeGit.DefaultSha;

    /// <summary>This process's start in the fake process table.</summary>
    public static readonly DateTime ProcessStart = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);

    /// <summary>The CLI the tests hop names (the staged antiphon-scripts build; the recording launcher starts nothing).</summary>
    public static string StagedCli => Path.Combine(AppContext.BaseDirectory, "antiphon-scripts", "Antiphon.Scripts.dll");

    private readonly object _trace = new();

    public NightlyRunWorld(string prefix = "s19-run")
    {
        Fixture = NightlyFixture.Create(prefix);
        State = Fixture.PathOf("state");
        Directory.CreateDirectory(State);
        Clone = Fixture.NewOwnedClone();
        TracePath = Fixture.PathOf("trace.log");
        Coordination = Fixture.PathOf("coord");
        Releases = Fixture.PathOf("releases");
        Launcher = new RecordingLauncher(Answer);
        Processes.Start(Processes.CurrentPid, ProcessStart);
    }

    public NightlyFixture Fixture { get; }
    public string Root => Fixture.Root;
    public string State { get; }
    public string Logs => Path.Combine(State, "logs");
    public string Clone { get; }
    public string TracePath { get; }
    public string Coordination { get; }
    public string Releases { get; }
    public string LastRunPath => Path.Combine(State, "last-run.json");
    public string LastGreenPath => Path.Combine(State, "last-complete-green.json");

    /// <summary>The git seam (Write-C487Seams -GitOutput/-GitExit: new FakeGit(output, exit)).</summary>
    public FakeGit Git { get; set; } = new();

    /// <summary>A git used instead of Git, untraced (the real git of the bootstrap and line-ending cases).</summary>
    public NightlyGit? RealGit { get; set; }

    public FakeProcessTable Processes { get; } = new();
    public FakeHostProbe Probe { get; } = new();
    public RecordingLauncher Launcher { get; }

    /// <summary>The run's clock (the UtcNow seam): the system clock unless a case pins one.</summary>
    public TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>The launch answer after the EXEC line (null: every launch passes).</summary>
    public Func<NightlyLaunch, NightlyLaunchResult>? Behaviour { get; set; }

    /// <summary>The report stand-in's exit.</summary>
    public int ReportExit { get; set; }

    /// <summary>The report calls the stand-in recorded.</summary>
    public List<NightlyReportOptions> Reports { get; } = [];

    /// <summary>A report used instead of the stand-in.</summary>
    public Func<NightlyReportOptions, int>? Report { get; set; }

    /// <summary>The product root the run reads (null: the checkout these tests were built from).</summary>
    public string? ProductRoot { get; set; }

    /// <summary>The selection attempt writer (null: NightlyRun.SelectionWorker over the staged CLI).</summary>
    public Func<IDictionary, NightlySelectionBinding>? SelectionAttempts { get; set; }

    /// <summary>The trace file's text.</summary>
    public string TraceText => File.Exists(TracePath) ? File.ReadAllText(TracePath) : "";

    /// <summary>The host's output lines.</summary>
    public string[] OutLines => Fixture.Out.ToString().Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

    /// <summary>A clock pinned at an instant (the C545 UtcNow seam).</summary>
    public static TimeProvider At(string utc) => new FakeTimeProvider(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture));

    public NightlyHost Host() => new()
    {
        Clock = Clock,
        Git = RealGit ?? ((directory, arguments) =>
        {
            Append("GIT " + string.Join(" ", arguments));
            return Git.Run(directory, arguments);
        }),
        Launcher = Launcher,
        Processes = Processes,
        Probe = Probe,
        Out = Fixture.Out,
        Error = Fixture.Error,
        Trace = new WorldTrace(this),
    };

    /// <summary>The bindings of a seamed run: the staged CLI, the report stand-in, the shadow evidence recorder suppressed.</summary>
    public NightlyRunBindings Bindings() => new()
    {
        CliPath = StagedCli,
        ProductRoot = ProductRoot,
        SelectionAttempts = SelectionAttempts,
        Report = Report ?? (options =>
        {
            Reports.Add(options);
            var line = "EXEC antiphon-scripts nightly-report --summary " + options.Summary;
            if (!string.IsNullOrWhiteSpace(options.PreviousSummary)) line += " --previous-summary " + options.PreviousSummary;
            Append(line);
            return ReportExit;
        }),
        RcEvidence = new RcRunEvidenceRecorder(Environment.GetEnvironmentVariable("ANTIPHON_RC_RUN_EVIDENCE"), true, false, null),
    };

    /// <summary>Invoke-FxRun's options: the clone, the state and log roots, -NoReport, scheduled master, the private coordination root.</summary>
    public NightlyRunOptions Options(Action<NightlyRunOptions>? extra = null)
    {
        var options = new NightlyRunOptions
        {
            CheckoutRoot = Clone, StateRoot = State, LogRoot = Logs, NoReport = true, Trigger = "scheduled", Ref = "master", CoordinationRoot = Coordination,
        };
        extra?.Invoke(options);
        return options;
    }

    /// <summary>Invoke-FxRun: Invoke-AntiphonNightlyRun of these options (the completion record ends the host's output).</summary>
    public NightlyRunResult Run(Action<NightlyRunOptions>? extra = null) => NightlyRun.Run(Options(extra), Host(), Bindings());

    /// <summary>
    /// The C545 StartProcess seam: a nightly-tests hop writes summary.json into its --log-root and exits exitCode; every
    /// other launch passes.
    /// </summary>
    public static Func<NightlyLaunch, NightlyLaunchResult> TestsHop(string summaryJson, int exitCode) => launch =>
    {
        var args = launch.Arguments ?? [];
        var log = Array.IndexOf(args.ToArray(), "--log-root");
        if (args.Contains("nightly-tests") && log >= 0 && log + 1 < args.Count)
        {
            Directory.CreateDirectory(args[log + 1]);
            File.WriteAllText(Path.Combine(args[log + 1], "summary.json"), summaryJson);
            return NightlyTestsWorld.Result(exitCode);
        }
        return NightlyTestsWorld.Result(0);
    };

    /// <summary>The C545 summary: complete, the given tests verdict, delivered, policy hash p.</summary>
    public static string C545Summary(bool testsPassed) =>
        "{\"coverageComplete\":true,\"testsPassed\":" + (testsPassed ? "true" : "false") + ",\"reportDelivered\":true,\"policyHash\":\"p\"}";

    /// <summary>The JSON document at the path, or null.</summary>
    public static JsonElement? Json(string path)
    {
        if (!File.Exists(path)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    /// <summary>A real git in this directory (the fixture's own repositories); a nonzero exit throws with the output.</summary>
    public static string FixtureGit(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var name in new[] { "GIT_AUTHOR_DATE", "GIT_COMMITTER_DATE" }) start.Environment[name] = "2026-11-01T00:00:00Z";
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(directory);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("fixture git failed: " + string.Join(" ", arguments) + "\n" + output + errors.Result);
        return output;
    }

    public void Dispose() => Fixture.Dispose();

    private NightlyLaunchResult Answer(NightlyLaunch launch)
    {
        Append("EXEC " + launch.CommandText);
        return Behaviour?.Invoke(launch) ?? NightlyTestsWorld.Result(0);
    }

    private void Append(string line)
    {
        lock (_trace) File.AppendAllText(TracePath, line + Environment.NewLine, Encoding.ASCII);
    }

    // The host's trace lines (Write-NightlyTrace) into the same file, under the same lock as the fakes' lines.
    private sealed class WorldTrace(NightlyRunWorld world) : TextWriter
    {
        public override Encoding Encoding => Encoding.ASCII;

        public override void Write(char value)
        {
            lock (world._trace) File.AppendAllText(world.TracePath, value.ToString(), Encoding.ASCII);
        }

        public override void WriteLine(string? value) => world.Append(value ?? "");
    }
}
