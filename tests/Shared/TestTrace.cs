using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// OpenTelemetry-style spans for test fixtures. A span is a plain <see cref="Activity"/> from
/// <see cref="SourceName"/>, so anything already listening sees it:
/// <list type="bullet">
/// <item>TUnit's HTML report nests spans started inside a test under that test's trace (as
/// "external spans", capped per run by <c>TUNIT_OTEL_MAX_EXTERNAL_SPANS</c>, default 100).</item>
/// <item>With <see cref="FileVariable"/> set, every completed span from every source (this one,
/// TUnit, Npgsql, HttpClient, ASP.NET Core) is appended to that file as one JSON object per line,
/// with no cap. <c>scripts/summarize-test-trace.ps1</c> turns it into per-test and per-span totals.</item>
/// <item>In assemblies that compile <c>TestTraceOtlp.cs</c>, with <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>
/// set, every span is also exported over OTLP (a collector, Jaeger or the Aspire dashboard).</item>
/// </list>
/// With no listener, <see cref="Span"/> returns null and costs nothing. Span names and tag values
/// are code-supplied labels; never pass payloads, prompts, paths with secrets or credentials.
/// </summary>
public static partial class TestTrace
{
    public const string SourceName = "Antiphon.Tests";
    public const string FileVariable = "ANTIPHON_TEST_TRACE_FILE";

    public static readonly ActivitySource Source = new(SourceName);

    private static readonly object Gate = new();
    private static ActivityListener? _listener;
    private static FileSink? _sink;

    /// <summary>Starts a child span of whatever span is current (the test body, or an enclosing fixture span).</summary>
    public static Activity? Span(string name) => Source.StartActivity(name, ActivityKind.Internal);

    public static Activity? Span(string name, string key, object? value)
    {
        var activity = Source.StartActivity(name, ActivityKind.Internal);
        activity?.SetTag(key, value);
        return activity;
    }

    public static Activity? Span(string name, params (string Key, object? Value)[] tags)
    {
        var activity = Source.StartActivity(name, ActivityKind.Internal);
        if (activity is not null)
            foreach (var (key, value) in tags)
                activity.SetTag(key, value);
        return activity;
    }

    /// <summary>Runs <paramref name="body"/> inside a span and marks the span failed when it throws.</summary>
    public static async Task<T> SpanAsync<T>(string name, Func<Task<T>> body)
    {
        using var activity = Span(name);
        try { return await body(); }
        catch (Exception ex) { Fail(activity, ex); throw; }
    }

    public static async Task SpanAsync(string name, Func<Task> body)
    {
        using var activity = Span(name);
        try { await body(); }
        catch (Exception ex) { Fail(activity, ex); throw; }
    }

    private static void Fail(Activity? activity, Exception ex) =>
        activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

    /// <summary>
    /// Runs when the test assembly loads, before discovery and every assembly hook, so the
    /// Postgres container, schema cache and other assembly setup are traced too.
    /// </summary>
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void InstallAtLoad()
    {
        EnsureFileExporter();
        InstallOtlp();
    }

    /// <summary>Implemented in TestTraceOtlp.cs where the OTLP exporter package is referenced; erased elsewhere.</summary>
    static partial void InstallOtlp();

    static partial void FlushOtlp();

    /// <summary>
    /// Installs the JSONL exporter once when <see cref="FileVariable"/> names a file. Unset or blank
    /// installs nothing. Idempotent and cheap; also called from the assembly's test-start receiver.
    /// </summary>
    public static void EnsureFileExporter()
    {
        if (Volatile.Read(ref _listener) is not null)
            return;
        var path = Environment.GetEnvironmentVariable(FileVariable);
        if (string.IsNullOrWhiteSpace(path))
            return;
        lock (Gate)
        {
            if (_listener is not null)
                return;
            try
            {
                _sink = new FileSink(Path.GetFullPath(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                Console.Error.WriteLine($"[TestTrace] {FileVariable} could not be opened ({ex.GetType().Name}); tracing to file is off.");
                _listener = new ActivityListener { ShouldListenTo = _ => false };
                return;
            }
            var sink = _sink;
            var listener = new ActivityListener
            {
                ShouldListenTo = _ => true,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = sink.Write,
            };
            ActivitySource.AddActivityListener(listener);
            AppDomain.CurrentDomain.ProcessExit += (_, _) => sink.Flush();
            Volatile.Write(ref _listener, listener);
        }
    }

    /// <summary>Flushes buffered spans; called after the last test in the assembly.</summary>
    public static void Flush()
    {
        _sink?.Flush();
        FlushOtlp();
    }

    private sealed class FileSink
    {
        private readonly string _path;
        private readonly ConcurrentQueue<string> _pending = new();
        private readonly object _write = new();
        private int _count;

        public FileSink(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, "");
            _path = path;
        }

        public void Write(Activity activity)
        {
            var line = JsonSerializer.Serialize(new SpanRecord(
                activity.TraceId.ToHexString(),
                activity.SpanId.ToHexString(),
                activity.ParentSpanId == default ? null : activity.ParentSpanId.ToHexString(),
                activity.Source.Name,
                activity.DisplayName,
                activity.StartTimeUtc,
                Math.Round(activity.Duration.TotalMilliseconds, 3),
                activity.Status == ActivityStatusCode.Error ? "error" : null,
                activity.TagObjects.Any()
                    ? activity.TagObjects.ToDictionary(t => t.Key, t => t.Value?.ToString(), StringComparer.Ordinal)
                    : null,
                Environment.ProcessId));
            _pending.Enqueue(line);
            if (Interlocked.Increment(ref _count) % 256 == 0)
                Flush();
        }

        public void Flush()
        {
            lock (_write)
            {
                if (_pending.IsEmpty)
                    return;
                var builder = new StringBuilder();
                while (_pending.TryDequeue(out var line))
                    builder.Append(line).Append('\n');
                try { File.AppendAllText(_path, builder.ToString()); }
                catch (IOException) { /* a trace write never fails a test */ }
            }
        }
    }

    private sealed record SpanRecord(
        string TraceId, string SpanId, string? ParentId, string Source, string Name,
        DateTime Start, double DurationMs, string? Status, Dictionary<string, string?>? Tags, int Pid);
}
