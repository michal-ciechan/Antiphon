using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Porta.Pty;

namespace Antiphon.Agents.Pty;

/// <summary>
/// The pty surface <see cref="PtyAgentRunner"/> actually uses — two streams, a pid, resize, kill, and
/// an exit signal.
///
/// <para>It exists because the two backends cannot share Porta's own <c>IPtyConnection</c>:
/// <c>PtyExitedEventArgs</c> has an <b>internal</b> constructor, so no assembly outside Porta.Pty can
/// raise its exit event. Rather than reflect our way around that, the runner talks to this
/// interface and Porta's connection is adapted into it — which also keeps a third-party type out of
/// the runner's own plumbing.</para>
/// </summary>
internal interface IPtySession : IDisposable
{
    Stream ReaderStream { get; }

    Stream WriterStream { get; }

    int Pid { get; }

    /// <summary>Raised at most once with actual native evidence.</summary>
    event Action<PtyExitObservation>? Exited;

    /// <summary>Read after subscribing to replay an exit that preceded that subscription.</summary>
    PtyExitObservation? ReadExitObservation();

    void Resize(int cols, int rows);

    void Kill();
}

/// <summary>Porta transport: Unix PTY, or the deprecated Windows kernel32/inbox path.</summary>
internal sealed class PortaPtySession : IPtySession
{
    private readonly IPtyConnection _connection;
    private readonly PortaExitSignalReader _signalReader = new();
    private PtyExitObservation? _exitObservation;
    private readonly PtyStopCallback? _stopSignal;
    private readonly DateTime? _ownedStartedAtUtc;

    public PortaPtySession(IPtyConnection connection, Action<PortaPtySession>? beforeReplayForTest = null, PtyStopCallback? stopSignal = null)
    {
        _connection = connection;
        _stopSignal = stopSignal;
        if (OperatingSystem.IsLinux())
        {
            try { using var child = Process.GetProcessById(connection.Pid); _ownedStartedAtUtc = child.StartTime.ToUniversalTime(); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { }
        }
        _connection.ProcessExited += (_, e) => PublishExit(e.ExitCode);
        beforeReplayForTest?.Invoke(this);
        // Porta sets code/signal, then this event, then calls ProcessExited. An early exit can
        // precede subscription, and callback/replay can overlap. Both share the same one-shot cache.
        if (_connection.WaitForExit(0)) PublishExit(_connection.ExitCode);
    }

    public event Action<PtyExitObservation>? Exited;

    public PtyExitObservation? ReadExitObservation() => Volatile.Read(ref _exitObservation);

    private void PublishExit(int exitCode)
    {
        var observation = new PtyExitObservation(exitCode, _signalReader.ReadCompleted(_connection));
        if (Interlocked.CompareExchange(ref _exitObservation, observation, null) is null)
            Exited?.Invoke(observation);
    }

    public Stream ReaderStream => _connection.ReaderStream;

    public Stream WriterStream => _connection.WriterStream;

    public int Pid => _connection.Pid;

    public void Resize(int cols, int rows) => _connection.Resize(cols, rows);

    public void Kill()
    {
        if (OperatingSystem.IsLinux())
        {
            if (ReadExitObservation() is not null) return;
            if (Attempt(9, "ProcessTree", () => {
                using var child = Process.GetProcessById(Pid);
                if (child.HasExited || _ownedStartedAtUtc is not { } start || child.StartTime.ToUniversalTime() != start) return false;
                child.Kill(entireProcessTree: true);
                return true;
            }) != true)
                Attempt(1, "PortaFallback", () => {
                    using var child = Process.GetProcessById(Pid);
                    if (child.HasExited || _ownedStartedAtUtc is not { } start || child.StartTime.ToUniversalTime() != start) return false;
                    return SendSignal(Pid, 1) != -1; // exactly Porta 1.0.7's native success contract
                });
            return;
        }
        // We do not own Porta's kill-on-close job handle, so tree-kill the pid we do have.
        // Production modern does not depend on this; tests and inbox fallback still do (CARD-0308).
        try
        {
            using var process = Process.GetProcessById(_connection.Pid);
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            try { _connection.Kill(); } catch { /* already gone */ }
        }
    }

    private bool? Attempt(int signal, string path, Func<bool?> send)
    {
        try { return _stopSignal is { } callback ? callback(Pid, signal, path, send) : send(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { return false; }
    }

    public void Dispose()
    {
        if (OperatingSystem.IsLinux() && ReadExitObservation() is null)
        {
            // Upstream closes master streams and swallows its SIGHUP syscall result. The void
            // return proves only an attempt; it must never become an issued receipt.
            Attempt(1, "Disposal", () => { _connection.Dispose(); return null; });
        }
        else _connection.Dispose();
    }

    [DllImport("libporta_pty", EntryPoint = "pty_kill", SetLastError = true)]
    private static extern int SendSignal(int pid, int signal);
}
