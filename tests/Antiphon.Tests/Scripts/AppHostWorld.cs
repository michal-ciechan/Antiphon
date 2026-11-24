using System.IO.Abstractions;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 31 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the fake machine the AppHost commands run against,
/// in place of the inert seams file the deleted drivers dot-sourced into restart-apphost.ps1 (test-apphost-graceful-stop.ps1's
/// Write-C716Seams) and the stubs of the watchdog drivers. A temporary checkout root (real files: the locks need their share
/// semantics), a fake clock that Sleep advances, a FakeGit that names the root as the main worktree with HEAD Head, and every
/// IAppHostPlatform member recorded in Trace ("port 17204", "stop-tree 111", "graceful", "launch ...") and answered from the
/// settings below. Unit 37: it is also the dev stack commands' IDevStackSeams ("tool ...", "slot ...", "browser ...", "tab ...",
/// "prompt ...", "describe ..."). Nothing reaches a real port, process, task, event log, token, terminal or browser.
/// </summary>
internal sealed class AppHostWorld : IAppHostPlatform, IDevStackSeams, IDisposable
{
    public const string Head = "0123456789abcdef0123456789abcdef01234567";

    private readonly NightlyFixture _fixture = NightlyFixture.Create("apphost-world");
    private readonly List<string> _trace = [];
    private readonly object _gate = new();

    public AppHostWorld()
    {
        Directory.CreateDirectory(LogDir);
        Git = new FakeGit(Head)
            .Reply(arguments => arguments.Contains("--show-toplevel"), call => new NightlyGitResult(0, Root + Environment.NewLine))
            .Reply(arguments => arguments.Contains("worktree"), call => new NightlyGitResult(0, "worktree " + MainWorktree + "\nHEAD " + Head + "\nbranch refs/heads/master\n"))
            .Reply(arguments => arguments.Contains("HEAD") && arguments.Contains("rev-parse"), call => new NightlyGitResult(HeadExit, CurrentHead + Environment.NewLine))
            .Reply(arguments => arguments.Contains("status"), call => new NightlyGitResult(0, TrackedEdits ? " M server/Program.cs\n" : ""))
            .Reply(arguments => arguments.Contains("--git-path"), call => IndexLockPath == null ? new NightlyGitResult(128, "fatal: no index") : new NightlyGitResult(0, IndexLockPath + Environment.NewLine));
        MainWorktree = Root;
    }

    public FakeGit Git { get; }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 24, 9, 0, 0, TimeSpan.Zero));

    public StringWriter Out => _fixture.Out;

    public StringWriter Error => _fixture.Error;

    public string Root => _fixture.Root;

    public string LogDir => Path.Combine(Root, "logs");

    public string PathOf(params string[] parts) => _fixture.PathOf(parts);

    public string LogPath(string name) => Path.Combine(LogDir, name);

    /// <summary>The trace so far.</summary>
    public string[] Trace
    {
        get { lock (_gate) return _trace.ToArray(); }
    }

    public int Count(string kind) => Trace.Count(row => row == kind || row.StartsWith(kind + " ", StringComparison.Ordinal));

    public int IndexOf(string kind) => Array.FindIndex(Trace, row => row == kind || row.StartsWith(kind + " ", StringComparison.Ordinal));

    public void Record(string row)
    {
        lock (_gate) _trace.Add(row);
    }

    // ---- settings ----

    public string MainWorktree { get; set; }

    public string CurrentHead { get; set; } = Head;

    public int HeadExit { get; set; }

    public bool TrackedEdits { get; set; }

    public string? IndexLockPath { get; set; }

    public bool IsWindows { get; set; } = true;

    public int CurrentPid { get; set; } = 424242;

    public string PhoneHomeConfigPath => PathOf("phone-home.json");

    /// <summary>Holder answers: a pid listed here is Alive for that many more calls (then Dead); a pid in Unknown is Unknown; others Dead.</summary>
    public Dictionary<int, int> AliveCalls { get; } = new();

    public HashSet<int> AlwaysAlive { get; } = [];

    public Dictionary<int, int[]> Owners { get; } = new();

    public List<int> Listening { get; } = [];

    public bool ListenersFail { get; set; }

    public List<AppHostProcessRow> Named { get; } = [];

    public List<AppHostProcessRow> CensusRows { get; } = [];

    public bool CensusFails { get; set; }

    public HashSet<int> OwnChain { get; } = [424242];

    public string GracefulClass { get; set; } = "unreachable";

    public int GracefulPid { get; set; } = 4242;

    public string GracefulDetail { get; set; } = "connection failed";

    /// <summary>On a launch: the action run (default: the dashboard URL file appears).</summary>
    public Action<AppHostWorld>? OnLaunch { get; set; } = world => File.WriteAllText(world.LogPath("apphost-dashboard-url.txt"), "http://127.0.0.1:9/");

    public FakeChild? Child { get; private set; }

    /// <summary>The launched child's exit code (null: still running).</summary>
    public int? ChildExit { get; set; }

    public Func<string, AppHostHttpAnswer>? Http { get; set; }

    public int HealthStatus { get; set; } = 200;

    public string? VersionSha { get; set; } = Head;

    public Func<string, IReadOnlyList<string>, AppHostCommandResult>? Commands { get; set; }

    public Dictionary<string, AppHostTaskInfo> Tasks { get; } = new();

    public bool? LogonTaskRunning { get; set; } = false;

    public List<(string Message, bool Information)> Events { get; } = [];

    public bool EventsWritten { get; set; } = true;

    public List<(Uri Uri, string Key, string Body)> Webhooks { get; } = [];

    public bool WebhookOk { get; set; } = true;

    /// <summary>A dev stack child's answer (null: exit 0, no output); each run is traced "tool &lt;command text&gt;".</summary>
    public Func<DevStackLaunch, Action<string>, int>? Tools { get; set; }

    /// <summary>The dev stack children so far.</summary>
    public List<DevStackLaunch> Launches { get; } = [];

    /// <summary>A build slot's answer (null: exit 0); each slot is traced "slot &lt;label&gt; &lt;command&gt;".</summary>
    public Func<BuildSlotOptions, TextWriter, int>? Slot { get; set; }

    /// <summary>The build slots so far.</summary>
    public List<BuildSlotOptions> Slots { get; } = [];

    /// <summary>The URLs opened.</summary>
    public List<string> Browsed { get; } = [];

    /// <summary>The terminal tabs opened: title, directory, command.</summary>
    public List<(string Title, string Directory, string[] Command)> Tabs { get; } = [];

    /// <summary>The prompt's answers in order; empty: the end of the input (null).</summary>
    public Queue<string?> Answers { get; } = new();

    /// <summary>Describe answers by pid; a pid not listed has exited.</summary>
    public Dictionary<int, DevStackProcess> Described { get; } = new();

    /// <summary>The detached CLI starts so far (the arguments after the CLI).</summary>
    public List<string[]> CliStarts { get; } = [];

    // ---- the host ----

    public NightlyHost Host(Func<string, string?>? variables = null) => new()
    {
        Clock = Clock,
        Git = Git.Run,
        Launcher = new RecordingLauncher(),
        Processes = new FakeProcessTable(),
        Probe = new FakeHostProbe(),
        Out = Out,
        Error = Error,
        Trace = TextWriter.Null,
        Sleep = span => { Record("sleep " + span.TotalSeconds); Clock.Advance(span); },
        Variables = variables ?? (_ => null),
    };

    // ---- IAppHostPlatform ----

    public IFileSystem Files { get; } = new FileSystem();

    public AppHostHolder Holder(int pid)
    {
        if (AlwaysAlive.Contains(pid)) return AppHostHolder.Alive;
        lock (_gate)
        {
            if (AliveCalls.TryGetValue(pid, out var left) && left > 0)
            {
                AliveCalls[pid] = left - 1;
                return AppHostHolder.Alive;
            }
        }
        return AppHostHolder.Dead;
    }

    public IReadOnlyList<int> PortOwners(int port)
    {
        Record("port " + port);
        return Owners.TryGetValue(port, out var owners) ? owners : [];
    }

    public IReadOnlyList<int> ListeningPorts()
    {
        Record("listeners");
        if (ListenersFail) throw new InvalidOperationException("listener table unreadable");
        return Listening.ToArray();
    }

    public IReadOnlyList<AppHostProcessRow> Processes(IReadOnlyCollection<string> names)
    {
        Record("processes " + string.Join(",", names));
        return Named.Where(row => names.Contains(row.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
    }

    public IReadOnlyList<AppHostProcessRow> Census()
    {
        Record("census");
        if (CensusFails) throw new InvalidOperationException("census unreadable");
        return CensusRows.ToArray();
    }

    public IReadOnlyCollection<int> OwnProcessChain() => OwnChain;

    /// <summary>A stopped process's listeners go with it (the owner leaves Owners); default: the listeners stay.</summary>
    public bool StopFreesPorts { get; set; }

    public void StopProcess(int pid)
    {
        Record("stop-id " + pid);
        if (!StopFreesPorts) return;
        foreach (var port in Owners.Keys.ToArray()) Owners[port] = Owners[port].Where(owner => owner != pid).ToArray();
    }

    public void StopTree(int pid) => Record("stop-tree " + pid);

    public IAppHostChild StartCli(IReadOnlyList<string> arguments, string output, string error)
    {
        Record("launch " + string.Join(" ", arguments));
        CliStarts.Add(arguments.ToArray());
        File.WriteAllText(output, "");
        File.WriteAllText(error, "");
        OnLaunch?.Invoke(this);
        Child = new FakeChild(this) { Exit = ChildExit };
        return Child;
    }

    public AppHostHttpAnswer Get(string url, int timeoutSec)
    {
        if (Http != null) { Record("get " + url); return Http(url); }
        if (url.EndsWith("/health", StringComparison.Ordinal))
        {
            Record("health");
            return HealthStatus == 200 ? new AppHostHttpAnswer(200, "Healthy", null)
                : HealthStatus == 0 ? new AppHostHttpAnswer(null, null, "No connection could be made because the target machine actively refused it. (localhost:17202)")
                : new AppHostHttpAnswer(HealthStatus, null, "Response status code does not indicate success: " + HealthStatus + ".");
        }
        if (url.EndsWith("/api/version", StringComparison.Ordinal))
        {
            Record("version");
            return VersionSha == null ? new AppHostHttpAnswer(null, null, "refused")
                : new AppHostHttpAnswer(200, "{\"version\":\"" + VersionSha + "\",\"informationalVersion\":\"" + VersionSha + "\"}", null);
        }
        Record("get " + url);
        return new AppHostHttpAnswer(null, null, "No connection could be made because the target machine actively refused it.");
    }

    public AppHostHttpAnswer Post(string url, string body, int timeoutSec)
    {
        Record("post " + url);
        return Http?.Invoke(url) ?? new AppHostHttpAnswer(null, null, "No connection could be made because the target machine actively refused it.");
    }

    public AppHostGracefulStopResult GracefulStop(string reason, int timeoutSec)
    {
        Record("graceful " + GracefulClass + " " + reason + " " + timeoutSec);
        return new AppHostGracefulStopResult(GracefulClass, GracefulClass == "accepted" ? GracefulPid : null, GracefulDetail);
    }

    public bool PostWebhook(Uri uri, string idempotencyKey, string body, int timeoutSec)
    {
        Record("webhook " + idempotencyKey);
        Webhooks.Add((uri, idempotencyKey, body));
        return WebhookOk;
    }

    public AppHostCommandResult Run(string file, IReadOnlyList<string> arguments, int timeoutSec)
    {
        Record("run " + file + " " + string.Join(" ", arguments));
        return Commands?.Invoke(file, arguments) ?? new AppHostCommandResult(false, 0, "");
    }

    public AppHostTaskInfo Task(string name)
    {
        Record("task " + name);
        return Tasks.TryGetValue(name, out var info) ? info : new AppHostTaskInfo("Missing", "No scheduled task named '" + name + "' is registered");
    }

    public bool? TaskRunning(string name)
    {
        Record("task-running " + name);
        return LogonTaskRunning;
    }

    public void SetTaskEnabled(string name, bool enabled)
    {
        Record((enabled ? "enable " : "disable ") + name + " marker=" + File.Exists(LogPath("apphost.down-on-purpose")));
        if (!Tasks.ContainsKey(name)) throw new InvalidOperationException("No scheduled task named '" + name + "' is registered");
        Tasks[name] = enabled ? new AppHostTaskInfo("Enabled", "State=Ready") : new AppHostTaskInfo("Disabled", "State=Disabled");
    }

    public bool WriteEvent(string message, bool information)
    {
        Record("event " + (information ? "information" : "warning"));
        Events.Add((message, information));
        return EventsWritten;
    }

    public void CheckDaemonBuild(NightlyHost host) => Record("check-daemon-build");

    // ---- IDevStackSeams ----

    public int Run(DevStackLaunch launch, Action<string> line)
    {
        Record("tool " + launch.CommandText);
        lock (_gate) Launches.Add(launch);
        return Tools?.Invoke(launch, line) ?? 0;
    }

    public int BuildSlot(BuildSlotOptions options, TextWriter capture)
    {
        Record("slot " + options.Label + " " + string.Join(" ", options.Command));
        lock (_gate) Slots.Add(options);
        return Slot?.Invoke(options, capture) ?? 0;
    }

    public void OpenBrowser(string url)
    {
        Record("browser " + url);
        Browsed.Add(url);
    }

    public void OpenTab(string title, string workingDirectory, IReadOnlyList<string> command)
    {
        Record("tab " + title);
        Tabs.Add((title, workingDirectory, command.ToArray()));
    }

    public string? Prompt(string text)
    {
        Record("prompt " + text);
        return Answers.Count > 0 ? Answers.Dequeue() : null;
    }

    public DevStackProcess? Describe(int pid)
    {
        Record("describe " + pid);
        return Described.TryGetValue(pid, out var process) ? process : null;
    }

    /// <summary>A restart-lock or launch-lock line "pid stamp" written as the PowerShell did (no newline).</summary>
    public string WriteLock(string name, int pid, DateTime stampUtc)
    {
        var path = LogPath(name);
        File.WriteAllText(path, pid + " " + stampUtc.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
        return path;
    }

    public DateTime UtcNow => Clock.GetUtcNow().UtcDateTime;

    public void Dispose() => _fixture.Dispose();

    /// <summary>The launched CLI child (dev-aspire, run-apphost): running until Exit is set.</summary>
    internal sealed class FakeChild(AppHostWorld world) : IAppHostChild
    {
        public int Pid => 99999;

        public int? Exit { get; set; }

        public bool HasExited => Exit.HasValue;

        public int? ExitCode => Exit;

        public void Stop() => world.Record("child-stop");
    }
}
