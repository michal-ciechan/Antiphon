using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Checkpoints;

/// <summary>
/// The row verb: one row of a plan's ### Checkpoints table, one build (unless --no-build) and one exact filter into a fresh
/// results directory, then the CHECKPOINT report line, the FAILED/EXECUTED/ROSTER MISS roster and the
/// <c>CHECKPOINT &lt;name&gt; EXIT CODE: &lt;n&gt;</c> trailer, always the last line. Since unit 20 of
/// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md scripts/run-checkpoint.ps1 is its thin launcher, so this
/// verb holds that script's contract (docs/testing-and-build.md, "Checkpoint manifest (CARD-0585)"):
/// <list type="bullet">
/// <item>Input checks before any directory, source read or slot: the bin-&lt;name&gt;/ output path (CARD-0448), a positive
/// repeat whose product with --min-executed fits an integer, Name=Value properties that set neither the output path nor
/// the reserved repeat properties (CARD-0671), one TUnit csproj for a repeat (CARD-0885) and a full object id for
/// --expected-source-sha. Each refusal is <c>CHECKPOINT &lt;name&gt; invalid input: ...</c>, exit 2.</item>
/// <item>A fresh results directory (an existing one is refused), then the source snapshot: an unknown one, or a dirty or
/// different one under --expected-source-sha, is exit 2 with <c>reason=</c> on the report line (CARD-0835). A strict
/// (--expected-source-sha or --repeat) --no-build row checks its build-source stamp before it waits for a slot.</item>
/// <item>The build slot (CARD-0589) after every input check: the broker's lease, waited for in FIFO order with BUILD SLOT
/// lines; no slot within --slot-wait-minutes (45) is exit 4 with nothing built or run; an unreachable runner falls back
/// to an unleased run at -maxcpucount:4 after 60 s. --no-slot asks the broker nothing and passes no -maxcpucount.</item>
/// <item>The build and run (RowRunner): child output on this console as it arrives, ANTIPHON_TEST_PHASE_* removed from
/// both children before the row's own journal root is set (CARD-1748), UseAppHost=false off Windows unless a property
/// names UseAppHost, no deadline unless --row-timeout gives one.</item>
/// <item>report.json and report.md in the results directory whenever it exists (schema 3 with the repeat evidence).</item>
/// </list>
/// Exit codes: 0 green; 1 failed tests; 2 invalid input, a dirty tracked tree, a detected tracked change, a failed build,
/// no TRX or ineligible source; 3 fewer than --min-executed executed tests or an --expect token that matched nothing;
/// 4 no build slot in time; 5 the --row-timeout expired; 8 reserved legacy code; 130 the caller cancelled.
/// </summary>
internal static class RowCommand
{
    public const string NoSlotLine = "BUILD SLOT skipped by -NoSlot";
    public const string PhaseEnvironmentPrefix = "ANTIPHON_TEST_PHASE_";

    private static readonly Regex PropertyShape = new(@"^[A-Za-z_][A-Za-z0-9_.-]*=", RegexOptions.CultureInvariant);
    private static readonly Regex OutputKeys = new(@"(^|;)\s*(OutputPath|OutDir|BaseOutputPath)\s*=",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex RepeatKeys = new(@"(^|;)\s*AntiphonCheckpointRepeat(Project)?\s*=",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex FullObjectId = new("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.CultureInvariant);
    private static readonly Regex OutputPathGuard = new(ManifestValidator.OutputPathPattern, RegexOptions.CultureInvariant);
    private static readonly HashSet<string> GeneratedLayoutProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "ArtifactsPath", "UseArtifactsOutput", "CustomBeforeDirectoryBuildProps",
        "AntiphonCheckpointRepositoryRoot", CheckpointIntermediateCache.RootProperty,
    };

    public static async Task<int> RunAsync(Program.ArgSet options, string repo, CheckpointApp.Runtime runtime)
    {
        var console = TextWriter.Synchronized(runtime.Output ?? Console.Out);
        var cancellationToken = runtime.CancellationToken;
        var name = Required(options, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            console.WriteLine("refused: manifest-identity: row name is empty");
            return ExitCodes.Invalid;
        }

        var row = new Receipt(console, name, Required(options, "filter"));
        var project = Required(options, "project");
        var outputPath = Required(options, "output-path");
        var platform = runtime.Platform ?? new RuntimePlatform();
        repo = Path.GetFullPath(repo);

        // (1) Inputs. Nothing is created, read or waited for before they pass.
        var invalid = ReadInputs(options, repo, project, outputPath, out var input);
        if (invalid is not null)
            return row.Invalid(invalid);

        // (2) A fresh results directory: a reused one is how a stale TRX gets reported as this run's.
        var resultsRoot = options.Get("results-root") is { Length: > 0 } root ? root : ".antiphon/checkpoints";
        resultsRoot = Path.GetFullPath(Path.IsPathRooted(resultsRoot) ? resultsRoot : Path.Combine(repo, resultsRoot));
        var stamp = runtime.RowStamp?.Invoke()
            ?? DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..4];
        var results = Path.Combine(resultsRoot, name + "-" + stamp);
        if (Directory.Exists(results) || File.Exists(results))
            return row.Invalid("results directory exists: " + results);
        var resultsRefusal = SafeWrite.CreateDirectory(resultsRoot, SafeWrite.Under(resultsRoot))
            ?? SafeWrite.CreateFreshDirectory(results, SafeWrite.Under(resultsRoot));
        if (resultsRefusal is not null)
            return row.Invalid("refused: " + resultsRefusal);
        row.Results = results;
        row.RunId = name + "-" + stamp;
        row.Worktree = repo;

        // (3) Source identity (CARD-0835).
        var capture = runtime.SourceCapture ?? new SourceSnapshot().Capture;
        var start = capture(repo);
        var source = new SourceEvidence { Start = start };
        row.Source = source;
        row.Commit = start.CaptureStatus == "known" && start.Commit is not null ? start.Commit : "unknown";
        if (start.CaptureStatus != "known")
            return row.Fail(ExitCodes.Invalid, "source_unknown");
        if (input.ExpectedSourceSha is not null && (start.DirtyFiles != 0 || start.Commit != input.ExpectedSourceSha))
            return row.Fail(ExitCodes.Invalid, start.DirtyFiles != 0 ? "source_dirty" : "source_mismatch");
        var replay = input.NoBuild
            ? ResolveReplay(input, repo, project, outputPath, start, platform, cancellationToken)
            : null;
        using var replayCache = replay?.Cache;
        if (replay is not null)
        {
            row.Build = "reused";
            source.BuildSource = replay.BuildSource;
            if (replay.Refusal is not null)
                return row.Fail(ExitCodes.Invalid, replay.Refusal);
        }
        var guard = new SourceRunGuard(repo, source, runtime.SourceCapture);
        if (!guard.Observe())
            return row.Fail(ExitCodes.Invalid, guard.Reason);

        // (4) The host build slot (CARD-0589), after every input check so an invalid row never waits.
        SlotLease lease;
        if (input.NoSlot)
        {
            console.WriteLine(NoSlotLine);
            lease = new SlotLease { State = "skipped", MaxCpuCount = 0 };
        }
        else
        {
            var slots = runtime.Slots ?? new BuildSlotClient(
                runtime.SlotHandler ?? new HttpClientHandler(),
                BuildSlotClient.DefaultEndpoint(platform.IsWindows),
                wait: TimeSpan.FromMinutes(input.SlotWaitMinutes),
                clock: runtime.SlotClock,
                delay: runtime.SlotDelay,
                log: SlotLine(console),
                holders: runtime.SlotHolders ?? new ProcessLeaseHolderSource(runtime.SlotStartTimeReader),
                sensitiveToken: runtime.EnvironmentLookup("ANTIPHON_TASK_TOKEN"));
            var session = input.SlotsOff
                ? new SlotSession("off", 4)
                : await slots.ProbeAsync(cancellationToken).ConfigureAwait(false);
            lease = await slots.AcquireAsync(session, name + "@" + project, cancellationToken).ConfigureAwait(false);
        }

        row.Slot = lease.State;
        row.SlotReason = lease.SlotReason;
        row.WaitedSeconds = lease.WaitedSeconds;
        var held = true;
        try
        {
            if (lease.ExitCode == ExitCodes.SlotTimeout)
            {
                console.WriteLine($"CHECKPOINT {name} slot=timeout waited={lease.WaitedSeconds}s: no build slot within "
                    + input.SlotWaitMinutes.ToString(CultureInfo.InvariantCulture) + " minutes; nothing was built or run");
                return row.Fail(ExitCodes.SlotTimeout, null);
            }
            if (lease.ExitCode != ExitCodes.Green)
                return row.Fail(lease.ExitCode, null);

            // (5) Build and run under the slot.
            var request = new RowRequest
            {
                Name = name,
                Project = project,
                OutputPath = outputPath,
                Filter = row.Filter,
                ResultsDirectory = results,
                WorkingDirectory = repo,
                NoBuild = input.NoBuild,
                MinExecuted = input.MinExecuted,
                Repeat = input.Repeat,
                Expect = input.Expect,
                Properties = input.Properties,
                ResolvedBuildProperties = replay?.Properties,
                IntermediateCache = replayCache,
                Commit = row.Commit,
                Source = source,
                SourceBoundary = guard.Observe,
                ExpectedSourceSha = input.ExpectedSourceSha,
                Slot = lease.State,
                SlotReason = lease.SlotReason,
                WaitedSeconds = lease.WaitedSeconds,
                // The grant applies to the build only; --no-slot passes no -maxcpucount at all.
                MaxCpuCount = input.NoSlot ? 0 : lease.MaxCpuCount > 0 ? lease.MaxCpuCount : 4,
                Deadline = input.RowTimeout ?? Timeout.InfiniteTimeSpan,
                TimingRoot = input.TimingRoot ?? runtime.EnvironmentLookup("ANTIPHON_TEST_TIMING_ROOT"),
                TimingRunId = name,
                ProducingBuildId = outputPath,
                ChildOutput = console.WriteLine,
                ClearedEnvironmentPrefixes = [PhaseEnvironmentPrefix],
            };
            var runner = new RowRunner(runtime.Driver ?? new ProcessDriver { DotnetShim = input.DotnetShim }, platform);
            RowRunResult result;
            try
            {
                result = await runner.RunAsync(request, new TrailerFilter(console, name), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                console.WriteLine($"CHECKPOINT {name} driver interruption: {ex.GetType().Name}");
                // No observed end: the source is unknown, never clean.
                source.End = null;
                source.State = "unknown";
                return row.Fail(ExitCodes.Invalid, "source_unknown");
            }

            held = false;
            await lease.DisposeAsync().ConfigureAwait(false);
            return row.Finish(result);
        }
        finally
        {
            if (held)
                await lease.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed record Replay(string BuildSource, string? Refusal = null,
        IReadOnlyList<string>? Properties = null, CheckpointIntermediateCache? Cache = null);

    private static Replay ResolveReplay(Inputs input, string repo, string project, string outputPath,
        SourceObservation source, IPlatform platform, CancellationToken cancellationToken)
    {
        var original = BuildStep.PropertyArguments(input.Properties, platform.IsWindows, input.Repeat, input.CanonicalProject);
        var cold = original.ToList();
        BuildStep.AppendSharedLayout(cold, repo, project, outputPath);
        var coldCheck = CheckpointBuildBinding.Expected(repo, project, outputPath, cold, source).Check();
        var explicitWarmRoot = CheckpointIntermediateCache.UsesManagedRoot(original);
        if (coldCheck == "verified" && !explicitWarmRoot)
            return new Replay(coldCheck, Properties: cold);

        // Explicit producing properties still require the gate if they identify a managed layout.
        // Strip the generated properties for key derivation only after the supplied values match the receipt.
        var warmInputs = explicitWarmRoot && coldCheck == "verified"
            ? BuildStep.PropertyArguments(input.Properties.Where(pair => !GeneratedLayoutProperties.Contains(pair.Key)),
                platform.IsWindows, input.Repeat, input.CanonicalProject)
            : original;
        if (source.DirtyFiles == 0 && !CheckpointIntermediateCache.OverridesLayout(warmInputs))
        {
            // Derive both layouts from the request. A receipt cannot supply paths or MSBuild arguments.
            var warm = warmInputs.ToList();
            var partition = CheckpointIntermediateCache.PathFor(repo, project, warmInputs, source);
            BuildStep.AppendSharedLayout(warm, repo, project, outputPath, partition);
            if (CheckpointBuildBinding.Expected(repo, project, outputPath, warm, source).Check() == "verified")
            {
                var cache = CheckpointIntermediateCache.TryOpen(repo, source, source.Commit, replay: true,
                    cancellationToken: cancellationToken);
                try
                {
                    if (cache?.Prepare(project, warmInputs, requireSealed: true, cancellationToken: cancellationToken) is not null)
                    {
                        var admitted = new Replay("verified", Properties: warm, Cache: cache);
                        cache = null;
                        return admitted;
                    }
                }
                finally { cache?.Dispose(); }
                return new Replay("verified", "build_cache_unavailable");
            }
        }

        // Never forward a managed root through the ordinary unverified cold replay path.
        if (explicitWarmRoot)
            return new Replay(coldCheck, coldCheck == "verified" ? "build_cache_unavailable" : "build_source_mismatch");
        var strict = input.ExpectedSourceSha is not null || input.Repeat > 1;
        return new Replay(coldCheck, strict ? "build_source_mismatch" : null);
    }

    private sealed record Inputs(
        bool NoBuild, int Repeat, int MinExecuted, List<string> Expect, List<KeyValuePair<string, string>> Properties,
        string? CanonicalProject, string? ExpectedSourceSha, double SlotWaitMinutes, bool NoSlot, bool SlotsOff,
        TimeSpan? RowTimeout, string? TimingRoot, string? DotnetShim);

    private static string? ReadInputs(Program.ArgSet options, string repo, string project, string outputPath, out Inputs input)
    {
        input = null!;
        // CARD-0448: a trailing backslash loses itself to Windows argv quoting and leaves a directory whose name ends in a space.
        if (!OutputPathGuard.IsMatch(outputPath))
            return $"OutputPath '{outputPath}' must be bin-<name>/ with a forward slash and no trailing space (CARD-0448 argv hazard: a trailing backslash creates a directory whose name ends in a space)";
        if (!Integer(options.Get("repeat"), 1, out var repeat) || !Integer(options.Get("min-executed"), 1, out var minimum)
            || repeat < 1 || (long)repeat * minimum > int.MaxValue)
            return "Repeat must be a positive integer and Repeat x MinExecuted must fit an integer";

        // CARD-0671: one comma-separated value carries several (pwsh -File binds every argument as a string); a value that
        // needs a comma writes MSBuild's %2C. The output path stays owned by --output-path.
        var properties = new List<KeyValuePair<string, string>>();
        foreach (var token in Tokens(options.GetAll("msbuild-property")))
        {
            if (!PropertyShape.IsMatch(token))
                return $"MsBuildProperty '{token}' must be Name=Value";
            if (OutputKeys.IsMatch(token))
                return $"MsBuildProperty '{token}' may not set the output path; use -OutputPath";
            if (RepeatKeys.IsMatch(token))
                return $"MsBuildProperty '{token}' may not set reserved repeat properties";
            var equals = token.IndexOf('=');
            properties.Add(new KeyValuePair<string, string>(token[..equals], token[(equals + 1)..]));
        }

        string? canonical = null;
        if (repeat > 1)
        {
            try { canonical = RepeatRequest.CanonicalProject(repo, project); }
            catch (ManifestValidationException ex) { return ex.Message; }
        }

        var expected = options.Get("expected-source-sha");
        if (string.IsNullOrWhiteSpace(expected))
            expected = null;
        else if (!FullObjectId.IsMatch(expected))
            return "ExpectedSourceSha must be a normalized full Git object ID";

        var waitText = options.Get("slot-wait-minutes");
        var wait = 45.0;
        if (!string.IsNullOrWhiteSpace(waitText)
            && (!double.TryParse(waitText, NumberStyles.Float, CultureInfo.InvariantCulture, out wait) || !double.IsFinite(wait) || wait <= 0
                || wait * 60_000d > uint.MaxValue - 1d))
            return "SlotWaitMinutes must be a positive number of minutes";

        TimeSpan? rowTimeout = null;
        if (options.Get("row-timeout") is { } timeoutText)
        {
            rowTimeout = Program.ParseDuration(timeoutText);
            if (rowTimeout is not { } span || span <= TimeSpan.Zero || span.TotalDays > 7)
                return "row-timeout must name a positive duration of at most 7 days";
        }

        input = new Inputs(
            options.Has("no-build"), repeat, minimum, ExpectTokens(options.GetAll("expect")), properties, canonical, expected, wait,
            options.Has("no-slot"), options.Get("slots") == "off", rowTimeout,
            options.Get("timing-root") is { Length: > 0 } timing ? timing : null,
            options.Get("dotnet") is { Length: > 0 } shim ? shim : null);
        return null;
    }

    private static bool Integer(string? text, int fallback, out int value)
    {
        value = fallback;
        return text is null || int.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    private static IEnumerable<string> Tokens(IEnumerable<string> values) =>
        values.SelectMany(value => value.Split(',')).Select(token => token.Trim()).Where(token => token.Length > 0);

    // CARD-0615: the edge quotes of a quoted roster ('A','B' or "A","B") are wrapper syntax, never part of a name. An
    // interior quote stays significant.
    internal static List<string> ExpectTokens(IEnumerable<string> values) =>
        values.SelectMany(value => value.Split(','))
            .Select(token => token.Trim().Trim('\'', '"').Trim())
            .Where(token => token.Length > 0)
            .ToList();

    // The broker's busy and memory-floor answers repeat on every retry; the BUILD SLOT waiting line says so once a minute.
    private static Action<string> SlotLine(TextWriter console) => line =>
    {
        if (!line.Contains(" status=409 ", StringComparison.Ordinal))
            console.WriteLine(line);
    };

    private static string Required(Program.ArgSet options, string name) =>
        options.Get(name) ?? throw new ManifestValidationException(name, "missing --" + name);

    // RowRunner ends each outcome with the trailer; this verb prints it itself, last, after the lease is released.
    private sealed class TrailerFilter(TextWriter inner, string name) : TextWriter
    {
        private readonly string _trailer = "CHECKPOINT " + name + " EXIT CODE:";

        public override Encoding Encoding => inner.Encoding;

        public override void WriteLine(string? value)
        {
            if (value is not null && value.StartsWith(_trailer, StringComparison.Ordinal))
                return;
            inner.WriteLine(value);
        }

        public override void Write(char value) => inner.Write(value);

        public override void Write(string? value) => inner.Write(value);
    }

    // The row's report line, its receipts and its trailer.
    private sealed class Receipt(TextWriter console, string name, string filter)
    {
        private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;

        public string Filter { get; } = filter;
        public string? Results { get; set; }
        public string RunId { get; set; } = "";
        public string Worktree { get; set; } = "";
        public string Commit { get; set; } = "unknown";
        public SourceEvidence Source { get; set; } = new();
        public string Build { get; set; } = "n/a";
        public string Slot { get; set; } = "skipped";
        public string? SlotReason { get; set; }
        public int WaitedSeconds { get; set; }

        public int Invalid(string message)
        {
            console.WriteLine($"CHECKPOINT {name} invalid input: {message}");
            return Fail(ExitCodes.Invalid, null);
        }

        public int Fail(int exit, string? reason)
        {
            var line = Line(null, reason);
            console.WriteLine(line);
            return Close(exit, line, reason ?? "invalid", null);
        }

        public int Finish(RowRunResult result)
        {
            Build = string.IsNullOrEmpty(result.BuildState) ? Build : result.BuildState;
            var line = result.Line;
            if (string.IsNullOrEmpty(line))
            {
                // Every outcome is reported as one line (CARD-0585 rule 3), the early ones included.
                line = Line(result, null);
                console.WriteLine(line);
            }
            return Close(result.ExitCode, line, result.State, result);
        }

        private string Line(RowRunResult? result, string? reason)
        {
            var trx = result?.TrxPath ?? (Results is null ? null : Path.Combine(Results, "run.trx"));
            var line = CheckpointLine.Format(new CheckpointLineModel
            {
                Name = name,
                Commit = Commit,
                Build = Build,
                Filter = Filter,
                Executed = result?.Trx?.Executed.ToString(CultureInfo.InvariantCulture),
                Passed = result?.Trx?.Passed.ToString(CultureInfo.InvariantCulture),
                Failed = result?.Trx?.Failed.ToString(CultureInfo.InvariantCulture),
                Skipped = result?.Trx?.Skipped.ToString(CultureInfo.InvariantCulture),
                Trx = trx is null ? null : Path.GetFullPath(trx),
                Slot = Slot,
                SlotReason = SlotReason,
                WaitedSeconds = WaitedSeconds,
                Source = Source,
                Repeat = result?.Repeat,
            });
            return reason is null ? line : line + " reason=" + reason;
        }

        private int Close(int exit, string line, string state, RowRunResult? result)
        {
            if (Results is not null)
            {
                var ended = DateTimeOffset.UtcNow;
                try
                {
                    ReportWriter.WriteFiles(Results, new ReportModel
                    {
                        SchemaVersion = result?.Repeat is null ? 2 : 3,
                        RunId = RunId,
                        Commit = Commit,
                        Worktree = Worktree,
                        Source = Source,
                        Host = new ReportHost
                        {
                            Os = System.Runtime.InteropServices.RuntimeInformation.OSDescription.Trim(),
                            Cores = Environment.ProcessorCount,
                            MemAvailableMb = HostSnapshot.ReadMemAvailableMb(),
                            LoadAvg = HostSnapshot.ReadLoad(),
                        },
                        StartedAt = _started,
                        EndedAt = ended,
                        WallSeconds = (ended - _started).TotalSeconds,
                        ExitCode = exit,
                        Verdict = exit == ExitCodes.Green ? "GREEN" : "INVALID",
                        Reason = exit == ExitCodes.Green ? null : "source_ineligible",
                        Rows =
                        [
                            new ReportRow
                            {
                                Id = name, Filter = Filter, State = state, ExitCode = exit, Source = Source, Line = line,
                                Trx = result?.TrxPath, JournalDiagnostic = result?.JournalDiagnostic,
                                Slot = Slot, SlotReason = SlotReason, WaitedSeconds = WaitedSeconds,
                                Executed = result?.Trx?.Executed, Passed = result?.Trx?.Passed,
                                Failed = result?.Trx?.Failed, Skipped = result?.Trx?.Skipped,
                                Reruns = result?.Reruns ?? 0, RerunLines = result?.RerunLines ?? [],
                                Repeat = result?.Repeat, Timings = result?.Timings,
                            },
                        ],
                    });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    console.WriteLine("refused: report.json receipt-write-failed: " + ex.Message);
                    exit = ExitCodes.Invalid;
                }
            }
            console.WriteLine($"CHECKPOINT {name} EXIT CODE: {exit}");
            return exit;
        }
    }
}
