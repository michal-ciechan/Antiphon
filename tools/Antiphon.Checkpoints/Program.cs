using System.Reflection;
using System.Text.Json;

namespace Antiphon.Checkpoints;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            return await RunAsync(args).ConfigureAwait(false);
        }
        catch (ManifestValidationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ex.ExitCode;
        }
    }

    internal static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            Console.WriteLine("antiphon-checkpoints run|start|wait|status|stop|report|import|row|clean|execute|--version");
            return ExitCodes.Green;
        }

        if (args[0] is "--version" or "-v")
        {
            var info = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";
            Console.WriteLine("antiphon-checkpoints " + info);
            return ExitCodes.Green;
        }

        if (args[0] == "smoke-detach")
            return SmokeDetach(args);

        var verb = args[0];
        var options = Parse(args.Skip(1).ToArray());
        var repo = options.Get("repo-root") ?? RepoPaths.FindRoot();
        switch (verb)
        {
            case "import":
                return Import(options, repo);
            case "run":
                return await RunAndWait(options, repo).ConfigureAwait(false);
            case "start":
                return Start(options, repo);
            case "wait":
                return await Wait(options, repo).ConfigureAwait(false);
            case "status":
                return Status(options, repo);
            case "stop":
                return Stop(options, repo);
            case "report":
                return Report(options, repo);
            case "clean":
                return Clean(options, repo);
            case "row":
                return await Row(options, repo).ConfigureAwait(false);
            case "execute":
                return await CheckpointApp.ExecuteAsync(Required(options, "run"), CancellationToken.None).ConfigureAwait(false);
            default:
                Console.Error.WriteLine("unknown verb " + verb);
                return ExitCodes.Invalid;
        }
    }

    private static int SmokeDetach(string[] args)
    {
        var marker = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "c723-smoke");
        var launcher = new DetachedLauncher(new RuntimePlatform());
        var script = OperatingSystem.IsWindows()
            ? $"ping -n 3 127.0.0.1 >NUL & echo ok> \"{marker}\""
            : $"sleep 1; echo ok > '{marker.Replace("'", "'\\''")}'";
        var file = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";
        var cmd = OperatingSystem.IsWindows()
            ? new[] { "/d", "/s", "/c", script }
            : new[] { "-lc", script };
        var pid = launcher.Start(new LaunchRequest(file, cmd, Path.GetDirectoryName(marker) ?? Path.GetTempPath()));
        Console.WriteLine("executor=" + pid);
        Console.Out.Flush();
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
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, ManifestLoader.ToYaml(imported.Manifest));
        Console.WriteLine("imported " + imported.Manifest.Checkpoints.Count + " rows -> " + outPath);
        return ExitCodes.Green;
    }

    private static int Start(ArgSet options, string repo)
    {
        var (manifest, request) = LoadSelection(options, repo);
        return CheckpointApp.Start(manifest, request, repo, Console.Out).ExitCode;
    }

    private static async Task<int> RunAndWait(ArgSet options, string repo)
    {
        var (manifest, request) = LoadSelection(options, repo);
        var started = CheckpointApp.Start(manifest, request, repo, Console.Out);
        if (started.ExitCode != 0)
            return started.ExitCode;
        var max = ParseDuration(options.Get("max-wait"));
        var heartbeat = ParseDuration(options.Get("heartbeat")) ?? TimeSpan.FromSeconds(60);
        return await new WaitCommand().WaitAsync(started.RunDirectory, max, heartbeat, Console.Out, CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task<int> Wait(ArgSet options, string repo)
    {
        var run = ResolveRun(options, repo);
        var max = ParseDuration(options.Get("max-wait"));
        var heartbeat = ParseDuration(options.Get("heartbeat")) ?? TimeSpan.FromSeconds(60);
        return await new WaitCommand().WaitAsync(run, max, heartbeat, Console.Out, CancellationToken.None).ConfigureAwait(false);
    }

    private static int Status(ArgSet options, string repo)
    {
        var root = ResultsRoot(options, repo);
        if (!Directory.Exists(root))
            return ExitCodes.Green;
        var store = new RunStateStore();
        foreach (var dir in Directory.EnumerateDirectories(root).OrderBy(path => path))
        {
            var state = store.TryRead(Path.Combine(dir, "state.json"));
            if (state is null)
                continue;
            var green = state.Rows.Count(row => row.State == "green");
            var red = state.Rows.Count(row => row.State is "red" or "build-failed" or "timeout");
            var queued = state.Rows.Count(row => row.State is "queued" or "running");
            Console.WriteLine($"{state.RunId} {state.Phase} green={green} red={red} queued={queued}");
        }

        return ExitCodes.Green;
    }

    private static int Stop(ArgSet options, string repo)
    {
        var run = ResolveRun(options, repo);
        var store = new RunStateStore();
        var state = store.TryRead(Path.Combine(run, "state.json"));
        if (state is { ExecutorPid: > 0 })
        {
            try
            {
                using var process = Process.GetProcessById(state.ExecutorPid);
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                /* already gone */
            }
        }

        if (state is not null)
        {
            state.Phase = "stopped";
            store.Write(Path.Combine(run, "state.json"), state);
        }

        Console.WriteLine("stopped " + Path.GetFileName(run));
        return ExitCodes.Green;
    }

    private static int Report(ArgSet options, string repo)
    {
        if (options.Has("merge"))
        {
            var root = ResultsRoot(options, repo);
            var models = Directory.Exists(root)
                ? Directory.EnumerateDirectories(root)
                    .Select(dir => Path.Combine(dir, "report.json"))
                    .Where(File.Exists)
                    .Select(path => JsonSerializer.Deserialize<ReportModel>(File.ReadAllText(path), ReportWriter.Json)!)
                    .Where(model => model is not null)
                    .ToList()
                : [];
            if (models.Count == 0)
                return ExitCodes.Invalid;
            var hash = models[0].ManifestHash;
            var merged = ReportMerger.Merge(models.Where(model => model.ManifestHash == hash).ToList());
            Console.Write(options.Has("json") ? ReportWriter.JsonText(merged) : ReportWriter.Markdown(merged));
            return ExitCodes.Green;
        }

        var run = ResolveRun(options, repo);
        var report = Path.Combine(run, options.Has("json") ? "report.json" : "report.md");
        if (!File.Exists(report))
            return ExitCodes.Invalid;
        Console.Write(File.ReadAllText(report));
        return ExitCodes.Green;
    }

    private static int Clean(ArgSet options, string repo)
    {
        var older = ParseDuration(options.Get("older-than"));
        if (older is TimeSpan age)
        {
            var removed = OutputCleanup.RemoveOlderRuns(ResultsRoot(options, repo), age, options.Has("dry-run"));
            foreach (var path in removed)
                Console.WriteLine(OutputCleanup.DeletedLine(options.Has("dry-run"), path));
        }

        if (options.Get("run") is not null || options.Get("manifest") is not null || options.Positionals.Count > 0)
        {
            var manifest = options.Get("manifest") is string manifestPath
                ? ManifestLoader.LoadFile(manifestPath, repo)
                : ManifestLoader.LoadFile(Path.Combine(ResolveRun(options, repo), "manifest.resolved.yaml"), repo);
            var result = OutputCleanup.CleanOwnedOutputs(repo, manifest.Builds.Select(build => build.Id).ToList(), exitCode: 0, cleanOnRed: true, options.Has("dry-run"));
            foreach (var path in result.Deleted)
                Console.WriteLine(OutputCleanup.DeletedLine(options.Has("dry-run"), path));
        }

        return ExitCodes.Green;
    }

    private static async Task<int> Row(ArgSet options, string repo)
    {
        var platform = new RuntimePlatform();
        var slots = new BuildSlotClient(new HttpClientHandler(), BuildSlotClient.DefaultEndpoint(platform.IsWindows));
        var session = options.Get("slots") == "off"
            ? new SlotSession("off", 4)
            : await slots.ProbeAsync(CancellationToken.None).ConfigureAwait(false);
        var name = Required(options, "name");
        await using var lease = await slots.AcquireAsync(session, name, CancellationToken.None).ConfigureAwait(false);
        if (lease.ExitCode == ExitCodes.SlotTimeout)
        {
            Console.WriteLine($"CHECKPOINT {name} slot=timeout waited={lease.WaitedSeconds}s");
            Console.WriteLine($"CHECKPOINT {name} EXIT CODE: 4");
            return ExitCodes.SlotTimeout;
        }

        var resultsRoot = options.Get("results-root") ?? ".antiphon/checkpoints";
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..4];
        var results = Path.Combine(repo, resultsRoot, name + "-" + stamp);
        var properties = (options.GetAll("msbuild-property"))
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(token =>
            {
                var eq = token.IndexOf('=');
                return new KeyValuePair<string, string>(token[..eq], token[(eq + 1)..]);
            })
            .ToList();
        var commit = GitSnapshot.Run(repo, "rev-parse", "HEAD");
        if (commit.StartsWith("git-failed", StringComparison.Ordinal))
            commit = new string('0', 40);
        var runner = new RowRunner(new ProcessDriver { DotnetShim = options.Get("dotnet") }, platform);
        var result = await runner.RunAsync(new RowRequest
        {
            Name = name,
            Project = Required(options, "project"),
            OutputPath = Required(options, "output-path"),
            Filter = Required(options, "filter"),
            ResultsDirectory = results,
            WorkingDirectory = repo,
            NoBuild = options.Has("no-build"),
            MinExecuted = int.TryParse(options.Get("min-executed"), out var min) ? min : 1,
            Expect = (options.Get("expect") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(token => token.Trim('\'', '"')).Where(token => token.Length > 0).ToList(),
            Properties = properties,
            Commit = commit.Trim(),
            Slot = lease.State,
            WaitedSeconds = lease.WaitedSeconds,
            MaxCpuCount = lease.MaxCpuCount > 0 ? lease.MaxCpuCount : 4,
        }, Console.Out, CancellationToken.None).ConfigureAwait(false);
        return result.ExitCode;
    }

    private static (CheckpointManifest Manifest, RunRequest Request) LoadSelection(ArgSet options, string repo)
    {
        CheckpointManifest manifest;
        if (options.Get("plan") is string plan)
        {
            var path = Path.IsPathRooted(plan) ? plan : Path.Combine(repo, plan);
            var at = path.IndexOf('@');
            if (at > 0 && File.Exists(path[..at]))
                path = path[..at];
            var imported = PlanTableImporter.ImportFile(path);
            foreach (var warning in imported.Warnings)
                Console.Error.WriteLine("WARNING " + warning);
            if (imported.Manifest is null)
                throw new ManifestValidationException("plan", imported.Error ?? "import failed");
            manifest = imported.Manifest;
        }
        else if (options.Positionals.Count > 0)
        {
            var path = options.Positionals[0];
            manifest = ManifestLoader.LoadFile(Path.IsPathRooted(path) ? path : Path.Combine(repo, path), repo);
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

        ManifestValidator.Validate(manifest, repo);
        return (manifest, new RunRequest
        {
            Rows = rows,
            Baseline = options.Get("baseline"),
            KnownFlaky = (options.Get("known-flaky") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            RowTimeoutMinutes = Minutes(options.Get("row-timeout")),
            TotalTimeoutMinutes = Minutes(options.Get("total-timeout")),
            Parallel = int.TryParse(options.Get("parallel"), out var width) ? width : null,
            Serial = options.Has("serial"),
            Slots = options.Get("slots") ?? "auto",
            KeepOutputs = options.Has("keep-outputs"),
            CleanOnRed = options.Has("clean-on-red"),
        });
    }

    private static string ResolveRun(ArgSet options, string repo)
    {
        if (options.Get("run") is string run && run.Length > 0)
            return Path.IsPathRooted(run) ? run : Path.Combine(ResultsRoot(options, repo), run);
        if (options.Positionals.Count > 0 && !options.Positionals[0].EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
        {
            var token = options.Positionals[0];
            return Path.IsPathRooted(token) ? token : Path.Combine(ResultsRoot(options, repo), token);
        }

        var latest = Path.Combine(ResultsRoot(options, repo), "latest");
        if (!File.Exists(latest))
            throw new ManifestValidationException("run", "no latest run");
        return Path.Combine(ResultsRoot(options, repo), File.ReadAllText(latest).Trim());
    }

    private static string ResultsRoot(ArgSet options, string repo)
    {
        var root = options.Get("results-root") ?? ".antiphon/checkpoints";
        return Path.GetFullPath(Path.IsPathRooted(root) ? root : Path.Combine(repo, root));
    }

    private static string Required(ArgSet options, string name) =>
        options.Get(name) ?? throw new ManifestValidationException(name, "missing --" + name);

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
