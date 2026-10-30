using System.ComponentModel;
using System.Diagnostics;
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

    public PortaPtySession(IPtyConnection connection, Action<PortaPtySession>? beforeReplayForTest = null)
    {
        _connection = connection;
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

    public void Dispose() => _connection.Dispose();
}
