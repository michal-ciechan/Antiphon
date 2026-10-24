using System.Text;
using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using Antiphon.Tests.TestHelpers;

namespace Antiphon.Tests.Infrastructure;

internal sealed class HostCleanupCheckpointFixture
{
    public ControlledTimeProvider Clock { get; } = new();
    public VirtualCheckpointIo Io { get; }
    public CheckpointCleanupContext Context { get; }
    public CheckpointWrite Write => new(Context);
    public CheckpointDelete Delete => new(Context);
    public CheckpointOutputLedger Ledger => new(Context);
    // The policy uses the host's Path APIs even when kernel facts are virtual Linux facts.
    // Keep dictionary keys and observed paths in the same host-native form.
    public string Parent { get; } = Path.GetFullPath(Path.Combine("/c987", Guid.NewGuid().ToString("N")));
    public string Root => Path.Combine(Parent, "bin-owned");
    public string Payload => Path.Combine(Root, "payload.txt");
    public DateTimeOffset Exit => Clock.GetUtcNow().AddSeconds(1);
    public OutputCustody.Window Window => OutputCustody.For(Context, Root, Exit)!;

    public HostCleanupCheckpointFixture(CheckpointPlatform platform = CheckpointPlatform.Linux)
    {
        Io = new(Clock, platform);
        Context = new(Io, Io, Clock);
        Io.CreateDirectory("/");
        Io.CreateDirectory("/c987");
        Io.CreateDirectory(Parent);
        Io.Put(Path.Combine(Parent, ".git"), "git marker");
        Io.Repository = Parent;
        SafeWrite.Require(Write.CreateFreshDirectory(Root, Write.Under(Parent)));
        Io.Put(Payload, "original"); // child output, not a registry entry
        Io.Events.Clear();
        Io.ReadRequests.Clear();
        Io.Opens.Clear();
        Io.GitRequests.Clear();
    }

    public static void AssertNoSignalCalls(Type type)
    {
        var opcodes = typeof(System.Reflection.Emit.OpCodes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(System.Reflection.Emit.OpCode)).Select(f => (System.Reflection.Emit.OpCode)f.GetValue(null)!)
            .ToDictionary(op => unchecked((ushort)op.Value));
        foreach (var method in type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static))
        {
            var il = method.GetMethodBody()?.GetILAsByteArray();
            if (il is null) continue;
            for (var at = 0; at < il.Length;)
            {
                ushort code = il[at++]; if (code == 0xfe) code = (ushort)(0xfe00 | il[at++]);
                var op = opcodes[code];
                if (op.OperandType == System.Reflection.Emit.OperandType.InlineMethod)
                {
                    var called = method.Module.ResolveMethod(BitConverter.ToInt32(il, at));
                    if (called?.DeclaringType == typeof(ProcessControl) || called?.DeclaringType == typeof(IProcessControl)
                        || called?.DeclaringType == typeof(System.Diagnostics.Process) && called.Name == "Kill")
                        throw new InvalidOperationException("C987.sweep_never_signals_a_process.signal-call: " + called);
                }
                at += op.OperandType switch
                {
                    System.Reflection.Emit.OperandType.InlineNone => 0,
                    System.Reflection.Emit.OperandType.ShortInlineBrTarget or System.Reflection.Emit.OperandType.ShortInlineI or System.Reflection.Emit.OperandType.ShortInlineVar => 1,
                    System.Reflection.Emit.OperandType.InlineVar => 2,
                    System.Reflection.Emit.OperandType.InlineI8 or System.Reflection.Emit.OperandType.InlineR => 8,
                    System.Reflection.Emit.OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, at),
                    _ => 4,
                };
            }
        }
    }

    public void Seal() => SafeWrite.Require(Ledger.Seal(Root, Exit));
    public CheckpointCleanupContext FreshContext() => new(Io, Io, Clock);
    public byte[] Read(int limit = 1024, Action? after = null, CancellationToken ct = default) =>
        Antiphon.Checkpoints.Coverage.ConfinedFileReader.ReadBytes(Context, Root, Payload, limit, after, ct);
}


internal sealed class HostCleanupReceiptFixture
{
    public HostCleanupCheckpointFixture Files { get; } = new();
    public VirtualCheckpointIo Io => Files.Io;
    public CheckpointCleanupContext Context => Files.Context;
    public string Run => Path.Combine(Files.Root, "verified-run");
    public string StatePath => Path.Combine(Run, "state.json");
    public string MarkdownPath => Path.Combine(Run, "report.md");
    public string JsonPath => Path.Combine(Run, "report.json");
    public string Output => Path.Combine(Files.Root, "output");
    public string StampPath => Path.Combine(Output, "checkpoint-build-source.json");
    public RunState State { get; }
    public ReportModel Report { get; }
    public CheckpointBuildBinding Stamp { get; }

    public HostCleanupReceiptFixture(int exit = 0, bool seed = true)
    {
        SafeWrite.Require(Files.Write.CreateFreshDirectory(Run, Files.Write.Under(Files.Root)));
        Io.CreateDirectory(Output);
        State = new()
        {
            RunId = "verified-run", ToolOwner = SafeWrite.Marker, Phase = "done", ExitCode = exit,
            StartedAt = Files.Clock.GetUtcNow(), EndedAt = Files.Clock.GetUtcNow(),
            Source = Source(),
            Builds = [new() { Id = "owned", State = "built", Slot = "granted", Source = Source() }],
            Rows = [new() { Id = "CP-fixture", State = "green", Slot = "granted" }]
        };
        Report = new()
        {
            RunId = State.RunId, Commit = State.Source.Start.Commit!, Source = Source(), ExitCode = exit,
            Verdict = exit == 0 ? "GREEN" : "RED",
            OutputNames = ["bin-owned/"], OutputPaths = [Output],
            Builds = [new() { Id = "owned", State = "built", Project = "fixture", Slot = "granted", Source = Source() }],
            Rows = [new() { Id = "CP-fixture", State = "green", Source = Source(), Executed = 1, Passed = 1 }]
        };
        Stamp = new() { Commit = Report.Commit, Fingerprint = Report.Source.Start.Fingerprint, OutputPath = Output };
        if (seed)
        {
            var markdown = ReportWriter.Markdown(Context, Report);
            Report.MarkdownSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(markdown)));
            Io.Put(MarkdownPath, markdown);
            SaveReport();
            Io.Put(StatePath, System.Text.Json.JsonSerializer.Serialize(State, RunStateStore.Json));
        }
        SaveStamp();
        ClearEvents();
    }

    private static SourceEvidence Source()
    {
        var observation = new SourceObservation(new string('a', 40), 0, new string('1', 64), DateTimeOffset.UnixEpoch, "known");
        return new() { Start = observation, End = observation, State = "clean", BuildSource = "verified" };
    }
    public void SaveReport() => Io.Put(JsonPath, ReportWriter.JsonText(Report));
    public void SaveStamp() => Io.Put(StampPath, System.Text.Json.JsonSerializer.Serialize(Stamp, CheckpointApp.Json));
    public void ClearEvents() { Io.Events.Clear(); Io.Opens.Clear(); Io.ReadRequests.Clear(); }
}

internal sealed class VirtualCheckpointIo(ControlledTimeProvider clock, CheckpointPlatform platform = CheckpointPlatform.Linux) : ICheckpointCleanupIo, ICheckpointGitRunner
{
    internal sealed class Node
    {
        public byte[] Bytes = [];
        public required CheckpointStat Stat;
        public string? Target;
        public FileAttributes Attributes;
        public DateTime CreatedUtc;
    }

    public int CurrentProcessId => 123;
    public string MachineName => "virtual-host";
    public string BootId => "virtual-boot";
    public string PidNamespace => "virtual-pidns";
    public string ImageDirectory { get; set; } = "/virtual/image";
    public string CurrentDirectory => Repository;
    public Dictionary<int, CheckpointProcessSnapshot> Processes { get; } = new() { [123] = new(456, 'R', false) };
    public CheckpointProcessSnapshot Process(int pid)
    {
        Enter("process", pid.ToString());
        return Processes.TryGetValue(pid, out var value) ? value : throw new ArgumentException("absent process");
    }
    public DateTime CreationTimeUtc(string path) { Enter("created", path); return Nodes[path].CreatedUtc; }
    public DateTime LastWriteTimeUtc(string path) { Enter("modified", path); return DateTime.UnixEpoch.AddTicks(Nodes[path].Stat.Modified / 100); }
    public string StreamPath(Stream stream) => ((WriteStream)stream).Path;
    public Dictionary<string, string> Environment { get; } = [];
    public Dictionary<string, Node> Nodes { get; } = new(new FixturePathComparer(platform));
    public List<(string Operation, string Path)> Events { get; } = [];
    public List<CheckpointReadOpen> ReadRequests { get; } = [];
    public List<(string Path, FileMode Mode, FileAccess Access, FileShare Share)> Opens { get; } = [];
    public List<CheckpointGitRequest> GitRequests { get; } = [];
    public Action<string, string>? Before { get; set; }
    public Action<string>? AfterRead { get; set; }
    public Func<CheckpointGitRequest, CheckpointGitResult>? GitResult { get; set; }
    public Func<CheckpointHandleStat, CheckpointHandleStat>? HandleMetadata { get; set; }
    public Func<Stream>? StreamFactory { get; set; }
    public bool InspectFails { get; set; }
    public string Repository { get; set; } = "/c987";
    public CheckpointPlatform Platform { get; set; } = platform;
    public uint Owner => 1000;
    public int RecursiveIntents { get; private set; }
    private ulong _inode = 10;
    private readonly Dictionary<string, List<FileShare>> _held = new(new FixturePathComparer(platform));
    private long Now => (clock.GetUtcNow().UtcTicks - DateTime.UnixEpoch.Ticks) * 100;

    private void Enter(string operation, string path)
    {
        Events.Add((operation, path));
        Before?.Invoke(operation, path);
    }
    public int Count(string operation, string? path = null) => Events.Count(e => e.Operation == operation && (path is null || Nodes.Comparer.Equals(e.Path, path)));
    public Node Put(string path, string text) => Put(path, Encoding.UTF8.GetBytes(text));
    public Node Put(string path, byte[] bytes)
    {
        var node = new Node { CreatedUtc = clock.GetUtcNow().UtcDateTime, Bytes = bytes, Stat = new("file", 1, ++_inode, bytes.Length, Now, Now, 0x8180, 1, Owner) };
        Nodes[path] = node;
        return node;
    }
    public string Text(string path) => Encoding.UTF8.GetString(Nodes[path].Bytes);
    public bool FileExists(string path) { Enter("file-exists", path); return Nodes.TryGetValue(path, out var n) && n.Stat.Kind != "directory"; }
    public bool DirectoryExists(string path) { Enter("directory-exists", path); return Nodes.TryGetValue(path, out var n) && n.Stat.Kind == "directory"; }
    public FileAttributes Attributes(string path)
    {
        Enter("attributes", path);
        if (!Nodes.TryGetValue(path, out var n)) throw new FileNotFoundException();
        return n.Attributes | (n.Stat.Kind == "directory" ? FileAttributes.Directory : 0) | (n.Target is null ? 0 : FileAttributes.ReparsePoint);
    }
    public string? LinkTarget(string path) { Enter("link", path); return Nodes.GetValueOrDefault(path)?.Target; }
    public long Length(string path) { Enter("length", path); return Nodes[path].Bytes.Length; }
    public IEnumerable<string> Enumerate(string path)
    {
        Enter("enumerate", path);
        return Nodes.Keys.Where(p => Nodes.Comparer.Equals(Path.GetDirectoryName(p), path)).ToArray();
    }
    public CheckpointStat Stat(string path, bool portable = false)
    {
        Enter("stat", path);
        return Nodes.TryGetValue(path, out var n) ? n.Stat : new("absent", 0, 0, 0, 0, 0);
    }
    public CheckpointStat CustodyStat(string path) => Stat(path);
    public int CreateExclusiveDirectory(string path)
    {
        Enter("mkdir-exclusive", path);
        if (Nodes.ContainsKey(path)) return 17;
        if (!Nodes.TryGetValue(Path.GetDirectoryName(path)!, out var parent) || parent.Stat.Kind != "directory") return 2;
        CreateDirectory(path);
        return 0;
    }
    public void CreateDirectory(string path)
    {
        Enter("mkdir", path);
        if (Nodes.ContainsKey(path)) return;
        var missing = new Stack<string>();
        for (var parent = Path.GetDirectoryName(path); parent is not null && !Nodes.ContainsKey(parent); parent = Path.GetDirectoryName(parent))
            missing.Push(parent);
        while (missing.TryPop(out var ancestor))
            Nodes[ancestor] = new Node { CreatedUtc = clock.GetUtcNow().UtcDateTime, Stat = new("directory", 1, ++_inode, 0, Now, Now, 0x41C0, 1, Owner) };
        Nodes[path] = new Node { CreatedUtc = clock.GetUtcNow().UtcDateTime, Stat = new("directory", 1, ++_inode, 0, Now, Now, 0x41C0, 1, Owner) };
    }
    public Stream Open(string path, FileMode mode, FileAccess access, FileShare share)
    {
        Enter("open", path);
        Opens.Add((path, mode, access, share));
        if (_held.TryGetValue(path, out var held) && held.Any(s => s == FileShare.None || share == FileShare.None)) throw new IOException("sharing violation");
        if (mode == FileMode.CreateNew && Nodes.ContainsKey(path)) throw new IOException("already exists");
        if (!Nodes.ContainsKey(path) && mode is FileMode.Open or FileMode.Truncate) throw new FileNotFoundException(path);
        if (!Nodes.ContainsKey(path) || mode is FileMode.Create or FileMode.CreateNew or FileMode.Truncate) Put(path, "");
        if (!_held.TryGetValue(path, out held)) _held[path] = held = [];
        held.Add(share);
        return new WriteStream(path, Nodes[path].Bytes, mode == FileMode.Append, access != FileAccess.Read, bytes =>
        {
            if (access != FileAccess.Read)
            {
                Nodes[path].Bytes = bytes;
                Nodes[path].Stat = Nodes[path].Stat with { Size = bytes.Length, Modified = Now, Changed = Now };
            }
            held.Remove(share);
        });
    }
    public ICheckpointReadHandle OpenRead(string path, CheckpointReadOpen request)
    {
        Enter("open-read", path);
        ReadRequests.Add(request);
        if (!Nodes.TryGetValue(path, out var node)) throw new FileNotFoundException(path);
        if (node.Target is not null && ((Platform == CheckpointPlatform.Linux && (request.UnixFlags & 0x20000) == 0)
            || (Platform == CheckpointPlatform.Windows && (request.WindowsFlags & 0x00200000) == 0)))
        { Enter("follow-target", node.Target); node = Nodes[node.Target]; }
        else if (node.Target is not null) throw new IOException("no follow");
        return new Held(this, node, path);
    }
    public void WriteBytes(string path, ReadOnlySpan<byte> bytes) { Enter("write", path); Put(path, bytes.ToArray()); }
    public void AppendText(string path, string text) { Enter("append", path); Put(path, Text(path) + text); }
    public void Copy(string source, string destination, bool overwrite) { Enter("copy", destination); Put(destination, Nodes[source].Bytes.ToArray()); }
    public void Move(string source, string destination, bool overwrite) { Enter("move", destination); Nodes[destination] = Nodes[source]; Nodes.Remove(source); }
    public void Unlink(string path) { Enter("unlink", path); Nodes.Remove(path); }
    public void RemoveDirectory(string path, bool recursive)
    {
        Enter("rmdir", path);
        if (recursive)
        {
            RecursiveIntents++;
            var comparer = (FixturePathComparer)Nodes.Comparer;
            foreach (var entry in Nodes.Keys.Where(p => comparer.IsChild(p, path)).ToArray())
                Nodes.Remove(entry);
        }
        if (Nodes.Keys.Any(p => Nodes.Comparer.Equals(Path.GetDirectoryName(p), path))) throw new IOException("directory not empty");
        Nodes.Remove(path);
    }

    // Production canonicalizes with the host Path APIs. Match their separator aliases while
    // retaining the virtual platform's case rule; Linux fixtures on Windows use native paths too.
    private sealed class FixturePathComparer(CheckpointPlatform platform) : IEqualityComparer<string>
    {
        private StringComparer Comparer => platform == CheckpointPlatform.Windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        private string Key(string path) => platform == CheckpointPlatform.Windows || OperatingSystem.IsWindows()
            ? path.Replace('\\', '/') : path;
        public bool Equals(string? x, string? y) => x is null || y is null ? x == y : Comparer.Equals(Key(x), Key(y));
        public int GetHashCode(string obj) => Comparer.GetHashCode(Key(obj));
        public bool IsChild(string path, string parent) => Key(path).StartsWith(Key(parent) + "/",
            platform == CheckpointPlatform.Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
    public string? EnvironmentVariable(string name) => Environment.GetValueOrDefault(name) ?? (name == "PATH" ? "/bin" : null);
    public CheckpointGitResult Run(CheckpointGitRequest request)
    {
        Enter("git", request.WorkingDirectory);
        GitRequests.Add(request);
        return GitResult?.Invoke(request) ?? new(0, request.Arguments.Contains("rev-parse") ? Repository + "\n" : "", "", false);
    }

    private sealed class Held(VirtualCheckpointIo io, Node node, string path) : ICheckpointReadHandle
    {
        public CheckpointHandleStat Inspect()
        {
            io.Enter("inspect", path);
            if (io.InspectFails) throw new IOException("inspect failed");
            var metadata = new CheckpointHandleStat((node.Stat.Mode & 0xF000) == 0x8000, node.Stat.Links, (ulong)node.Stat.Size, path);
            return io.HandleMetadata?.Invoke(metadata) ?? metadata;
        }
        public Stream OpenStream()
        {
            io.Enter("stream", path);
            return io.StreamFactory?.Invoke() ?? new ReadStream(node.Bytes, () => io.AfterRead?.Invoke(path));
        }
        public void Dispose() => io.Enter("close-read", path);
    }
    private sealed class ReadStream(byte[] bytes, Action afterRead) : MemoryStream(bytes, false)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var result = base.ReadAsync(buffer, cancellationToken);
            afterRead();
            return result;
        }
    }
    private sealed class WriteStream : MemoryStream
    {
        private readonly Action<byte[]> _closed;
        public string Path { get; }
        private readonly bool _writable;
        private bool _disposed;
        public override bool CanWrite => _writable;
        public WriteStream(string path, byte[] bytes, bool append, bool writable, Action<byte[]> closed)
        { _writable = true; Write(bytes); Position = append ? Length : 0; _closed = closed; Path = path; _writable = writable; }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed) { _disposed = true; _closed(ToArray()); }
            base.Dispose(disposing);
        }
    }
}

internal sealed class HostCleanupToolFixture
{
    public HostCleanupReceiptFixture Receipt { get; }
    public HostCleanupCheckpointFixture Files => Receipt.Files;
    public VirtualCheckpointIo Io => Files.Io;
    public CheckpointCleanupContext Context => Files.Context;
    public string Run { get; private set; }
    public string Tool => Path.Combine(Run, "tool");
    public string Payload => Path.Combine(Tool, "fixture.dll");
    public ProcessIdentity Dead => new(999, 1, Io.MachineName, Io.BootId, Io.PidNamespace);
    public RunOwnership Owner { get; }
    public HostCleanupToolFixture(int exit = 0, bool sealAuthority = false)
    {
        Receipt = new(exit);
        Run = Receipt.Run;
        Owner = new() { RunId = Path.GetFileName(Run), RunDirectory = Run,
            Starter = new ProcessIdentityProbe(Context).Current(), Launched = Dead, Phase = "launched" };
        SafeWrite.Require(Files.Write.CreateFreshDirectory(Tool, Files.Write.Under(Run)));
        SafeWrite.Require(Files.Write.Text(Payload, "keep", Files.Write.Under(Tool)));
        SafeWrite.Require(Files.Ledger.Seal(Tool));
        if (sealAuthority) Owner.ToolCopy = ToolCopyAuthority.Seal(Run, Owner, Context);
        RunOwnershipStore.Write(Run, Owner, Context);
        Receipt.ClearEvents();
    }
    public ToolCopyCleanup Cleanup(CheckpointCleanupContext? context = null, Action<string>? beforeDelete = null) =>
        new(context: context ?? Context, beforeDelete: beforeDelete);
    public StringWriter LastWaitOutput { get; private set; } = new();
    public async Task<int> Wait(CheckpointCleanupContext? context = null, string? sha = null, CancellationToken ct = default)
    {
        LastWaitOutput = new();
        return await new WaitCommand(context: context ?? Context).WaitAsync(Run, TimeSpan.Zero, TimeSpan.FromSeconds(1), LastWaitOutput, ct, sha);
    }
    public CheckpointApp.Runtime Runtime(IProcessControl? control = null) => new()
    {
        CleanupContext = Context, RepoRoot = Files.Parent, SourceBranch = "virtual", EnvironmentLookup = _ => null,
        ProcessControl = control, Output = new StringWriter(),
        SourceCapture = _ => Receipt.State.Source.Start,
    };
    public async Task StartAsync()
    {
        var source = Path.Combine(Files.Parent, "source-tool");
        Io.CreateDirectory(source); Io.Put(Path.Combine(source, "Antiphon.Checkpoints.dll"), "image");
        var launches = 0;
        var runtime = new CheckpointApp.Runtime
        {
            CleanupContext = Context, RepoRoot = Files.Parent, SourceBranch = "virtual", EnvironmentLookup = _ => null,
            ToolDirectory = source, SourceCapture = _ => Receipt.State.Source.Start,
            LaunchWithOutcome = request => { launches++; return new(LaunchKind.Started, Dead.Pid, Dead); },
        };
        var started = await CheckpointApp.StartAsync(Manifest(), new() { Branch = "virtual" }, Files.Parent, new StringWriter(), runtime);
        if (started.ExitCode != 0 || launches != 1) throw new InvalidOperationException("virtual starter failed");
        Run = started.RunDirectory;
        Receipt.State.RunId = Path.GetFileName(Run); Receipt.Report.RunId = Receipt.State.RunId;
        SafeWrite.Require(new RunStateStore(Context).Write(Path.Combine(Run, "state.json"), Receipt.State));
        ReportWriter.WriteFiles(Context, Run, Receipt.Report);
    }
    public static CheckpointManifest Manifest() => new()
    {
        Checkpoints = [new() { Id = "CP-1", After = ["S1"], Command = "true", EstimatedMinutes = 1 }]
    };
}
