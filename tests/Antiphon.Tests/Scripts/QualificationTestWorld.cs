using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The process table of a qualification test (the injected Observe of the deleted drivers, as a table): each pid is alive,
/// absent, reused (alive with another start) or unknown (alive, its start unreadable), with the start its record names
/// (Started). A pid never set is absent. OnProbe runs before each IsAlive (the probe-ordering witness); Throw makes every
/// probe throw (the redacted probe error). Thread-safe.
/// </summary>
public sealed class QualificationProcessWorld : INightlyProcessTable
{
    private readonly object _gate = new();
    private readonly Dictionary<int, string> _status = [];

    /// <summary>The recorded start of every pid: a fixed instant plus pid seconds.</summary>
    public static DateTime Started(int pid) => new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc).AddSeconds(pid);

    /// <summary>The record's processStartedAt for the pid.</summary>
    public static string StartedText(int pid) => Started(pid).ToString("o");

    /// <summary>This process's id (the starter's or supervisor's own pid).</summary>
    public int CurrentPid { get; set; } = 4242;

    /// <summary>Runs before each IsAlive with the pid probed.</summary>
    public Action<int>? OnProbe { get; set; }

    /// <summary>Every probe throws this message.</summary>
    public string? Throw { get; set; }

    /// <summary>The IsAlive calls in order.</summary>
    public List<int> Probes { get; } = [];

    /// <summary>Sets the pid's status: alive, absent, reused or unknown.</summary>
    public QualificationProcessWorld Set(int pid, string status)
    {
        lock (_gate) _status[pid] = status;
        return this;
    }

    /// <summary>The pid's status (absent when never set).</summary>
    public string Status(int pid)
    {
        lock (_gate) return _status.TryGetValue(pid, out var status) ? status : "absent";
    }

    /// <inheritdoc />
    public DateTime? StartTimeUtc(int pid)
    {
        if (Throw != null) throw new InvalidOperationException(Throw);
        return Status(pid) switch
        {
            "alive" => Started(pid),
            "reused" => Started(pid).AddMinutes(5),
            _ => null,
        };
    }

    /// <inheritdoc />
    public bool IsAlive(int pid)
    {
        lock (_gate) Probes.Add(pid);
        OnProbe?.Invoke(pid);
        if (Throw != null) throw new InvalidOperationException(Throw);
        return pid > 0 && Status(pid) != "absent";
    }

    /// <inheritdoc />
    public NightlyParent Parent(int pid) => NightlyParent.None;

    /// <inheritdoc />
    public string BootId() => "boot";

    /// <inheritdoc />
    public string HostId() => "host";
}

/// <summary>A scripted child: each WaitForExit runs OnWait with the wait's number (1, 2, ...) and returns its answer; the clock advances by the wait.</summary>
public sealed class QualificationFakeChild : IQualificationChild
{
    private readonly FakeTimeProvider? _clock;

    public QualificationFakeChild(int pid, DateTime started, FakeTimeProvider? clock = null)
    {
        Pid = pid;
        StartTimeUtc = started;
        _clock = clock;
    }

    /// <summary>Answers wait number n: true when the child has exited by then. Default: exited at once.</summary>
    public Func<int, bool> OnWait { get; set; } = _ => true;

    /// <summary>The exit code once exited.</summary>
    public int Exit { get; set; }

    /// <summary>The waits so far.</summary>
    public int Waits { get; private set; }

    public int Pid { get; }

    public DateTime StartTimeUtc { get; }

    public bool HasExited { get; private set; }

    public int ExitCode => HasExited ? Exit : throw new InvalidOperationException("not exited");

    public bool WaitForExit(TimeSpan timeout)
    {
        Waits++;
        var exited = OnWait(Waits);
        if (!exited) _clock?.Advance(timeout);
        HasExited = exited;
        return exited;
    }

    public void Dispose() { }
}

/// <summary>The qualification seams of a test: it starts and stops nothing, records each call and answers from its delegates.</summary>
public sealed class QualificationTestHost : IQualificationHost
{
    /// <summary>The CLI argv (Cli and the staged copy's).</summary>
    public string[] CliArgv { get; set; } = ["antiphon-scripts"];

    public IReadOnlyList<string> Cli => CliArgv;

    /// <summary>The directories StageCli was asked to fill.</summary>
    public List<string> Staged { get; } = [];

    /// <summary>StageCli throws this when set.</summary>
    public Exception? StageFailure { get; set; }

    public IReadOnlyList<string> StageCli(string directory)
    {
        Staged.Add(directory);
        if (StageFailure != null) throw StageFailure;
        return CliArgv;
    }

    /// <summary>The starts so far.</summary>
    public List<QualificationStart> Starts { get; } = [];

    /// <summary>Answers a start (null: a child that exited 0 at once, pid 7000 + its number).</summary>
    public Func<QualificationStart, IQualificationChild>? OnStart { get; set; }

    public IQualificationChild Start(QualificationStart start)
    {
        Starts.Add(start);
        return OnStart?.Invoke(start) ?? new QualificationFakeChild(7000 + Starts.Count, DateTime.UtcNow);
    }

    /// <summary>The tree stops so far (pids in order).</summary>
    public List<int> Stops { get; } = [];

    /// <summary>Answers a tree stop (null: stopped).</summary>
    public Func<int, QualificationTreeStop>? OnStop { get; set; }

    public QualificationTreeStop StopTree(int pid)
    {
        Stops.Add(pid);
        return OnStop?.Invoke(pid) ?? new QualificationTreeStop(true, "stopped", QualificationTreeStop.CommandFor(pid));
    }

    /// <summary>The command line of each pid (absent: unreadable).</summary>
    public Dictionary<int, string> CommandLines { get; } = [];

    public string? CommandLine(int pid) => CommandLines.TryGetValue(pid, out var text) ? text : null;

    /// <summary>The console list answer.</summary>
    public int[] Consoles { get; set; } = [];

    public IReadOnlyList<int> ConsoleProcessIds() => Consoles;

    /// <summary>The session answer.</summary>
    public int? Session { get; set; }

    public int? SessionId() => Session;

    /// <summary>The heartbeat's process table (empty by default).</summary>
    public List<QualificationProcess> Table { get; } = [];

    public IQualificationProcessTable ProcessTable => new TableView(this);

    private sealed class TableView(QualificationTestHost owner) : IQualificationProcessTable
    {
        public IReadOnlyList<QualificationProcess> Snapshot() => owner.Table.ToArray();
    }
}

/// <summary>One qualification test's world: a temp root, the process table, the qualification seams, a fake clock and the host.</summary>
public sealed class QualificationWorld : IDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private QualificationWorld(string prefix)
    {
        Fixture = NightlyFixture.Create(prefix);
        Clock = new FakeTimeProvider(Now);
    }

    public static QualificationWorld Create(string prefix = "qualification") => new(prefix);

    public NightlyFixture Fixture { get; }

    public FakeTimeProvider Clock { get; }

    public QualificationProcessWorld Processes { get; } = new();

    public QualificationTestHost Seams { get; } = new();

    public FakeGit Git { get; } = new();

    public RecordingLauncher Launcher { get; } = new();

    public FakeHostProbe Probe { get; } = new();

    public System.IO.Abstractions.IFileSystem Files { get; } = new System.IO.Abstractions.FileSystem();

    /// <summary>The sleeps the host was asked for (each advances the clock).</summary>
    public List<TimeSpan> Sleeps { get; } = [];

    public string Out => Fixture.Out.ToString();

    public string Error => Fixture.Error.ToString();

    /// <summary>A host over this world (the processes given, or this world's).</summary>
    public NightlyHost Host(INightlyProcessTable? processes = null) => new()
    {
        Clock = Clock,
        Git = Git.Run,
        Launcher = Launcher,
        Processes = processes ?? Processes,
        Probe = Probe,
        Out = Fixture.Out,
        Error = Fixture.Error,
        Trace = Fixture.Trace,
        Qualification = Seams,
        Sleep = span =>
        {
            Sleeps.Add(span);
            Clock.Advance(span);
        },
    };

    /// <summary>A directory under the root, created.</summary>
    public string Directory(string name)
    {
        var path = Fixture.PathOf(name);
        System.IO.Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Writes the object as compact JSON (UTF-8, no byte-order mark).</summary>
    public static void WriteJson(string path, object value) =>
        File.WriteAllText(path, value is string text ? text : JsonSerializer.Serialize(value), new UTF8Encoding(false));

    /// <summary>Clears the captured output.</summary>
    public void ClearOutput() => Fixture.Out.GetStringBuilder().Clear();

    /// <summary>The last stdout line parsed as JSON.</summary>
    public JsonObject LastJson() => JsonNode.Parse(Out.Trim().Split('\n')[^1])!.AsObject();

    public void Dispose() => Fixture.Dispose();
}
