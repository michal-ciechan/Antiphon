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

    /// <summary>Raised once with the child's exit code.</summary>
    event Action<int>? Exited;

    void Resize(int cols, int rows);

    void Kill();
}

/// <summary>The default backend: Porta.Pty → kernel32 → the inbox <c>conhost.exe</c>.</summary>
internal sealed class PortaPtySession : IPtySession
{
    private readonly IPtyConnection _connection;

    public PortaPtySession(IPtyConnection connection)
    {
        _connection = connection;
        _connection.ProcessExited += (_, e) => Exited?.Invoke(e.ExitCode);
    }

    public event Action<int>? Exited;

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
