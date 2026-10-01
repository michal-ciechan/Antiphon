using System.IO.Pipes;
using System.Net.Sockets;

namespace Antiphon.SessionRunner;

/// <summary>Owns one Herdr request or subscription connection.</summary>
internal sealed class HerdrConnection(Stream stream, string? instanceId) : IAsyncDisposable
{
    public Stream Stream { get; } = stream;
    public string? InstanceId { get; } = instanceId;
    public ValueTask DisposeAsync() => Stream.DisposeAsync();
}

internal static class HerdrTransport
{
    // Test seam for an OS connect that remains pending; successful connections still use the native call.
    internal static AsyncLocal<Func<CancellationToken, Task>?> PendingConnectOverride { get; } = new();
    internal static async Task<HerdrConnection> ConnectAsync(
        string endpoint, int timeoutMs, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(1, timeoutMs)));
        if (OperatingSystem.IsWindows())
        {
            var pipe = new NamedPipeClientStream(".", endpoint, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(timeout.Token);
                return new HerdrConnection(pipe, HerdrPeerIdentity.FromPipe(pipe.SafePipeHandle));
            }
            catch (Exception ex)
            {
                await pipe.DisposeAsync();
                throw MapConnectFailure(endpoint, timeoutMs, cancellationToken, ex);
            }
        }

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            if (PendingConnectOverride.Value is { } pending)
                await pending(timeout.Token);
            else
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(endpoint), timeout.Token);
            var identity = HerdrPeerIdentity.FromSocket(socket);
            return new HerdrConnection(new NetworkStream(socket, ownsSocket: true), identity);
        }
        catch (Exception ex)
        {
            socket.Dispose();
            throw MapConnectFailure(endpoint, timeoutMs, cancellationToken, ex);
        }
    }

    private static Exception MapConnectFailure(string endpoint, int timeoutMs, CancellationToken caller, Exception ex)
    {
        if (ex is OperationCanceledException && caller.IsCancellationRequested)
            return ex;
        if (ex is OperationCanceledException)
            return new HerdrBackendUnavailableException(
                $"Herdr is unavailable: endpoint '{endpoint}' did not accept a connection within {timeoutMs} ms.", ex);
        if (ex is IOException or SocketException or ArgumentException)
            return new HerdrBackendUnavailableException($"Herdr is unavailable: could not connect to endpoint '{endpoint}'.", ex);
        return ex;
    }
}

/// <summary>Pure endpoint resolver; the supplied environment snapshot is also used by tests.</summary>
internal static class HerdrEndpointResolver
{
    internal static string Resolve(HerdrSettings settings, string? socketOverride,
        Func<string, string?> environment, Func<Environment.SpecialFolder, string> folder,
        bool windows, Func<string>? unixTempPath = null)
    {
        if (socketOverride is not null) return Validate(socketOverride, windows);
        if (!string.IsNullOrWhiteSpace(settings.SocketPath)) return Validate(settings.SocketPath, windows);
        if (!string.IsNullOrWhiteSpace(settings.Session)) return SessionPath(settings.Session, environment, folder, windows, unixTempPath);
        var socket = environment("HERDR_SOCKET_PATH");
        if (!string.IsNullOrWhiteSpace(socket)) return Validate(socket, windows);
        var session = environment("HERDR_SESSION");
        if (!string.IsNullOrWhiteSpace(session)) return SessionPath(session, environment, folder, windows, unixTempPath);
        return DefaultPath(environment, folder, windows, unixTempPath);
    }

    private static string SessionPath(string session, Func<string, string?> environment,
        Func<Environment.SpecialFolder, string> folder, bool windows, Func<string>? unixTempPath) =>
        windows
            ? Path.Combine(folder(Environment.SpecialFolder.ApplicationData), "herdr", "sessions", session, "herdr.sock")
            : session == "default"
                ? DefaultPath(environment, folder, windows, unixTempPath)
                : UnixJoin(UnixConfigRoot(environment, unixTempPath), "herdr", "sessions", session, "herdr.sock");

    private static string DefaultPath(Func<string, string?> environment,
        Func<Environment.SpecialFolder, string> folder, bool windows, Func<string>? unixTempPath) =>
        windows
            ? Path.Combine(folder(Environment.SpecialFolder.ApplicationData), "herdr", "herdr.sock")
            : UnixJoin(UnixConfigRoot(environment, unixTempPath), "herdr", "herdr.sock");

    // POSIX path rules must not depend on the OS running a resolver test.
    internal static bool IsUnixAbsolute(string path) => path.StartsWith("/", StringComparison.Ordinal);
    internal static string UnixJoin(params string[] parts) =>
        string.Join("/", parts.Select((part, index) => index == 0 ? part.TrimEnd('/') : part.Trim('/')));

    private static string UnixConfigRoot(Func<string, string?> environment, Func<string>? unixTempPath)
    {
        var xdg = environment("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdg) && IsUnixAbsolute(xdg)) return xdg;
        var home = environment("HOME");
        if (!string.IsNullOrWhiteSpace(home) && IsUnixAbsolute(home))
            return UnixJoin(home, ".config");
        // The live caller supplies Path.GetTempPath; pure simulated resolution defaults to /tmp.
        var temp = unixTempPath?.Invoke() ?? "/tmp/";
        if (IsUnixAbsolute(temp)) return temp;
        throw new HerdrBackendUnavailableException("Herdr has no absolute Unix configuration directory.");
    }

    private static string Validate(string path, bool windows)
    {
        if (!windows && !IsUnixAbsolute(path))
            throw new HerdrBackendUnavailableException($"Herdr Unix socket endpoint must be absolute: '{path}'.");
        return path;
    }
}
