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
        bool windows)
    {
        if (socketOverride is not null) return Validate(socketOverride, windows);
        if (!string.IsNullOrWhiteSpace(settings.SocketPath)) return Validate(settings.SocketPath, windows);
        if (!string.IsNullOrWhiteSpace(settings.Session)) return SessionPath(settings.Session, environment, folder, windows);
        var socket = environment("HERDR_SOCKET_PATH");
        if (!string.IsNullOrWhiteSpace(socket)) return Validate(socket, windows);
        var session = environment("HERDR_SESSION");
        if (!string.IsNullOrWhiteSpace(session)) return SessionPath(session, environment, folder, windows);
        return DefaultPath(environment, folder, windows);
    }

    private static string SessionPath(string session, Func<string, string?> environment,
        Func<Environment.SpecialFolder, string> folder, bool windows) =>
        windows
            ? Path.Combine(folder(Environment.SpecialFolder.ApplicationData), "herdr", "sessions", session, "herdr.sock")
            : session == "default"
                ? DefaultPath(environment, folder, windows)
                : Path.Combine(UnixConfigRoot(environment), "herdr", "sessions", session, "herdr.sock");

    private static string DefaultPath(Func<string, string?> environment,
        Func<Environment.SpecialFolder, string> folder, bool windows) =>
        windows
            ? Path.Combine(folder(Environment.SpecialFolder.ApplicationData), "herdr", "herdr.sock")
            : Path.Combine(UnixConfigRoot(environment), "herdr", "herdr.sock");

    private static string UnixConfigRoot(Func<string, string?> environment)
    {
        var xdg = environment("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdg) && Path.IsPathFullyQualified(xdg)) return xdg;
        var home = environment("HOME");
        if (!string.IsNullOrWhiteSpace(home) && Path.IsPathFullyQualified(home))
            return Path.Combine(home, ".config");
        var temp = Path.GetTempPath();
        if (Path.IsPathFullyQualified(temp)) return temp;
        throw new HerdrBackendUnavailableException("Herdr has no absolute Unix configuration directory.");
    }

    private static string Validate(string path, bool windows)
    {
        if (!windows && !Path.IsPathFullyQualified(path))
            throw new HerdrBackendUnavailableException($"Herdr Unix socket endpoint must be absolute: '{path}'.");
        return path;
    }
}
