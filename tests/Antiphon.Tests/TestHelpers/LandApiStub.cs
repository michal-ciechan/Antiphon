using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Path-aware loopback stub for <c>delegate.ps1 -Land</c> (CARD-0495). Distinguishes
/// <c>/api/version</c>, task status, <c>/land/v2</c> and legacy <c>/land</c> so a silent
/// fallback is observable as <see cref="LegacyLandPosts"/>.
/// </summary>
internal sealed class LandApiStub : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Task _pump;
    private readonly ConcurrentQueue<LandApiCall> _requests = new();
    private int _legacyLandPosts;

    internal LandApiStub(LandApiStubOptions options)
    {
        Options = options;
        Url = EphemeralHttpListener.BindLoopback(_listener);
        _pump = Task.Run(PumpAsync);
    }

    public LandApiStubOptions Options { get; }
    public string Url { get; }
    public IReadOnlyList<LandApiCall> Requests => _requests.ToArray();
    public int LegacyLandPosts => Volatile.Read(ref _legacyLandPosts);

    public static LandApiStub Old(string sha, string? v2Body = null, string? taskStatusBody = null) =>
        new(new LandApiStubOptions
        {
            VersionBody = TwoFieldVersion(sha),
            V2Status = 404,
            V2Body = v2Body ?? "{}",
            TaskStatusBody = taskStatusBody ?? "{}",
        });

    public static LandApiStub Compatible(
        string sha,
        IReadOnlyList<string>? capabilities = null,
        string? v2Body = null,
        string? taskStatusBody = null,
        string? extraJsonFields = null) =>
        new(new LandApiStubOptions
        {
            VersionBody = CompatibleVersion(sha, capabilities ?? ["land-v2"], extraJsonFields),
            V2Body = v2Body ?? DefaultQueuedBody(),
            TaskStatusBody = taskStatusBody ?? "{}",
        });

    public static LandApiStub ProcessSwap(string sha, int v2Status = 404) =>
        new(new LandApiStubOptions
        {
            VersionBody = CompatibleVersion(sha, ["land-v2"], extraJsonFields: null),
            V2Status = v2Status,
            V2Body = """{"error":"not found"}""",
        });

    public static LandApiStub WithVersion(
        int status,
        string body,
        TimeSpan? delay = null,
        string? v2Body = null) =>
        new(new LandApiStubOptions
        {
            VersionStatus = status,
            VersionBody = body,
            VersionDelay = delay ?? TimeSpan.Zero,
            V2Body = v2Body ?? DefaultQueuedBody(),
        });

    public static string TwoFieldVersion(string sha) =>
        JsonSerializer.Serialize(new { version = sha, informationalVersion = sha });

    public static string CompatibleVersion(string sha, IReadOnlyList<string> capabilities, string? extraJsonFields)
    {
        var caps = JsonSerializer.Serialize(capabilities);
        var extra = string.IsNullOrWhiteSpace(extraJsonFields) ? "" : "," + extraJsonFields;
        return $$"""{"version":{{JsonSerializer.Serialize(sha)}},"informationalVersion":{{JsonSerializer.Serialize(sha)}},"capabilities":{{caps}}{{extra}}}""";
    }

    public static string DefaultQueuedBody(Guid? requestId = null, string status = "queued", string notification = "tracked") =>
        JsonSerializer.Serialize(new { requestId = requestId ?? Guid.NewGuid(), status, notification });

    private async Task PumpAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }

            try { await HandleAsync(context); }
            catch (HttpListenerException) { }
            catch (ObjectDisposedException) { }
            catch (IOException) { }
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath ?? "";
        var method = context.Request.HttpMethod ?? "";
        var body = "";
        if (context.Request.HasEntityBody)
        {
            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
            body = await reader.ReadToEndAsync();
        }

        _requests.Enqueue(new LandApiCall(method, path, body));

        if (method == "GET" && path == "/api/version")
        {
            if (Options.VersionDelay > TimeSpan.Zero)
                await Task.Delay(Options.VersionDelay);
            await WriteAsync(context, Options.VersionStatus, Options.VersionBody, json: Options.VersionIsJson);
            return;
        }

        if (method == "GET" && IsTaskRoot(path))
        {
            await WriteAsync(context, 200, Options.TaskStatusBody, json: true);
            return;
        }

        if (method == "POST" && path.EndsWith("/land/v2", StringComparison.Ordinal))
        {
            if (Options.V2AbortAfterRead)
            {
                context.Response.Abort();
                return;
            }

            await WriteAsync(context, Options.V2Status, Options.V2Body, json: true);
            return;
        }

        if (method == "POST" && path.EndsWith("/land", StringComparison.Ordinal)
            && !path.EndsWith("/land/v2", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _legacyLandPosts);
            await WriteAsync(context, 202, Options.LegacyLandBody, json: true);
            return;
        }

        if (method == "POST" && path.EndsWith("/reply", StringComparison.Ordinal))
        {
            await WriteAsync(context, 200, Options.ReplyBody, json: true);
            return;
        }

        await WriteAsync(context, 404, """{"title":"Not Found"}""", json: true);
    }

    private static bool IsTaskRoot(string path)
    {
        const string prefix = "/api/agent-tasks/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        var rest = path[prefix.Length..];
        return rest.Length > 0 && !rest.Contains('/');
    }

    private static async Task WriteAsync(HttpListenerContext context, int status, string body, bool json)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.StatusCode = status;
        context.Response.ContentType = json ? "application/json" : "text/plain";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    public async ValueTask DisposeAsync()
    {
        try { _listener.Close(); }
        catch (ObjectDisposedException) { }
        try { await _pump; }
        catch (Exception) { }
    }
}

internal sealed record LandApiCall(string Method, string Path, string Body);

internal sealed class LandApiStubOptions
{
    public int VersionStatus { get; init; } = 200;
    public string VersionBody { get; init; } = "{}";
    public bool VersionIsJson { get; init; } = true;
    public TimeSpan VersionDelay { get; init; }
    public string TaskStatusBody { get; init; } = "{}";
    public int V2Status { get; init; } = 202;
    public string V2Body { get; init; } = "{}";
    public bool V2AbortAfterRead { get; init; }
    public string LegacyLandBody { get; init; } = """{"status":"queued","notification":"tracked"}""";
    public string ReplyBody { get; init; } = "{}";
}
