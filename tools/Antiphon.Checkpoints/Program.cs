using Antiphon.HostCleanup.Checkpoints;
using System.Reflection;
using System.Text.Json;

namespace Antiphon.Checkpoints;

public static class Program
{
    public static Task<int> Main(string[] args) => MainAsync(args);

    public static async Task<int> MainAsync(string[] args,
        Func<ConsoleCancelEventHandler, IDisposable>? subscribe = null,
        Func<CancellationToken, CheckpointApp.Runtime>? runtimeFactory = null)
    {
        using var cancellation = new CancellationTokenSource();
        var work = args.Length > 0 && args[0] is "run" or "start" or "wait" or "status"
            or "stop" or "report" or "validate" or "import" or "coverage" or "row" or "clean" or "execute";
        ConsoleCancelEventHandler handler = (_, signal) =>
        {
            signal.Cancel = true;
            cancellation.Cancel();
        };
        using var subscription = work ? (subscribe ?? ConsoleCancellation.Subscribe)(handler) : null;
        try
        {
            var token = work ? cancellation.Token : CancellationToken.None;
            var runtime = runtimeFactory?.Invoke(token) ?? new CheckpointApp.Runtime { CancellationToken = token };
            return await RunAsync(args, runtime).ConfigureAwait(false);
        }
        catch (ManifestValidationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ex.ExitCode;
        }
    }

    public static async Task<int> RunAsync(string[] args, CheckpointApp.Runtime? runtime = null)
    {
        runtime ??= new CheckpointApp.Runtime();
        try
        {
            runtime.CancellationToken.ThrowIfCancellationRequested();
            var result = await RunCoreAsync(args, runtime).ConfigureAwait(false);
            // Receipt readers may turn cancellation into an I/O refusal or a
            // returned failure. The caller token, not exception text, decides.
            return runtime.CancellationToken.IsCancellationRequested ? Canceled(runtime) : result;
        }
        catch (Exception) when (runtime.CancellationToken.IsCancellationRequested)
        {
            return Canceled(runtime);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            (runtime.Output ?? Console.Error).WriteLine("refused: receipt-or-I/O-failure: " + ex.Message);
            return ExitCodes.Invalid;
        }
    }

    private static int Canceled(CheckpointApp.Runtime runtime)
    {
        (runtime.Output ?? Console.Error).WriteLine("canceled: caller-requested");
        return ExitCodes.Canceled;
    }

    private static async Task<int> RunCoreAsync(string[] args, CheckpointApp.Runtime runtime)
    {
        var context = runtime.CleanupContext;
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            Console.WriteLine("antiphon-checkpoints run|start|wait|status|stop|report|validate|import|coverage|row|clean|execute|inspect-temp|hold|--version");
            return ExitCodes.Green;
        }

        if (args[0] is "--version" or "-v")
        {
            var info = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";
            Console.WriteLine("antiphon-checkpoints " + info);
            return ExitCodes.Green;
        }

        if (args[0] == "smoke-write")
            return SmokeWrite(args);
        if (args[0] == "smoke-detach")
            return SmokeDetach(args, runtime);
        if (args[0] == "hold")
            return Hold(args);

        var verb = args[0];
        var options = Parse(args.Skip(1).ToArray());
        // validate reads only the receipt (the server starts its launcher from a directory that need not be a checkout); a
        // placement skip asks git at the receipt's commit in this repository, else in the current directory.
        if (verb == "inspect-temp")
            return InspectTempCommand.Run(options, runtime.Output ?? Console.Out);
        if (verb == "validate")
            return Validate(options, options.Get("repo-root") ?? runtime.RepoRoot ?? TryFindRoot(context) ?? context.Io.CurrentDirectory, runtime);
        var repo = options.Get("repo-root") ?? runtime.RepoRoot ?? RepoPaths.FindRoot(context: context);
        var output = runtime.Output ?? Console.Error;
        if ((verb is "row" or "run" or "start" && CheckpointDiskSpace.Refuse(output, repo, Path.GetTempPath()))
            || (verb == "row" && CheckpointDiskSpace.Refuse(output, ResultsRoot(options, repo))))
            return ExitCodes.DiskLow;
        switch (verb)
        {
            case "coverage":
                return new Coverage.CoverageCommand().Run(repo, options.Get("plan") ?? "", options.GetAll("tests").ToList(), options.Get("format") ?? "text", options.Get("checklist"), runtime?.Output ?? Console.Out);
            case "import":
                return Import(options, repo);
            case "run":
                return await RunAndWait(options, repo, runtime).ConfigureAwait(false);
            case "start":
                return await Start(options, repo, runtime).ConfigureAwait(false);
            case "wait":
                return await Wait(options, repo, runtime).ConfigureAwait(false);
            case "status":
                return Status(options, repo, runtime.Output ?? Console.Out, runtime);
            case "stop":
                return Stop(options, repo, runtime);
            case "report":
                return Report(options, repo, runtime.Output ?? Console.Out, runtime);
            case "clean":
                return Clean(options, repo, runtime.Output ?? Console.Out, runtime);
            case "row":
                return await RowCommand.RunAsync(options, repo, runtime).ConfigureAwait(false);
            case "execute":
                return await CheckpointApp.ExecuteAsync(Required(options, "run"), runtime.CancellationToken, runtime).ConfigureAwait(false);
            default:
                Console.Error.WriteLine("unknown verb " + verb);
                return ExitCodes.Invalid;
        }
    }

    private static int Hold(string[] args)
    {
        var parent = 0;
        for (var i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == "--parent"
                && int.TryParse(args[i + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var pid))
                parent = pid;
        }

        while (parent <= 0 || ParentAlive(parent))
            Thread.Sleep(500);
        return ExitCodes.Green;
    }

    private static bool ParentAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static int SmokeWrite(string[] args)
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
            return ExitCodes.Invalid;
        Thread.Sleep(TimeSpan.FromSeconds(1));
        var full = Path.GetFullPath(args[1]);
        var directory = Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(directory))
            return ExitCodes.Invalid;
        var reason = SafeWrite.Text(full, "ok\n", SafeWrite.Under(directory));
        if (reason is not null)
        {
            Console.Error.WriteLine("refused: " + reason);
            return ExitCodes.Invalid;
        }

        return ExitCodes.Green;
    }

    private static int SmokeDetach(string[] args, CheckpointApp.Runtime? runtime)
    {
        var marker = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "c723-smoke");
        var output = runtime?.Output ?? Console.Out;
        string full;
        try
        {
            full = Path.GetFullPath(marker);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            output.WriteLine("refused: path-unsafe");
            return ExitCodes.Invalid;
        }

        var directory = Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(directory))
        {
            output.WriteLine("refused: path-unsafe");
            return ExitCodes.Invalid;
        }

        var refusal = SafeWrite.Preflight(directory, [full]);
        if (refusal is not null)
        {
            output.WriteLine("refused: " + refusal);
            return ExitCodes.Invalid;
        }

        if (File.Exists(full) || Directory.Exists(full))
        {
            output.WriteLine("refused: unrecorded-existing");
            return ExitCodes.Invalid;
        }

        var dll = typeof(Program).Assembly.Location;
        if (string.IsNullOrWhiteSpace(dll))
        {
            output.WriteLine("refused: path-unsafe");
            return ExitCodes.Invalid;
        }

        var launcher = new DetachedLauncher(new RuntimePlatform());
        var pid = launcher.Start(new LaunchRequest("dotnet", [dll, "smoke-write", full], directory));
        output.WriteLine("executor=" + pid);
        output.Flush();
        Thread.Sleep(TimeSpan.FromMinutes(2));
        return 0;
    }

    private static int Import(ArgSet options, string repo)
    {
        var plan = Required(options, "plan");
        var imported = PlanTableImporter.ImportFile(Path.IsPathRooted(plan) ? plan : Path.Combine(repo, plan));
        foreach (var warning in imported.Warnings)
            Console.WriteLine("WARNING " + warning);
        if (imported.ExitCode != 0 || imported.Manifest is null)
        {
            Console.Error.WriteLine(imported.Error);
            return imported.ExitCode;
        }

        var outPath = options.Get("out") ?? Path.Combine(repo, ".antiphon", "checkpoints.yaml");
        var written = SafeWrite.Text(outPath, ManifestLoader.ToYaml(imported.Manifest), SafeWrite.Under(repo));
        if (written is not null)
        {
            Console.Error.WriteLine("refused: " + written);
            return ExitCodes.Invalid;
        }
        Console.WriteLine("imported " + imported.Manifest.Checkpoints.Count + " rows -> " + outPath);
        return ExitCodes.Green;
    }

    private static async Task<int> Start(ArgSet options, string repo, CheckpointApp.Runtime? runtime)
    {
        runtime ??= new CheckpointApp.Runtime();
        var context = runtime.CleanupContext;
        var (manifest, request) = LoadSelection(options, repo, context, runtime.CancellationToken);
        return (await CheckpointApp.StartAsync(manifest, request, repo, (runtime.Output ?? Console.Out), runtime).ConfigureAwait(false)).ExitCode;
    }

    private static async Task<int> RunAndWait(ArgSet options, string repo, CheckpointApp.Runtime? runtime)
    {
        runtime ??= new CheckpointApp.Runtime();
        var context = runtime.CleanupContext;
        var (manifest, request) = LoadSelection(options, repo, context, runtime.CancellationToken);
        var started = await CheckpointApp.StartAsync(manifest, request, repo, (runtime.Output ?? Console.Out), runtime).ConfigureAwait(false);
        if (started.ExitCode != 0)
            return started.ExitCode;
        if (runtime.Wait is not null)
            return await runtime.Wait(started.RunDirectory).ConfigureAwait(false);
        var max = ParseDuration(options.Get("max-wait"));
        var heartbeat = ParseDuration(options.Get("heartbeat")) ?? TimeSpan.FromSeconds(60);
        return await new WaitCommand(probe: runtime.ProcessProbe, context: context).WaitAsync(started.RunDirectory, max, heartbeat, (runtime.Output ?? Console.Out), runtime.CancellationToken, request.Commit).ConfigureAwait(false);
    }

    private static async Task<int> Wait(ArgSet options, string repo, CheckpointApp.Runtime? runtime = null)
    {
        runtime ??= new CheckpointApp.Runtime();
        var context = runtime.CleanupContext;
        var run = ResolveRun(options, repo, context);
        var refusal = new CheckpointWrite(context).Preflight(repo, [Path.GetFullPath(run)]);
        if (refusal is not null)
        {
            Console.Out.WriteLine("refused: " + refusal);
            return ExitCodes.Invalid;
        }
        var max = ParseDuration(options.Get("max-wait"));
        var heartbeat = ParseDuration(options.Get("heartbeat")) ?? TimeSpan.FromSeconds(60);
        return await new WaitCommand(probe: runtime.ProcessProbe, context: context).WaitAsync(run, max, heartbeat, (runtime.Output ?? Console.Out), runtime.CancellationToken, options.Get("expected-source-sha")).ConfigureAwait(false);
    }

    private static int Status(ArgSet options, string repo, TextWriter output, CheckpointApp.Runtime? runtime = null)
    {
        runtime ??= new CheckpointApp.Runtime();
        var context = runtime.CleanupContext;
        var root = ResultsRoot(options, repo);
        if (!context.Io.DirectoryExists(root)) { output.WriteLine("refused: receipt-directory-missing: " + root); return ExitCodes.Invalid; }
        var store = new RunStateStore(context);
        var directories = options.Get("run") is not null ? new[] { ResolveRun(options, repo, context) } : context.Io.Enumerate(root).Where(context.Io.DirectoryExists).OrderBy(path => path).ToArray();
        foreach (var dir in directories)
        {
            var state = store.TryRead(Path.Combine(dir, "state.json"), out _, runtime.CancellationToken);
            if (state is null) { output.WriteLine("refused: state-receipt-unreadable: " + dir); return ExitCodes.Invalid; }
            if (options.Get("expected-source-sha") is string expected && state.Source?.Start?.Commit != expected)
            { output.WriteLine("refused: receipt-source-SHA-mismatching"); return ExitCodes.Invalid; }
            if (state.Phase == "done" && ReportWriter.RequiredReceiptRefusal(context, dir, state, runtime.CancellationToken) is string required)
            { output.WriteLine("refused: " + required); return ExitCodes.Invalid; }
            var green = state.Rows.Count(row => row.State == "green");
            var red = state.Rows.Count(row => row.State is "red" or "build-failed" or "timeout");
            var queued = state.Rows.Count(row => row.State is "queued" or "running");
            output.WriteLine($"{state.RunId} {state.Phase} green={green} red={red} queued={queued}");
        }

        return ExitCodes.Green;
    }

    private static int Stop(ArgSet options, string repo, CheckpointApp.Runtime? runtime)
    {
        runtime ??= new CheckpointApp.Runtime();
        var context = runtime.CleanupContext;
        var output = runtime?.Output ?? Console.Out;
        var run = ResolveRun(options, repo, context);
        var preflight = new CheckpointWrite(context).Preflight(repo, [Path.GetFullPath(run)], runtime?.GitExecutable);
        if (preflight is not null)
        {
            output.WriteLine("refused: " + preflight);
            return ExitCodes.Invalid;
        }

        var cleanup = new ToolCopyCleanup(runtime.ProcessProbe, context: context);
        var observed = cleanup.ObserveExecutor(run, runtime.CancellationToken);
        if (observed.Verdict is ProcessVerdict.Unknown or ProcessVerdict.ReusedPid)
        {
            Console.WriteLine("stop incomplete " + Path.GetFileName(run) + " " + observed.Reason);
            return ExitCodes.StillRunning;
        }
        var store = new RunStateStore(context);
        var state = store.TryRead(Path.Combine(run, "state.json"), out _, runtime.CancellationToken);
        if (state is null)
        {
            output.WriteLine("refused: state-receipt-unreadable: " + run);
            return ExitCodes.Invalid;
        }
        if (state.ToolOwner != SafeWrite.Marker || state.RunId != Path.GetFileName(Path.GetFullPath(run)))
        {
            output.WriteLine("refused: foreign_state");
            return ExitCodes.Invalid;
        }
        if (state.Source?.Start is null)
        {
            output.WriteLine("refused: state-receipt-source-unreadable");
            return ExitCodes.Invalid;
        }
        if (options.Get("expected-source-sha") is string expected && state.Source.Start.Commit != expected)
        {
            output.WriteLine("refused: receipt-source-SHA-mismatching");
            return ExitCodes.Invalid;
        }
        // An already-done receipt is refused before the gate, so a blank report
        // identity does not leave .terminal-publication.lock (CARD-1772).
        if (state.Phase == "done")
        {
            var required = RequiredRunReceiptRefusal(run, state, options.Get("expected-source-sha"), context, runtime.CancellationToken);
            if (required is not null)
            {
                output.WriteLine("refused: " + required);
                return ExitCodes.Invalid;
            }
        }
        if (!TerminalPublicationGate.TryEnter(context, run, out var publication))
        {
            output.WriteLine("stop incomplete " + Path.GetFileName(run) + " terminal-publication-busy");
            return ExitCodes.StillRunning;
        }
        using var held = publication;
        if (observed.Verdict == ProcessVerdict.AliveSame)
        {
            try
            {
                var identity = ExecutorOwnershipStore.Read(run, context, runtime.CancellationToken)?.Executor ?? RunOwnershipStore.Read(run, context, runtime.CancellationToken)?.Launched;
                if (identity is null)
                    return ExitCodes.StillRunning;
                if ((runtime.ProcessProbe ?? new ProcessIdentityProbe(context)).Observe(identity).Verdict != ProcessVerdict.AliveSame)
                    return ExitCodes.StillRunning;
                if (!(runtime?.ProcessControl ?? new ProcessControl()).StopAndWait(identity, TimeSpan.FromSeconds(10)))
                {
                    Console.WriteLine("stop incomplete " + Path.GetFileName(run) + " exit-timeout");
                    return ExitCodes.StillRunning;
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException
                                       or System.ComponentModel.Win32Exception)
            {
                Console.WriteLine("stop incomplete " + Path.GetFileName(run) + " " + ex.GetType().Name);
                return ExitCodes.StillRunning;
            }
        }

        // The executor may have completed rows or sealed the run while stop was
        // joining it. Never replace that progress with the pre-kill snapshot.
        state = store.TryRead(Path.Combine(run, "state.json"));
        if (state is null)
        {
            output.WriteLine("refused: state-receipt-unreadable: " + run);
            return ExitCodes.Invalid;
        }
        if (state.Phase != "done")
            TerminalReceipt.Publish(run, ExitCodes.Canceled, "supported-stop", runtime, state.StartedAt);
        else if (RequiredRunReceiptRefusal(run, state, options.Get("expected-source-sha"), context,
            CancellationToken.None) is string terminalRefusal)
        {
            output.WriteLine("refused: " + terminalRefusal);
            return ExitCodes.Invalid;
        }

        var receipt = cleanup.Remove(run, runtime.CancellationToken);
        output.WriteLine(receipt);
        if (receipt.Outcome == "Refused")
        {
            output.WriteLine("refused: " + receipt.Reason);
            return ExitCodes.Invalid;
        }

        output.WriteLine("stopped " + Path.GetFileName(run));
        return ExitCodes.Green;
    }

    private static string? RequiredRunReceiptRefusal(string run, RunState state, string? expectedSha, CheckpointCleanupContext context, CancellationToken cancellationToken)
    {
        if (expectedSha is not null && state.Source?.Start?.Commit != expectedSha)
            return "receipt-source-SHA-mismatching";
        return ReportWriter.RequiredReceiptRefusal(context, run, state, cancellationToken);
    }

    private static int Report(ArgSet options, string repo, TextWriter output, CheckpointApp.Runtime? runtime = null)
    {
        runtime ??= new CheckpointApp.Runtime();
        var context = runtime.CleanupContext;
        ReportModel? ReadRequired(string run)
        {
            var preflight = new CheckpointWrite(context).Preflight(repo, [Path.GetFullPath(run)]);
            var state = preflight is null ? new RunStateStore(context).TryRead(Path.Combine(run, "state.json"), out _, runtime.CancellationToken) : null;
            var reason = preflight ?? (state is null ? "state-receipt-unreadable: " + run
                : RequiredRunReceiptRefusal(run, state, options.Get("expected-source-sha"), context, runtime.CancellationToken));
            if (reason is not null) { output.WriteLine("refused: " + reason); return null; }
            return ReceiptJson.Read<ReportModel>(context, Path.Combine(run, "report.json"), ReportWriter.Json, runtime.CancellationToken);
        }

        if (options.Has("merge"))
        {
            var root = ResultsRoot(options, repo);
            var models = new List<ReportModel>();
            if (context.Io.DirectoryExists(root))
                foreach (var dir in context.Io.Enumerate(root).Where(context.Io.DirectoryExists).Where(dir =>
                    new[] { "state.json", "report.json", "report.md" }.Any(name => context.Io.FileExists(Path.Combine(dir, name)))))
                {
                    var model = ReadRequired(dir);
                    if (model is null) return ExitCodes.Invalid;
                    models.Add(model);
                }
            if (models.Count == 0)
            {
                output.WriteLine("refused: receipt-missing: " + root);
                return ExitCodes.Invalid;
            }
            var hash = models[0].ManifestHash;
            ReportModel merged;
            try { merged = ReportMerger.Merge(models.Where(model => model.ManifestHash == hash).ToList()); }
            catch (InvalidOperationException ex)
            {
                output.WriteLine("refused: receipt-merge-ineligible: " + ex.Message);
                return ExitCodes.Invalid;
            }
            output.Write(options.Has("json") ? ReportWriter.JsonText(merged) : ReportWriter.Markdown(context, merged));
            return ExitCodes.Green;
        }

        var run = ResolveRun(options, repo, context);
        if (ReadRequired(run) is null) return ExitCodes.Invalid;
        var report = Path.Combine(run, options.Has("json") ? "report.json" : "report.md");
        output.Write(ReceiptFile.ReadText(context, report, runtime.CancellationToken));
        return ExitCodes.Green;
    }

    private static int Clean(ArgSet options, string repo, TextWriter output, CheckpointApp.Runtime? runtime = null)
    {
        runtime ??= new CheckpointApp.Runtime();
        var context = runtime.CleanupContext;
        // A separate invocation cannot acquire deletion authority from persisted state.
        if (options.Get("run") is not null || options.Get("manifest") is not null || options.Positionals.Count > 0)
        {
            var run = ResolveRun(options, repo, context);
            output.WriteLine("left in place: " + run + "; remove it manually");
            var state = new RunStateStore(context).TryRead(Path.Combine(run, "state.json"), out _, runtime.CancellationToken);
            var required = state is null ? "state-receipt-unreadable" : ReportWriter.RequiredReceiptRefusal(context, run, state, runtime.CancellationToken);
            if (required is not null) output.WriteLine("refused: " + required);
            foreach (var path in state?.CreatedOutputPaths ?? []) output.WriteLine("left in place: " + path + "; remove it manually");
        }
        else output.WriteLine("left in place: " + ResultsRoot(options, repo) + "; remove it manually");
        output.WriteLine("refused: not-created-by-run; cleanup requires current-process creation records");
        return ExitCodes.Invalid;
    }

    private static string? TryFindRoot(CheckpointCleanupContext context)
    {
        try { return RepoPaths.FindRoot(context: context); }
        catch (ManifestValidationException) { return null; }
    }

    private static int Validate(ArgSet options, string repo, CheckpointApp.Runtime runtime)
    {
        var path = Required(options, "evidence");
        path = Path.IsPathRooted(path) ? path : Path.Combine(repo, path);
        var expected = Required(options, "expected-source-sha");
        // scripts/validate-checkpoint-receipt.ps1 is this verb's launcher (unit 20): both verdict lines go to stdout, as the
        // script printed them; the exit code is the verdict (0 valid, 2 not).
        var output = runtime.Output ?? Console.Out;
        // A comma-separated --rows, given once or more (the launcher joins a string[] with commas).
        var rows = options.GetAll("rows").SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToArray();
        try
        {
            var expectedRepeat = 1;
            if (options.Get("expected-repeat") is string count && !int.TryParse(count.Trim(), System.Globalization.NumberStyles.AllowLeadingSign,
                    System.Globalization.CultureInfo.InvariantCulture, out expectedRepeat))
                expectedRepeat = 0;
            var raw = ReceiptFile.ReadStrictText(runtime.CleanupContext, path, runtime.CancellationToken);
            var refusal = ReceiptQualification.Validate(raw, expected, rows, expectedRepeat, repo, runtime.CancellationToken);
            if (refusal is not null) { output.WriteLine("CHECKPOINT SOURCE INVALID reason=" + refusal); return ExitCodes.Invalid; }
            var selected = rows.Length;
            if (selected == 0)
            {
                using var document = JsonDocument.Parse(raw);
                selected = document.RootElement.TryGetProperty("rows", out var listed) && listed.ValueKind == JsonValueKind.Array ? listed.GetArrayLength() : 1;
            }
            output.WriteLine("CHECKPOINT SOURCE VALID source=" + expected + " rows=" + selected);
            return ExitCodes.Green;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or System.Xml.XmlException
            or ManifestValidationException or UnauthorizedAccessException)
        {
            output.WriteLine("CHECKPOINT SOURCE INVALID reason=" + ex.GetType().Name);
            return ExitCodes.Invalid;
        }
    }

    private static (CheckpointManifest Manifest, RunRequest Request) LoadSelection(ArgSet options, string repo, CheckpointCleanupContext? context = null, CancellationToken cancellationToken = default)
    {
        CheckpointManifest manifest;
        if (options.Get("plan") is string plan)
        {
            var path = Path.IsPathRooted(plan) ? plan : Path.Combine(repo, plan);
            var at = path.IndexOf('@');
            if (at > 0 && (context ?? CheckpointCleanupDefaults.Context).Io.FileExists(path[..at]))
                path = path[..at];
            var imported = PlanTableImporter.ImportMarkdown(ReceiptFile.ReadText(context ?? CheckpointCleanupDefaults.Context, path, cancellationToken), path, (context ?? CheckpointCleanupDefaults.Context).Io.Platform == CheckpointPlatform.Windows);
            foreach (var warning in imported.Warnings)
                Console.Error.WriteLine("WARNING " + warning);
            if (imported.Manifest is null)
                throw new ManifestValidationException("plan", imported.Error ?? "import failed");
            manifest = imported.Manifest;
        }
        else if (options.Positionals.Count > 0)
        {
            var path = options.Positionals[0];
            manifest = ManifestLoader.LoadFile(Path.IsPathRooted(path) ? path : Path.Combine(repo, path), repo, context: context, cancellationToken: cancellationToken);
        }
        else
            throw new ManifestValidationException("plan", "pass --plan or a manifest path");

        if (options.Get("results-root") is string root)
            manifest.ResultsRoot = root;
        var rows = (options.Get("rows") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (options.Get("after") is string after)
        {
            var selection = AfterSelector.Expand(after).ToList();
            var ids = manifest.Checkpoints.Where(row => AfterSelector.IsSelected(row.After, selection)).Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
            rows = rows.Count == 0 ? ids.ToList() : rows.Where(ids.Contains).ToList();
        }

        int? repeatOverride = null;
        if (options.Get("repeat") is string repeatText)
        {
            repeatOverride = RepeatRequest.Parse(repeatText);
            var selected = manifest.Checkpoints.Where(row => rows.Count == 0 || rows.Contains(row.Id)).ToList();
            if (selected.Count == 0)
                throw new ManifestValidationException("rows", "--repeat selected no rows");
            if (repeatOverride > 1 && selected.Any(row => row.IsCommand))
                throw new ManifestValidationException("repeat", "--repeat > 1 cannot select command rows");
            foreach (var row in selected)
                row.Repeat = repeatOverride.Value;
        }
        ManifestValidator.Validate(manifest, repo);
        var totalTimeoutMinutes = BoundedMinutes(options, "total-timeout");
        if (totalTimeoutMinutes is int asked)
        {
            var judged = manifest.Checkpoints.Where(row => rows.Count == 0 || rows.Contains(row.Id)).ToList();
            if (judged.Any(row => row.EstimatedMinutes is > 0))
            {
                var derived = RowTimeout.DeriveMinimumTotalMinutes(judged.Select(row => row.EstimatedMinutes ?? 0));
                if (asked < derived)
                {
                    var message = $"refused: checkpoint-total-below-derived: explicit totalTimeoutMinutes {asked} is below derived {derived}";
                    if (string.IsNullOrWhiteSpace(options.Get("total-timeout-override-reason")))
                        throw new ManifestValidationException("total-timeout", message);
                    Console.Error.WriteLine("WARNING " + message);
                }
            }
        }

        return (manifest, new RunRequest
        {
            Rows = rows,
            Repeat = repeatOverride,
            Baseline = options.Get("baseline"),
            KnownFlaky = (options.Get("known-flaky") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            RowTimeoutMinutes = Minutes(options.Get("row-timeout")),
            TotalTimeoutMinutes = totalTimeoutMinutes,
            HardWallTimeoutMinutes = BoundedMinutes(options, "hard-wall-timeout"),
            Parallel = int.TryParse(options.Get("parallel"), out var width) ? width : null,
            Serial = options.Has("serial"),
            Slots = options.Get("slots") ?? "auto",
            KeepOutputs = options.Has("keep-outputs"),
            CleanOnRed = options.Has("clean-on-red"),
            ExpectedSourceSha = options.Get("expected-source-sha"),
        });
    }

    private static string ResolveRun(ArgSet options, string repo, CheckpointCleanupContext? context = null)
    {
        if (options.Get("run") is string run && run.Length > 0)
            return Path.IsPathRooted(run) ? run : Path.Combine(ResultsRoot(options, repo), run);
        if (options.Positionals.Count > 0 && !options.Positionals[0].EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
        {
            var token = options.Positionals[0];
            return Path.IsPathRooted(token) ? token : Path.Combine(ResultsRoot(options, repo), token);
        }

        context ??= CheckpointCleanupDefaults.Context;
        var newest = context.Io.DirectoryExists(ResultsRoot(options, repo))
            ? context.Io.Enumerate(ResultsRoot(options, repo)).Where(context.Io.DirectoryExists).Where(path => context.Io.FileExists(Path.Combine(path, "request.json"))).OrderDescending(StringComparer.Ordinal).FirstOrDefault()
            : null;
        return newest ?? throw new ManifestValidationException("run", "no latest run");
    }

    private static string ResultsRoot(ArgSet options, string repo)
    {
        var root = options.Get("results-root") ?? ".antiphon/checkpoints";
        return Path.GetFullPath(Path.IsPathRooted(root) ? root : Path.Combine(repo, root));
    }

    private static string Required(ArgSet options, string name) =>
        options.Get(name) ?? throw new ManifestValidationException(name, "missing --" + name);

    private static int? BoundedMinutes(ArgSet options, string name)
    {
        if (!options.Has(name)) return null;
        try
        {
            var duration = ParseDuration(options.Get(name));
            if (duration is null) throw new FormatException();
            RunBudgetClock.Validate(duration, name);
            var minutes = checked((int)Math.Ceiling(duration.Value.TotalMinutes));
            RunBudgetClock.Validate(TimeSpan.FromMinutes(minutes), name);
            return minutes;
        }
        catch (Exception ex) when (ex is OverflowException or ArgumentException or FormatException)
        { throw new ManifestValidationException(name, name + " must name a positive finite duration"); }
    }

    private static int? Minutes(string? text)
    {
        var duration = ParseDuration(text);
        return duration is TimeSpan span ? (int)Math.Ceiling(span.TotalMinutes) : null;
    }

    internal static TimeSpan? ParseDuration(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var value = text.Trim();
        var suffix = value[^1];
        if (char.IsLetter(suffix) && double.TryParse(value[..^1], out var amount))
        {
            return suffix switch
            {
                's' => TimeSpan.FromSeconds(amount),
                'm' => TimeSpan.FromMinutes(amount),
                'h' => TimeSpan.FromHours(amount),
                'd' => TimeSpan.FromDays(amount),
                _ => null,
            };
        }

        return double.TryParse(value, out var seconds) ? TimeSpan.FromSeconds(seconds) : null;
    }

    private static ArgSet Parse(string[] args)
    {
        var options = new ArgSet();
        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                options.Positionals.Add(token);
                continue;
            }

            var name = token[2..];
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options.Add(name, args[i + 1]);
                i++;
            }
            else
                options.Add(name, "true");
        }

        return options;
    }

    internal sealed class ArgSet
    {
        private readonly Dictionary<string, List<string>> _values = new(StringComparer.Ordinal);
        public List<string> Positionals { get; } = [];
        public void Add(string name, string value)
        {
            if (!_values.TryGetValue(name, out var list))
                _values[name] = list = [];
            list.Add(value);
        }

        public string? Get(string name) => _values.TryGetValue(name, out var list) ? list[^1] : null;
        public IEnumerable<string> GetAll(string name) => _values.TryGetValue(name, out var list) ? list : [];
        public bool Has(string name) => _values.ContainsKey(name);
    }
}
