using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Coarse phase names. Callers pass only these values; nothing else becomes a label.</summary>
public enum TestPhaseKind
{
    DbCreate,
    DbMigrate,
    DbRestore,
    DbSnapshot,
    DbCleanup,
    HostStartup,
    HostShutdown,
    ProcessSpawn,
    ProcessWait,
    Filesystem,
    NetworkLoopback,
    OurCode,
    FixtureSetup,
    FixtureTeardown,
}

public sealed class TestTimingConfig
{
    public string? Root { get; init; }
    public string? RunId { get; init; }
    public string? GroupId { get; init; }
    public string? Attempt { get; init; }
    public string? Suite { get; init; }
    public int? Pid { get; init; }
    public string? Nonce { get; init; }
    public string? AssemblyId { get; init; }
    public bool ManualClock { get; init; }
    public long FrequencyHz { get; init; }
}

/// <summary>
/// Opt-in per-test phase recorder. <see cref="RootVariable"/> unset or blank is a true no-op:
/// no file, no stopwatch, no redaction, no default path. Install takes precedence and never
/// enables a process that did not call it.
/// </summary>
public static class TestTiming
{
    public const string RootVariable = "ANTIPHON_TEST_PHASE_ROOT";
    public const string RunIdVariable = "ANTIPHON_TEST_PHASE_RUN_ID";
    public const string GroupIdVariable = "ANTIPHON_TEST_PHASE_GROUP_ID";
    public const string AttemptVariable = "ANTIPHON_TEST_PHASE_ATTEMPT";
    public const string SuiteVariable = "ANTIPHON_TEST_PHASE_SUITE";
    public const int SpanCap = 4096;

    public static readonly string[] PhaseVariableNames =
    [
        RootVariable, RunIdVariable, GroupIdVariable, AttemptVariable, SuiteVariable,
    ];

    private static readonly AsyncLocal<TestTimingSession?> Ambient = new();
    private static readonly AsyncLocal<string?> BoundId = new();
    private static readonly AsyncLocal<ScopeNode?> Stack = new();
    private static readonly Lazy<TestTimingSession?> ProcessGate = new(SnapshotProcess);

    /// <summary>Antiphon.Tests installs DiagnosticsRedactor here. Other consumers omit free text.</summary>
    public static Func<string, string>? FreeTextRedactor { get; set; }

    public static TestTimingSession? Active => Resolve();

    public static IDisposable Install(TestTimingConfig config) => Install(TestTimingSession.TryOpen(config));

    public static IDisposable Install(TestTimingSession session)
    {
        var prior = Ambient.Value;
        Ambient.Value = session;
        return new AmbientLease(prior);
    }

    public static void BindTestId(string? testId) => BoundId.Value = testId;

    public static IDisposable BeginPhase(TestPhaseKind phase, string operation)
    {
        var session = Resolve();
        if (session is null || !session.Enabled)
            return Noop.Instance;
        if (session.EventDelayMilliseconds > 0)
            global::System.Threading.Thread.Sleep(session.EventDelayMilliseconds);
        return session.BeginPhase(phase, operation);
    }

    public static IDisposable BeginProcess(string exeBasename, int? argc, string category)
    {
        var session = Resolve();
        if (session is null || !session.Enabled)
            return Noop.Instance;
        return session.BeginProcess(exeBasename, argc, category);
    }

    public static IDisposable BeginFixturePhase(string fixtureId, TestPhaseKind phase, string operation)
    {
        var session = Resolve();
        if (session is null || !session.Enabled)
            return Noop.Instance;
        return session.BeginFixturePhase(fixtureId, phase, operation);
    }

    public static void RemovePhaseVariables(ProcessStartInfo start)
    {
        foreach (var name in PhaseVariableNames)
            start.Environment.Remove(name);
    }

    public static string NormalizeFreeText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "omitted";
        if (FreeTextRedactor is not null)
            text = FreeTextRedactor(text);
        if (string.IsNullOrEmpty(text) || text.Length > 128 || !IsAllowlisted(text))
            return "omitted";
        return text;
    }

    public static string DescribeArgument(object? value) =>
        value is null ? "null" : value.GetType().Name;

    internal static TestTimingSession? Resolve() => Ambient.Value ?? ProcessGate.Value;

    internal static string? ResolveOwner()
    {
        // An explicit bind is the unit-test seam and the child-task owner. It wins so a
        // stale TestContext cannot pull a phase back onto the previous case.
        if (BoundId.Value is { } bound)
            return bound;
        return TestContext.Current?.Metadata.TestDetails.TestId;
    }

    public static void RememberCurrentTest()
    {
        var context = TestContext.Current;
        if (context is not null)
            BoundId.Value = context.Metadata.TestDetails.TestId;
    }

    internal static ScopeNode? TakeOpenStack()
    {
        var node = Stack.Value;
        while (node is not null && node.Closed)
            node = node.Parent;
        if (!ReferenceEquals(node, Stack.Value))
            Stack.Value = node;
        return node;
    }

    internal static void Push(ScopeNode node) => Stack.Value = node;

    internal static void Pop(ScopeNode node)
    {
        if (ReferenceEquals(Stack.Value, node))
            Stack.Value = node.Parent;
    }

    private static TestTimingSession? SnapshotProcess()
    {
        var root = Environment.GetEnvironmentVariable(RootVariable);
        if (string.IsNullOrWhiteSpace(root))
            return null;
        return TestTimingSession.TryOpen(new TestTimingConfig
        {
            Root = root,
            RunId = Environment.GetEnvironmentVariable(RunIdVariable),
            GroupId = Environment.GetEnvironmentVariable(GroupIdVariable),
            Attempt = Environment.GetEnvironmentVariable(AttemptVariable),
            Suite = Environment.GetEnvironmentVariable(SuiteVariable),
        });
    }

    private static bool IsAllowlisted(string text)
    {
        foreach (var c in text)
        {
            var ok = c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '.' or '/' or '+' or '-';
            if (!ok)
                return false;
        }

        return true;
    }

    private sealed class AmbientLease(TestTimingSession? prior) : IDisposable
    {
        public void Dispose() => Ambient.Value = prior;
    }

    internal sealed class Noop : IDisposable
    {
        public static readonly Noop Instance = new();
        public void Dispose() { }
    }

    internal sealed class ScopeNode
    {
        public ScopeNode? Parent { get; init; }
        public int Id { get; init; }
        public bool Closed { get; set; }
    }
}

public sealed class TestTimingSession : IDisposable
{
    private readonly object _gate = new();
    private readonly object _sinkGate = new();
    private readonly TimingClock _clock;
    private readonly Dictionary<string, CaseBuffer> _cases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CaseBuffer> _fixtures = new(StringComparer.Ordinal);
    private readonly Dictionary<int, OpenScope> _open = new();
    private StreamWriter? _writer;
    private bool _enabled;
    private bool _fault;
    private bool _disposed;
    private int _nextId = 1;
    private int _dropped;

    private TestTimingSession(bool enabled, string? failure, string? path, TimingClock clock)
    {
        _enabled = enabled;
        FailureCode = failure;
        ShardPath = path;
        _clock = clock;
    }

    public bool Enabled => _enabled;

    // Test seam. TestTiming.BeginPhase sleeps this long, in milliseconds, when this
    // session is the installed recorder. Zero does not sleep.
    public int EventDelayMilliseconds { get; set; }

    public string? FailureCode { get; private set; }
    public string? ShardPath { get; }
    public long FrequencyHz => _clock.Frequency;

    public static string GroupToken(string? groupId)
    {
        if (string.IsNullOrEmpty(groupId))
            return "";
        var bytes = Encoding.UTF8.GetBytes(groupId);
        var sb = new StringBuilder(bytes.Length * 2);
        for (var i = 0; i < bytes.Length; i++)
            sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    // Same text as ConvertTo-QualificationIdentityText: length-prefixed chunk,
    // run id, attempt, and suite, separated by LF.
    public static string IdentityToken(string? groupId, string? runId, string? attempt, string? suite)
    {
        var text = IdentityText(groupId, runId, attempt, suite);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var sb = new StringBuilder(hash.Length * 2);
        for (var i = 0; i < hash.Length; i++)
            sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    public static string ChunkDirectory(string root, string? groupId, string? runId, string? attempt, string? suite) =>
        Path.Combine(root, "chunk-" + IdentityToken(groupId, runId, attempt, suite));

    // The chunk directory already holds the full identity. The owner file keeps a
    // prefix of that hash, the pid, and the nonce. The prefix is the longest one
    // whose path stays within 240 characters under
    // C:\Antiphon\releases\candidates\rc-YYYY-MM-DD-<12 hex>\logs\timing-<32 hex>.
    public static int OwnerHashPrefixLength { get; } = ComputeOwnerHashPrefixLength();

    public static string OwnerFileName(string identityToken, int pid, string nonce)
    {
        var length = OwnerHashPrefixLength;
        if (length > identityToken.Length)
            length = identityToken.Length;
        if (length < 0)
            length = 0;
        return "own-" + identityToken.Substring(0, length) + "-" + pid.ToString(CultureInfo.InvariantCulture) + "-" + nonce + ".txt";
    }

    private static int ComputeOwnerHashPrefixLength()
    {
        const string pid = "2147483647";
        var nonce = new string('0', 32);
        var windowsRoot = "C:\\Antiphon\\releases\\candidates\\rc-2026-10-27-" + new string('0', 12)
            + "\\logs\\timing-" + new string('0', 32);
        var bare = windowsRoot + "\\chunk-" + new string('0', 64) + "\\own--" + pid + "-" + nonce + ".txt";
        var room = 240 - bare.Length;
        if (room < 1)
            return 1;
        return room > 64 ? 64 : room;
    }

    private static string IdentityText(string? groupId, string? runId, string? attempt, string? suite)
    {
        groupId ??= "";
        runId ??= "";
        attempt ??= "";
        suite ??= "";
        return groupId.Length.ToString(CultureInfo.InvariantCulture) + ":" + groupId + "\n"
            + runId.Length.ToString(CultureInfo.InvariantCulture) + ":" + runId + "\n"
            + attempt.Length.ToString(CultureInfo.InvariantCulture) + ":" + attempt + "\n"
            + suite.Length.ToString(CultureInfo.InvariantCulture) + ":" + suite;
    }

    public static TestTimingSession TryOpen(TestTimingConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Root))
            return new TestTimingSession(false, null, null, TimingClock.CreateManual());

        if (!IsLocalAbsolute(config.Root))
            return new TestTimingSession(false, "phase-root-invalid", null, TimingClock.CreateManual());

        if (string.IsNullOrWhiteSpace(config.RunId) || string.IsNullOrWhiteSpace(config.GroupId)
            || string.IsNullOrWhiteSpace(config.Attempt) || string.IsNullOrWhiteSpace(config.Suite))
            return new TestTimingSession(false, "phase-metadata-missing", null, TimingClock.CreateManual());

        var clock = config.ManualClock
            ? TimingClock.CreateManual(config.FrequencyHz)
            : TimingClock.CreateReal();
        var pid = config.Pid ?? Environment.ProcessId;
        var nonce = string.IsNullOrWhiteSpace(config.Nonce) ? Guid.NewGuid().ToString("N") : config.Nonce!;
        if (!IsToken(nonce))
            return new TestTimingSession(false, "phase-root-invalid", null, clock);

        TestTimingSession? session = null;
        try
        {
            var folder = ChunkDirectory(config.Root, config.GroupId, config.RunId, config.Attempt, config.Suite);
            Directory.CreateDirectory(folder);
            var journalName = "cases-" + pid.ToString(CultureInfo.InvariantCulture) + "-" + nonce + ".jsonl";
            var path = Path.Combine(folder, journalName);
            var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096);
            session = new TestTimingSession(true, null, path, clock) { _writer = new StreamWriter(stream, new UTF8Encoding(false)) };
            var assembly = string.IsNullOrWhiteSpace(config.AssemblyId)
                ? (AssemblyId() ?? "")
                : config.AssemblyId!;
            session.WriteLine(session.HeaderJson(pid, nonce, assembly, config));
            var ownerPath = Path.Combine(
                folder,
                OwnerFileName(IdentityToken(config.GroupId, config.RunId, config.Attempt, config.Suite), pid, nonce));
            try
            {
                File.WriteAllText(ownerPath, journalName + "\n", new UTF8Encoding(false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The journal stays open. The reader attributes a missing sidecar from its header.
            }
            var opened = session;
            session = null;
            return opened;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            session?.Dispose();
            return new TestTimingSession(false, "phase-sink-failed", null, clock);
        }
    }

    public void FaultNextWrite() => _fault = true;

    public void SetTicks(long ticks) => _clock.Set(ticks);

    public long NowTicks() => _enabled || _clock.Manual ? _clock.Now() : 0;

    public long ToMs(long ticks) => _clock.ToMs(ticks);

    public IDisposable BeginPhase(TestPhaseKind phase, string operation)
    {
        if (!_enabled)
            return TestTiming.Noop.Instance;
        var owner = TestTiming.ResolveOwner();
        var parent = TestTiming.TakeOpenStack();
        var scope = Open(owner, phase, operation, _clock.Now(), parent?.Id, null, push: true);
        return scope;
    }

    public IDisposable BeginProcess(string exeBasename, int? argc, string category)
    {
        if (!_enabled)
            return TestTiming.Noop.Instance;
        var owner = TestTiming.ResolveOwner();
        int? processId = null;
        if (owner is not null)
        {
            var buffer = EnsureCase(owner, fixture: false);
            processId = buffer.AddProcess(BoundName(exeBasename), argc, BoundName(category));
        }

        var parent = TestTiming.TakeOpenStack();
        return Open(owner, TestPhaseKind.ProcessSpawn, "spawn", _clock.Now(), parent?.Id, processId, push: true);
    }

    public IDisposable BeginFixturePhase(string fixtureId, TestPhaseKind phase, string operation)
    {
        if (!_enabled)
            return TestTiming.Noop.Instance;
        var id = string.IsNullOrWhiteSpace(fixtureId) ? "fixture" : BoundName(fixtureId);
        return Open(id, phase, operation, _clock.Now(), null, null, push: false, fixture: true);
    }

    public int OpenAt(string owner, TestPhaseKind phase, string operation, long startTicks, int? parentId)
    {
        var scope = Open(owner, phase, operation, startTicks, parentId, null, push: false);
        return scope is OpenScope open ? open.Id : 0;
    }

    public void CloseAt(int id, long endTicks)
    {
        if (id == 0)
            return;
        lock (_gate)
        {
            if (!_open.TryGetValue(id, out var scope) || scope.Closed)
                return;
            scope.Closed = true;
            scope.Node.Closed = true;
            _open.Remove(id);
            AppendSpan(scope, endTicks);
        }
    }

    public void BeginCase(string testId, string className, string method, string[] args, long startTicks)
    {
        if (!_enabled)
            return;
        CaseBuffer buffer;
        lock (_gate)
        {
            buffer = EnsureCase(testId, fixture: false);
            buffer.ClassName = BoundName(className);
            buffer.Method = BoundName(method);
            buffer.Args = args;
            buffer.StartTicks = startTicks;
            buffer.Started = true;
        }

        WriteLine(StartJson(buffer));
    }

    public void EndCase(string testId, string outcome, long endTicks)
    {
        CaseBuffer? buffer;
        List<OpenScope> stillOpen;
        lock (_gate)
        {
            if (!_cases.TryGetValue(testId, out buffer) || buffer.Ended)
                return;
            stillOpen = _open.Values.Where(scope => scope.Owner == testId && !scope.Closed).ToList();
            foreach (var scope in stillOpen)
            {
                scope.Closed = true;
                scope.Node.Closed = true;
                scope.Incomplete = true;
                _open.Remove(scope.Id);
                AppendSpan(scope, endTicks);
            }

            buffer.EndTicks = endTicks;
            buffer.Outcome = BoundName(outcome);
            buffer.Ended = true;
        }

        WriteLine(CompleteJson(buffer, shared: false, fixtureId: null));
    }

    public void OnTestStart(TestContext context)
    {
        TestTiming.BindTestId(null);
        if (!_enabled)
            return;
        var details = context.Metadata.TestDetails;
        var args = Describe(details);
        BeginCase(details.TestId, details.ClassType.FullName ?? details.ClassType.Name, details.MethodName, args, _clock.Now());
        // TUnit 1.44's expanded ID differs from the MTP TRX GUID. Emit the
        // explicit identity into that case's captured output; never guess by name.
        try
        {
            context.Output.WriteLine("ANTIPHON_TEST_PHASE_ID " + details.TestId);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // Missing identity remains an incomplete join, never a test failure.
        }
    }

    public void OnTestEnd(TestContext context)
    {
        if (!_enabled)
            return;
        var id = context.Metadata.TestDetails.TestId;
        var outcome = "incomplete";
        try
        {
            var state = context.Execution.Result?.State.ToString();
            if (!string.IsNullOrWhiteSpace(state))
                outcome = state!;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException)
        {
            outcome = "incomplete";
        }

        EndCase(id, outcome, _clock.Now());
    }

    public void FlushHealth()
    {
        List<OpenScope> stillOpen;
        lock (_gate)
            stillOpen = _open.Values.Where(scope => scope.Fixture && !scope.Closed).ToList();
        foreach (var scope in stillOpen)
        {
            scope.Incomplete = true;
            CloseAt(scope.Id, _clock.Now());
        }

        lock (_gate)
        {
            foreach (var pair in _fixtures)
            {
                if (!pair.Value.Ended)
                {
                    pair.Value.EndTicks = _clock.Now();
                    pair.Value.Ended = true;
                    WriteLine(CompleteJson(pair.Value, shared: true, fixtureId: pair.Key));
                }
            }
        }

        WriteLine("{\"recordKind\":\"health\",\"failure\":" + JsonString(FailureCode) + ",\"droppedPhaseCount\":" + _dropped.ToString(CultureInfo.InvariantCulture) + "}");
        // WriteLine already flushes the health record under the sink lock and
        // contains I/O failures. An additional unguarded flush can escape.
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (!_enabled && _writer is null)
            return;
        List<string> openCases;
        lock (_gate)
            openCases = _cases.Values.Where(item => item.Started && !item.Ended).Select(item => item.TestId).ToList();
        foreach (var id in openCases)
            EndCase(id, "incomplete", _clock.Now());
        FlushHealth();
        lock (_sinkGate)
        {
            try
            {
                _writer?.Dispose();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                FailureCode = "phase-sink-failed";
            }
            finally
            {
                _writer = null;
                _enabled = false;
            }
        }
    }

    private IDisposable Open(string? owner, TestPhaseKind phase, string operation, long startTicks, int? parentId, int? processId, bool push, bool fixture = false)
    {
        if (owner is null)
            return TestTiming.Noop.Instance;
        OpenScope scope;
        lock (_gate)
        {
            var buffer = EnsureCase(owner, fixture);
            if (buffer.Spans.Count >= TestTiming.SpanCap)
            {
                buffer.Dropped++;
                _dropped++;
                return TestTiming.Noop.Instance;
            }

            var id = _nextId++;
            var node = new TestTiming.ScopeNode { Id = id, Parent = push ? TestTiming.TakeOpenStack() : null };
            scope = new OpenScope(this, id, owner, phase, BoundOperation(operation), startTicks, parentId, processId, node, fixture);
            _open[id] = scope;
            if (push)
                TestTiming.Push(node);
        }

        return scope;
    }

    private CaseBuffer EnsureCase(string id, bool fixture)
    {
        var map = fixture ? _fixtures : _cases;
        if (!map.TryGetValue(id, out var buffer))
        {
            buffer = new CaseBuffer(id) { StartTicks = _clock.Now() };
            map[id] = buffer;
        }

        return buffer;
    }

    private void AppendSpan(OpenScope scope, long endTicks)
    {
        var buffer = EnsureCase(scope.Owner, scope.Fixture);
        if (buffer.Spans.Count >= TestTiming.SpanCap)
        {
            buffer.Dropped++;
            _dropped++;
            return;
        }

        var end = endTicks < scope.StartTicks ? scope.StartTicks : endTicks;
        buffer.Spans.Add(new SpanRow(scope.Id, scope.ParentId, scope.Phase, scope.Operation, scope.StartTicks, end, scope.Incomplete, scope.ProcessId));
    }

    private void WriteLine(string json)
    {
        lock (_sinkGate)
        {
            if (!_enabled || _writer is null)
                return;
            try
            {
                if (_fault)
                {
                    _fault = false;
                    throw new IOException("phase-sink-failed");
                }
                _writer.WriteLine(json);
                _writer.Flush();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                FailureCode = "phase-sink-failed";
                _enabled = false;
            }
        }
    }

    private string HeaderJson(int pid, string nonce, string assembly, TestTimingConfig config) =>
        "{\"recordKind\":\"header\",\"version\":1,\"pid\":" + pid.ToString(CultureInfo.InvariantCulture)
        + ",\"nonce\":" + JsonString(nonce)
        + ",\"frequencyHz\":" + _clock.Frequency.ToString(CultureInfo.InvariantCulture)
        + ",\"epochUtc\":" + JsonString(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture))
        + ",\"runId\":" + JsonString(config.RunId)
        + ",\"groupId\":" + JsonString(config.GroupId)
        + ",\"attempt\":" + JsonString(config.Attempt)
        + ",\"suite\":" + JsonString(config.Suite)
        + ",\"assembly\":" + JsonString(assembly)
        + ",\"moduleVersionId\":" + JsonString(AssemblyModuleId()) + "}";

    private string StartJson(CaseBuffer buffer)
    {
        var start = _clock.ToMs(buffer.StartTicks - _clock.Epoch);
        return "{\"recordKind\":\"start\",\"testId\":" + JsonString(buffer.TestId)
            + ",\"className\":" + JsonString(buffer.ClassName)
            + ",\"method\":" + JsonString(buffer.Method)
            + ",\"args\":" + JsonArray(buffer.Args)
            + ",\"startOffsetMs\":" + start.ToString(CultureInfo.InvariantCulture) + "}";
    }

    private string CompleteJson(CaseBuffer buffer, bool shared, string? fixtureId)
    {
        var start = _clock.ToMs(buffer.StartTicks - _clock.Epoch);
        var end = _clock.ToMs(buffer.EndTicks - _clock.Epoch);
        if (end < start)
            end = start;
        var allocation = TestTimingMath.Allocate(start, end, buffer.Spans.Select(span => span.ToInterval(_clock)).ToList());
        var sb = new StringBuilder(512);
        sb.Append("{\"recordKind\":");
        sb.Append(shared ? "\"fixture\"" : "\"complete\"");
        sb.Append(",\"testId\":");
        sb.Append(shared ? "null" : JsonString(buffer.TestId));
        if (shared)
        {
            sb.Append(",\"fixtureId\":");
            sb.Append(JsonString(fixtureId));
        }

        sb.Append(",\"className\":").Append(JsonString(buffer.ClassName));
        sb.Append(",\"method\":").Append(JsonString(buffer.Method));
        sb.Append(",\"args\":").Append(JsonArray(buffer.Args));
        sb.Append(",\"startOffsetMs\":").Append(start.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"endOffsetMs\":").Append(end.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"outcome\":").Append(JsonString(string.IsNullOrEmpty(buffer.Outcome) ? "incomplete" : buffer.Outcome));
        sb.Append(",\"phases\":[");
        for (var i = 0; i < buffer.Spans.Count; i++)
        {
            if (i > 0)
                sb.Append(',');
            buffer.Spans[i].WriteJson(sb, _clock);
        }

        sb.Append("],\"processes\":[");
        for (var i = 0; i < buffer.Processes.Count; i++)
        {
            if (i > 0)
                sb.Append(',');
            buffer.Processes[i].WriteJson(sb);
        }

        sb.Append("],\"exclusive\":");
        allocation.WriteExclusive(sb);
        sb.Append(",\"overlapMs\":").Append(allocation.Overlap.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"remainderMs\":").Append(allocation.Remainder.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"childUnionMs\":").Append(allocation.ChildUnion.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"droppedPhaseCount\":").Append(buffer.Dropped.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"shared\":").Append(shared ? "true" : "false");
        sb.Append(",\"incomplete\":").Append(buffer.Spans.Any(span => span.Incomplete) || buffer.Outcome == "incomplete" ? "true" : "false");
        sb.Append('}');
        return sb.ToString();
    }

    private static string[] Describe(TestDetails details)
    {
        var values = details.Method.TestMethodArguments;
        if (values is null || values.Length == 0)
            return [];
        var args = new string[values.Length];
        for (var i = 0; i < values.Length; i++)
            args[i] = TestTiming.DescribeArgument(values[i]);
        return args;
    }

    private static string? AssemblyId()
    {
        try
        {
            return System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string? AssemblyModuleId()
    {
        try { return System.Reflection.Assembly.GetEntryAssembly()?.ManifestModule.ModuleVersionId.ToString("N"); }
        catch (InvalidOperationException) { return null; }
    }

    private static string BoundOperation(string? operation) => operation switch
    {
        "clone" or "migrate" or "restore" or "dump" or "create-tree" or "copy-tree"
            or "delete-tree" or "queue" or "start" or "stop" or "drop" or "spawn"
            or "wait" or "before" or "after" or "child" or "span" => operation,
        _ => "omitted",
    };

    private static string BoundName(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        var sb = new StringBuilder(Math.Min(value.Length, 128));
        foreach (var c in value)
        {
            if (c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '.' or '+' or '-')
                sb.Append(c);
            if (sb.Length >= 128)
                break;
        }

        return sb.Length == 0 ? "omitted" : sb.ToString();
    }

    internal static string JsonString(string? value)
    {
        if (value is null)
            return "null";
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                default:
                    if (c < ' ')
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    private static string JsonArray(string[]? values)
    {
        if (values is null || values.Length == 0)
            return "[]";
        var sb = new StringBuilder();
        sb.Append('[');
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0)
                sb.Append(',');
            sb.Append(JsonString(values[i]));
        }

        sb.Append(']');
        return sb.ToString();
    }

    private static bool IsLocalAbsolute(string root)
    {
        if (root.StartsWith("\\\\", StringComparison.Ordinal) || root.StartsWith("//", StringComparison.Ordinal))
            return false;
        return Path.IsPathRooted(root);
    }

    private static bool IsToken(string value)
    {
        if (value.Length is 0 or > 64)
            return false;
        foreach (var c in value)
        {
            if (c is not ((>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-'))
                return false;
        }

        return true;
    }

    private sealed class OpenScope : IDisposable
    {
        private readonly TestTimingSession _owner;
        private int _disposed;

        public OpenScope(TestTimingSession owner, int id, string ownerId, TestPhaseKind phase, string operation, long startTicks, int? parentId, int? processId, TestTiming.ScopeNode node, bool fixture)
        {
            _owner = owner;
            Id = id;
            Owner = ownerId;
            Phase = phase;
            Operation = operation;
            StartTicks = startTicks;
            ParentId = parentId;
            ProcessId = processId;
            Node = node;
            Fixture = fixture;
        }

        public int Id { get; }
        public string Owner { get; }
        public TestPhaseKind Phase { get; }
        public string Operation { get; }
        public long StartTicks { get; }
        public int? ParentId { get; }
        public int? ProcessId { get; }
        public TestTiming.ScopeNode Node { get; }
        public bool Fixture { get; }
        public bool Closed { get; set; }
        public bool Incomplete { get; set; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            _owner.CloseAt(Id, _owner._clock.Now());
            TestTiming.Pop(Node);
        }
    }
}

public static class TestTimingMath
{
    public static TimingAllocation Allocate(long start, long end, IReadOnlyList<TimingInterval> spans)
    {
        if (end < start)
            end = start;
        var width = end - start;
        var points = new SortedSet<long> { start, end };
        var capped = new List<TimingInterval>();
        foreach (var span in spans)
        {
            if (TestTimingMathNames.Coarse(span.Phase) == TimingCoarse.None)
                continue;
            var a = Math.Max(span.Start, start);
            var b = Math.Min(span.End, end);
            if (b <= a)
                continue;
            var copy = span with { Start = a, End = b };
            capped.Add(copy);
            points.Add(a);
            points.Add(b);
        }

        long db = 0, host = 0, process = 0, fs = 0, network = 0, fixture = 0, overlap = 0, covered = 0;
        var ordered = points.ToArray();
        for (var i = 0; i < ordered.Length - 1; i++)
        {
            var a = ordered[i];
            var b = ordered[i + 1];
            var segment = b - a;
            if (segment <= 0)
                continue;
            var covering = capped.Where(span => span.Start < b && span.End > a).ToList();
            if (covering.Count == 0)
                continue;
            var active = covering.Where(span => !covering.Any(other => other.Id != span.Id && IsDescendant(other, span, covering))).ToList();
            if (active.Count == 0)
                continue;
            covered += segment;
            var categories = active.Select(span => TestTimingMathNames.Coarse(span.Phase)).Distinct().ToList();
            if (categories.Count > 1)
                overlap += segment;
            else
            {
                switch (categories[0])
                {
                    case TimingCoarse.Db: db += segment; break;
                    case TimingCoarse.Host: host += segment; break;
                    case TimingCoarse.Process: process += segment; break;
                    case TimingCoarse.Fs: fs += segment; break;
                    case TimingCoarse.Network: network += segment; break;
                    case TimingCoarse.Fixture: fixture += segment; break;
                }
            }
        }

        var union = UnionLength(capped, start, end);
        return new TimingAllocation(db, host, process, fs, network, fixture, overlap, width - union, union);
    }

    private static bool IsDescendant(TimingInterval candidate, TimingInterval ancestor, List<TimingInterval> covering)
    {
        var parent = candidate.ParentId;
        var guard = 0;
        while (parent is not null && guard++ < 64)
        {
            if (parent == ancestor.Id)
                return true;
            var next = covering.FirstOrDefault(span => span.Id == parent.Value);
            if (next.Id == 0)
                return false;
            parent = next.ParentId;
        }

        return false;
    }

    private static long UnionLength(List<TimingInterval> spans, long start, long end)
    {
        if (spans.Count == 0)
            return 0;
        var ordered = spans.OrderBy(span => span.Start).ToList();
        long total = 0;
        var cursor = ordered[0].Start;
        var until = ordered[0].End;
        foreach (var span in ordered.Skip(1))
        {
            if (span.Start > until)
            {
                total += until - cursor;
                cursor = span.Start;
                until = span.End;
            }
            else if (span.End > until)
                until = span.End;
        }

        total += until - cursor;
        if (cursor < start)
            total -= start - cursor;
        return total < 0 ? 0 : total;
    }
}

public enum TimingCoarse
{
    None,
    Db,
    Host,
    Process,
    Fs,
    Network,
    Fixture,
}

public readonly record struct TimingInterval(int Id, int? ParentId, string Phase, long Start, long End);

public readonly record struct TimingAllocation(long Db, long Host, long Process, long Fs, long Network, long Fixture, long Overlap, long Remainder, long ChildUnion)
{
    public void WriteExclusive(StringBuilder sb)
    {
        sb.Append("{\"db\":").Append(Db.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"host\":").Append(Host.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"process\":").Append(Process.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"fs\":").Append(Fs.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"network\":").Append(Network.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"fixture\":").Append(Fixture.ToString(CultureInfo.InvariantCulture));
        sb.Append('}');
    }
}

internal static class TestTimingMathNames
{
    public static TimingCoarse Coarse(string phase) => phase switch
    {
        "db-create" or "db-migrate" or "db-restore" or "db-snapshot" or "db-cleanup" => TimingCoarse.Db,
        "host-startup" or "host-shutdown" => TimingCoarse.Host,
        "process-spawn" or "process-wait" => TimingCoarse.Process,
        "filesystem" => TimingCoarse.Fs,
        "network-loopback" => TimingCoarse.Network,
        "fixture-setup" or "fixture-teardown" => TimingCoarse.Fixture,
        _ => TimingCoarse.None,
    };

    public static string Name(TestPhaseKind phase) => phase switch
    {
        TestPhaseKind.DbCreate => "db-create",
        TestPhaseKind.DbMigrate => "db-migrate",
        TestPhaseKind.DbRestore => "db-restore",
        TestPhaseKind.DbSnapshot => "db-snapshot",
        TestPhaseKind.DbCleanup => "db-cleanup",
        TestPhaseKind.HostStartup => "host-startup",
        TestPhaseKind.HostShutdown => "host-shutdown",
        TestPhaseKind.ProcessSpawn => "process-spawn",
        TestPhaseKind.ProcessWait => "process-wait",
        TestPhaseKind.Filesystem => "filesystem",
        TestPhaseKind.NetworkLoopback => "network-loopback",
        TestPhaseKind.OurCode => "our-code",
        TestPhaseKind.FixtureSetup => "fixture-setup",
        TestPhaseKind.FixtureTeardown => "fixture-teardown",
        _ => "omitted",
    };
}

internal sealed class TimingClock
{
    private long _ticks;
    public bool Manual { get; }
    public long Frequency { get; }
    public long Epoch { get; }

    private TimingClock(bool manual, long frequency)
    {
        Manual = manual;
        Frequency = frequency;
        Epoch = manual ? 0 : Stopwatch.GetTimestamp();
    }

    public static TimingClock CreateManual(long frequency = 0) => new(true, frequency > 0 ? frequency : 10_000_000L);
    public static TimingClock CreateReal() => new(false, Stopwatch.Frequency);
    public long Now() => Manual ? Interlocked.Read(ref _ticks) : Stopwatch.GetTimestamp();
    public void Set(long ticks) => Interlocked.Exchange(ref _ticks, ticks);
    public long ToMs(long ticks) => ticks * 1000L / Frequency;
}

internal sealed class CaseBuffer(string testId)
{
    public string TestId { get; } = testId;
    public string ClassName { get; set; } = "";
    public string Method { get; set; } = "";
    public string[] Args { get; set; } = [];
    public string Outcome { get; set; } = "";
    public long StartTicks { get; set; }
    public long EndTicks { get; set; }
    public bool Started { get; set; }
    public bool Ended { get; set; }
    public int Dropped { get; set; }
    public List<SpanRow> Spans { get; } = [];
    public List<ProcessRow> Processes { get; } = [];

    public int AddProcess(string exe, int? argc, string category)
    {
        var id = Processes.Count + 1;
        Processes.Add(new ProcessRow(id, exe, category, argc));
        return id;
    }

    public long ClockOffset(long ticks) => ticks;
}

internal sealed record SpanRow(int Id, int? ParentId, TestPhaseKind Phase, string Operation, long StartTicks, long EndTicks, bool Incomplete, int? ProcessId)
{
    public TimingInterval ToInterval(TimingClock clock) =>
        new(Id, ParentId, TestTimingMathNames.Name(Phase), clock.ToMs(StartTicks - clock.Epoch), clock.ToMs(EndTicks - clock.Epoch));

    public void WriteJson(StringBuilder sb, TimingClock clock)
    {
        var start = clock.ToMs(StartTicks - clock.Epoch);
        var end = clock.ToMs(EndTicks - clock.Epoch);
        if (end < start)
            end = start;
        sb.Append("{\"id\":").Append(Id.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"parent\":").Append(ParentId?.ToString(CultureInfo.InvariantCulture) ?? "null");
        sb.Append(",\"phase\":").Append(TestTimingSession.JsonString(TestTimingMathNames.Name(Phase)));
        sb.Append(",\"operation\":").Append(TestTimingSession.JsonString(Operation));
        sb.Append(",\"startOffsetMs\":").Append(start.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"endOffsetMs\":").Append(end.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"incomplete\":").Append(Incomplete ? "true" : "false");
        sb.Append('}');
    }
}

internal sealed record ProcessRow(int Id, string Exe, string Category, int? Argc)
{
    public void WriteJson(StringBuilder sb)
    {
        sb.Append("{\"id\":").Append(Id.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"exe\":").Append(TestTimingSession.JsonString(Exe));
        sb.Append(",\"category\":").Append(TestTimingSession.JsonString(Category));
        sb.Append(",\"argc\":").Append(Argc?.ToString(CultureInfo.InvariantCulture) ?? "null");
        sb.Append(",\"count\":1}");
    }
}
