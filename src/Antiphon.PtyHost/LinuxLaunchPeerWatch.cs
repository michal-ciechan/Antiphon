using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.PtyHost;

/// <summary>Waits for Linux pipe hangup without reading or peeking at a request byte.</summary>
internal sealed class LinuxLaunchPeerWatch : IAsyncDisposable
{
    private const short PollIn = 0x0001, PollError = 0x0008, PollHangup = 0x0010,
        PollInvalid = 0x0020, PollReadHangup = 0x2000;
    private const int CloseOnExec = 0x80000, Interrupted = 4;
    private readonly SafeFileHandle _wake;
    private readonly CancellationTokenSource _lifetime;
    private readonly Task _watch;

    public LinuxLaunchPeerWatch(NamedPipeServerStream pipe, CancellationToken ct)
    {
        var fd = EventFd(0, CloseOnExec);
        if (fd < 0)
            throw new IOException("verification_custody_pipe_watch_unavailable");
        _wake = new SafeFileHandle((nint)fd, ownsHandle: true);
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // The server owns the pipe until this request-scoped watch has joined.
        var pipeFd = pipe.SafePipeHandle.DangerousGetHandle().ToInt32();
        _watch = Task.Factory.StartNew(() => Watch(pipeFd, fd), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public CancellationToken Token => _lifetime.Token;

    private void Watch(int pipeFd, int wakeFd)
    {
        PollFd[] items = [new() { Fd = pipeFd, Events = PollReadHangup },
            new() { Fd = wakeFd, Events = PollIn }];
        int result;
        do { result = Poll(items, (nuint)items.Length, -1); }
        while (result < 0 && Marshal.GetLastPInvokeError() == Interrupted);

        if (result < 0 || (items[0].Returned & (PollReadHangup | PollHangup | PollError | PollInvalid)) != 0)
            _lifetime.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        ulong wake = 1;
        while (Write(_wake, ref wake, sizeof(ulong)) < 0 && Marshal.GetLastPInvokeError() == Interrupted) { }
        try { await _watch; }
        finally
        {
            _wake.Dispose();
            _lifetime.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short Returned;
    }

    [DllImport("libc", EntryPoint = "eventfd", SetLastError = true)]
    private static extern int EventFd(uint initialValue, int flags);

    [DllImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static extern int Poll([In, Out] PollFd[] fds, nuint count, int timeout);

    [DllImport("libc", EntryPoint = "write", SetLastError = true)]
    private static extern nint Write(SafeFileHandle fd, ref ulong value, nuint length);
}
