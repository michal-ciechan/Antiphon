namespace Antiphon.Checkpoints;

public sealed class RowRequest
{
    public string Name { get; init; } = "";
    public string? Project { get; init; }
    public string? OutputPath { get; init; }
    public string? Filter { get; init; }
    public string? Command { get; init; }
    public string ResultsDirectory { get; init; } = "";
    public string WorkingDirectory { get; init; } = "";
    public bool NoBuild { get; init; }
    public string? BuildStateOverride { get; init; }
    public int MinExecuted { get; init; } = 1;
    public List<string> Expect { get; init; } = [];
    public List<KeyValuePair<string, string>> Properties { get; init; } = [];
    public IReadOnlyList<string> KnownFlaky { get; init; } = [];
    public string Commit { get; init; } = new string('0', 40);
    public string Slot { get; init; } = "unavailable";
    public int WaitedSeconds { get; init; }
    public int MaxCpuCount { get; init; } = 4;
    public TimeSpan Deadline { get; init; } = TimeSpan.FromMinutes(15);
    public string? DotnetFileName { get; init; }
    public Func<CancellationToken, Task>? BeforeLaunch { get; init; }
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
}

public sealed class RowRunResult
{
    public string Id { get; set; } = "";
    public int ExitCode { get; init; }
    public string BuildState { get; init; } = "";
    public string Line { get; set; } = "";
    public string Output { get; init; } = "";
    public int Reruns { get; init; }
    public List<string> RerunLines { get; init; } = [];
    public string? TrxPath { get; init; }
    public TrxParseResult? Trx { get; init; }
    public bool TimedOut { get; init; }
    public string State { get; init; } = "";
    public double Seconds { get; set; }
}

public sealed class RowRunner
{
    private readonly IDriver _driver;
    private readonly IPlatform _platform;

    public RowRunner(IDriver driver, IPlatform platform)
    {
        _driver = driver;
        _platform = platform;
    }

    public async Task<RowRunResult> RunAsync(RowRequest request, TextWriter output, CancellationToken cancellationToken)
    {
        var buffer = new StringWriter();
        var combined = new TeeWriter(output, buffer);
        if (!string.IsNullOrWhiteSpace(request.OutputPath)
            && !System.Text.RegularExpressions.Regex.IsMatch(request.OutputPath, ManifestValidator.OutputPathPattern))
        {
            combined.WriteLine($"CHECKPOINT {request.Name} invalid input: OutputPath '{request.OutputPath}' must be bin-<name>/ with a forward slash and no trailing space (CARD-0448)");
            combined.WriteLine($"CHECKPOINT {request.Name} EXIT CODE: 2");
            return Finish(ExitCodes.Invalid, "failed", "", buffer, 0, [], null, null, false, "invalid");
        }

        Directory.CreateDirectory(request.ResultsDirectory);
        var fileName = request.DotnetFileName ?? "dotnet";
        var properties = BuildStep.PropertyArguments(request.Properties, _platform.IsWindows);
        foreach (var property in properties)
            combined.WriteLine("MSBUILD PROPERTY " + property["--property:".Length..]);

        if (!string.IsNullOrWhiteSpace(request.Command))
        {
            if (request.BeforeLaunch is not null)
                await request.BeforeLaunch(cancellationToken).ConfigureAwait(false);
            var shell = _platform.IsWindows ? "cmd.exe" : "/bin/sh";
            var shellArgs = _platform.IsWindows
                ? new[] { "/d", "/s", "/c", request.Command }
                : new[] { "-lc", request.Command };
            var ran = await RowTimeout.RunWithDeadlineAsync(
                _driver,
                new DriverRequest(shell, shellArgs, request.WorkingDirectory, Path.Combine(request.ResultsDirectory, "console.log"), Environment: request.Environment),
                request.Deadline,
                cancellationToken).ConfigureAwait(false);
            if (ran.TimedOut)
                return TimeoutResult(request, "n/a", buffer, combined);
            var commandLine = CheckpointLine.Format(new CheckpointLineModel
            {
                Name = request.Name,
                Commit = request.Commit,
                Build = "n/a",
                Filter = request.Command,
                Command = true,
                ExitCode = ran.ExitCode,
                Slot = request.Slot,
                WaitedSeconds = request.WaitedSeconds,
            });
            combined.WriteLine(commandLine);
            var commandExit = ran.ExitCode == 0 ? ExitCodes.Green : ExitCodes.FailedTests;
            combined.WriteLine($"CHECKPOINT {request.Name} EXIT CODE: {commandExit}");
            return Finish(commandExit, "n/a", commandLine, buffer, 0, [], null, null, false,
                commandExit == 0 ? "green" : "red");
        }

        var buildState = request.BuildStateOverride ?? (request.NoBuild ? "reused" : "ok");
        if (!request.NoBuild)
        {
            if (request.BeforeLaunch is not null)
                await request.BeforeLaunch(cancellationToken).ConfigureAwait(false);
            var buildArgs = BuildStep.BuildArguments(request.Project!, request.OutputPath!, properties, request.MaxCpuCount);
            var built = await RowTimeout.RunWithDeadlineAsync(
                _driver,
                new DriverRequest(fileName, buildArgs, request.WorkingDirectory, Path.Combine(request.ResultsDirectory, "build.log"), Environment: request.Environment),
                request.Deadline,
                cancellationToken).ConfigureAwait(false);
            if (built.TimedOut)
                return TimeoutResult(request, "failed", buffer, combined);
            if (built.ExitCode != 0)
            {
                combined.WriteLine($"CHECKPOINT {request.Name} build=failed project={request.Project} outputPath={request.OutputPath} exit={built.ExitCode} slot={request.Slot} waited={request.WaitedSeconds}s");
                combined.WriteLine($"CHECKPOINT {request.Name} EXIT CODE: 2");
                return Finish(ExitCodes.Invalid, "failed", "", buffer, 0, [], null, null, false, "build-failed");
            }
        }

        var trxPath = Path.Combine(request.ResultsDirectory, "run.trx");
        if (request.BeforeLaunch is not null)
            await request.BeforeLaunch(cancellationToken).ConfigureAwait(false);
        var runArgs = BuildStep.RunArguments(request.Project!, request.OutputPath!, properties, request.Filter!, request.ResultsDirectory, "run.trx");
        var run = await RowTimeout.RunWithDeadlineAsync(
            _driver,
            new DriverRequest(fileName, runArgs, request.WorkingDirectory, Path.Combine(request.ResultsDirectory, "console.log"), Environment: request.Environment),
            request.Deadline,
            cancellationToken).ConfigureAwait(false);
        if (run.TimedOut)
            return TimeoutResult(request, buildState, buffer, combined);

        if (!File.Exists(trxPath))
        {
            combined.WriteLine($"CHECKPOINT {request.Name} no TRX written to {trxPath} (runner exit {run.ExitCode})");
            combined.WriteLine($"CHECKPOINT {request.Name} EXIT CODE: 2");
            return Finish(ExitCodes.Invalid, buildState, "", buffer, 0, [], trxPath, null, false, "no-trx");
        }

        var parsed = TrxReport.Parse(trxPath);
        if (!parsed.Ok)
        {
            combined.WriteLine($"CHECKPOINT {request.Name} malformed TRX {trxPath}: {parsed.Error}");
            combined.WriteLine($"CHECKPOINT {request.Name} EXIT CODE: 2");
            return Finish(ExitCodes.Invalid, buildState, "", buffer, 0, [], trxPath, parsed, false, "malformed");
        }

        var reruns = 0;
        var rerunLines = new List<string>();
        var decision = RerunPolicy.Select(parsed.FailureNames, request.KnownFlaky);
        var unlistedFailures = parsed.FailureNames.Where(name => !decision.Names.Contains(name)).ToList();
        var rerunPassed = false;
        if (decision.Filters.Count > 0)
        {
            reruns = 1;
            var passedNames = new HashSet<string>(StringComparer.Ordinal);
            var failedNames = new HashSet<string>(StringComparer.Ordinal);
            var sawOk = true;
            for (var index = 0; index < decision.Filters.Count; index++)
            {
                var rerunName = "rerun-" + (index + 1) + ".trx";
                var rerunTrx = Path.Combine(request.ResultsDirectory, rerunName);
                var rerunArgs = BuildStep.RunArguments(
                    request.Project!, request.OutputPath!, properties, decision.Filters[index], request.ResultsDirectory, rerunName);
                if (request.BeforeLaunch is not null)
                    await request.BeforeLaunch(cancellationToken).ConfigureAwait(false);
                var second = await RowTimeout.RunWithDeadlineAsync(
                    _driver,
                    new DriverRequest(fileName, rerunArgs, request.WorkingDirectory, Path.Combine(request.ResultsDirectory, "console.log"), Environment: request.Environment),
                    request.Deadline,
                    cancellationToken).ConfigureAwait(false);
                var secondParsed = File.Exists(rerunTrx) ? TrxReport.Parse(rerunTrx) : null;
                if (second.TimedOut || secondParsed is not { Ok: true })
                    sawOk = false;
                if (secondParsed is { Ok: true })
                {
                    foreach (var name in secondParsed.ExecutedNames)
                    {
                        if (secondParsed.FailureNames.Contains(name))
                            failedNames.Add(name);
                        else
                            passedNames.Add(name);
                    }
                }
            }

            foreach (var name in decision.Names)
            {
                var secondFailed = !sawOk || failedNames.Contains(name) || !passedNames.Contains(name);
                var outcome = secondFailed ? "Failed" : "Passed";
                rerunLines.Add($"RERUN {name} first=Failed second={outcome}");
            }

            rerunPassed = sawOk
                && decision.Names.All(name => passedNames.Contains(name) && !failedNames.Contains(name));
            if (rerunPassed && unlistedFailures.Count == 0)
            {
                parsed = new TrxParseResult
                {
                    Ok = true,
                    Executed = parsed.Executed,
                    Passed = parsed.Executed,
                    Failed = 0,
                    Skipped = parsed.Skipped,
                    ExecutedNames = parsed.ExecutedNames,
                    FailureNames = [],
                    Failures = [],
                    SlowClasses = parsed.SlowClasses,
                };
            }
        }

        var misses = new List<string>();
        foreach (var token in request.Expect.Where(t => !string.IsNullOrWhiteSpace(t)))
        {
            var hit = parsed.ExecutedNames.Concat(parsed.SkippedNames)
                .Any(name => name.Contains(token, StringComparison.OrdinalIgnoreCase));
            if (!hit)
                misses.Add(token);
        }

        var exit = ExitCodes.Green;
        if (parsed.Failed > 0 || parsed.FailureNames.Count > 0)
            exit = ExitCodes.FailedTests;
        else if (parsed.Executed < request.MinExecuted)
            exit = ExitCodes.RosterOrMin;
        else if (misses.Count > 0)
            exit = ExitCodes.RosterOrMin;

        var lineModel = CheckpointLine.Format(new CheckpointLineModel
        {
            Name = request.Name,
            Commit = request.Commit,
            Build = buildState,
            Filter = request.Filter!,
            Executed = parsed.Executed.ToString(),
            Passed = parsed.Passed.ToString(),
            Failed = parsed.Failed.ToString(),
            Skipped = parsed.Skipped.ToString(),
            Trx = Path.GetFullPath(trxPath),
            Slot = request.Slot,
            WaitedSeconds = request.WaitedSeconds,
            Reruns = reruns,
        });
        combined.WriteLine(lineModel);
        foreach (var rerunLine in rerunLines)
            combined.WriteLine(rerunLine);
        foreach (var name in parsed.FailureNames)
            combined.WriteLine("FAILED " + name);
        var shown = 0;
        foreach (var name in parsed.ExecutedNames)
        {
            if (shown >= 300)
                break;
            combined.WriteLine("EXECUTED " + name);
            shown++;
        }

        if (parsed.ExecutedNames.Count > shown)
            combined.WriteLine($"EXECUTED ... +{parsed.ExecutedNames.Count - shown} more");
        foreach (var token in misses)
            combined.WriteLine("ROSTER MISS " + token);
        if (exit == ExitCodes.RosterOrMin && parsed.Executed < request.MinExecuted)
            combined.WriteLine($"MIN EXECUTED expected at least={request.MinExecuted} actual={parsed.Executed}");
        combined.WriteLine($"CHECKPOINT {request.Name} EXIT CODE: {exit}");
        var state = exit switch
        {
            ExitCodes.Green => "green",
            ExitCodes.FailedTests => "red",
            ExitCodes.RosterOrMin => "roster-miss",
            _ => "red",
        };
        return Finish(exit, buildState, lineModel, buffer, reruns, rerunLines, Path.GetFullPath(trxPath), parsed, false, state);
    }

    private RowRunResult TimeoutResult(RowRequest request, string buildState, StringWriter buffer, TextWriter output)
    {
        var minutes = Math.Max(1, (int)Math.Round(request.Deadline.TotalMinutes));
        var line = CheckpointLine.Format(new CheckpointLineModel
        {
            Name = request.Name,
            Commit = request.Commit,
            Build = buildState,
            Filter = request.Filter ?? request.Command ?? "",
            Timeout = minutes + "m",
            Slot = request.Slot,
            WaitedSeconds = request.WaitedSeconds,
            Command = request.Command is not null,
        });
        output.WriteLine(line);
        output.WriteLine($"CHECKPOINT {request.Name} EXIT CODE: 5");
        return Finish(ExitCodes.Timeout, buildState, line, buffer, 0, [], null, null, true, "timeout");
    }

    private static RowRunResult Finish(
        int exitCode,
        string buildState,
        string line,
        StringWriter buffer,
        int reruns,
        List<string> rerunLines,
        string? trx,
        TrxParseResult? parsed,
        bool timedOut,
        string state) =>
        new()
        {
            ExitCode = exitCode,
            BuildState = buildState,
            Line = line,
            Output = buffer.ToString(),
            Reruns = reruns,
            RerunLines = rerunLines,
            TrxPath = trx,
            Trx = parsed,
            TimedOut = timedOut,
            State = state,
        };

    private sealed class TeeWriter : TextWriter
    {
        private readonly TextWriter _a;
        private readonly TextWriter _b;
        public TeeWriter(TextWriter a, TextWriter b) { _a = a; _b = b; }
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void WriteLine(string? value)
        {
            _a.WriteLine(value);
            _b.WriteLine(value);
        }
    }
}
