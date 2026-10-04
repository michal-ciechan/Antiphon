using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.SessionRunner;

/// <summary>Linux x64 only (admitted by the pin store). All leaf I/O is relative
/// to a no-follow directory descriptor. Files and rename/unlink metadata are flushed.</summary>
internal sealed class AgentPinPosixDirectory(SafeFileHandle handle) : IDisposable
{
    private const int NoFollow = 0x20000;
    private const int CloseOnExec = 0x80000;
    private const int DirectoryOnly = 0x10000;
    private int Fd => handle.DangerousGetHandle().ToInt32();

    public static AgentPinPosixDirectory Open(string path)
    {
        var current = new AgentPinPosixDirectory(OpenFile(-100, "/", DirectoryOnly));
        try
        {
            foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                var next = current.Child(part, false);
                current.Dispose();
                current = next;
            }
            return current;
        }
        catch { current.Dispose(); throw; }
    }

    public AgentPinPosixDirectory Child(string name, bool create)
    {
        Leaf(name);
        if (create)
        {
            if (MkdirAt(Fd, name, 0x1c0 /* 0700 */) != 0 && Marshal.GetLastPInvokeError() != 17 /* EEXIST */)
                throw new IOException("Cannot create pin directory.");
            Flush();
        }
        return new(OpenFile(Fd, name, DirectoryOnly));
    }

    public FileStream Acquire(string name)
    {
        Leaf(name);
        var file = OpenFile(Fd, name, 2 | 0x40 | 0x800 /* RDWR|CREAT|NONBLOCK */);
        try
        {
            RequireRegular(file);
            if (Flock(file, 2 | 4 /* LOCK_EX|LOCK_NB */) != 0)
                throw new IOException("Pin path is busy.");
            return new FileStream(file, FileAccess.ReadWrite);
        }
        catch { file.Dispose(); throw; }
    }

    public byte[]? Read(string name, int limit)
    {
        Leaf(name);
        var fd = OpenAt(Fd, name, NoFollow | CloseOnExec | 0x800, 0);
        if (fd < 0)
        {
            if (Marshal.GetLastPInvokeError() == 2 /* ENOENT */) return null;
            throw new IOException("Cannot open pin journal.");
        }
        using var file = new SafeFileHandle((IntPtr)fd, true);
        RequireRegular(file);
        using var stream = new FileStream(file, FileAccess.Read);
        if (stream.Length > limit) throw new IOException("Pin journal exceeds bound.");
        using var bytes = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = stream.Read(buffer)) > 0)
        {
            if (bytes.Length + count > limit) throw new IOException("Pin journal exceeds bound.");
            bytes.Write(buffer, 0, count);
        }
        return bytes.ToArray();
    }

    public void WriteNew(string name, byte[] bytes)
    {
        Leaf(name);
        using var file = OpenFile(Fd, name, 1 | 0x40 | 0x80 /* WRONLY|CREAT|EXCL */);
        using var stream = new FileStream(file, FileAccess.Write);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    public void Replace(string temp, string name)
    {
        Leaf(temp); Leaf(name);
        if (RenameAt(Fd, temp, Fd, name) != 0) throw new IOException("Cannot replace pin file.");
        Flush();
    }

    public void Delete(string name)
    {
        Leaf(name);
        if (UnlinkAt(Fd, name, 0) != 0 && Marshal.GetLastPInvokeError() != 2)
            throw new IOException("Cannot remove pin file.");
        Flush();
    }

    public void RemoveEmptyDirectory(string name)
    {
        Leaf(name);
        if (UnlinkAt(Fd, name, 0x200 /* AT_REMOVEDIR */) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error is not (2 or 39 /* ENOENT, ENOTEMPTY */))
                throw new IOException("Cannot remove empty pin leaf.");
        }
        Flush();
    }

    private void Flush()
    {
        if (Fsync(handle) != 0) throw new IOException("Cannot flush pin directory.");
    }

    private static SafeFileHandle OpenFile(int parent, string name, int flags)
    {
        var fd = OpenAt(parent, name, flags | NoFollow | CloseOnExec, 0x180 /* 0600 */);
        if (fd < 0) throw new IOException("Cannot open native pin path.");
        return new SafeFileHandle((IntPtr)fd, true);
    }

    private static void RequireRegular(SafeFileHandle file)
    {
        if (Statx(file, "", 0x1000, 1, out var stat) != 0
            || (stat.Mask & 1) == 0 || (stat.Mode & 0xf000) != 0x8000)
            throw new IOException("Pin state must be a regular file.");
    }

    private static void Leaf(string name)
    {
        if (name is "" or "." or ".." || name.Contains('/') || name.Contains('\0'))
            throw new ArgumentException("Invalid native leaf.");
    }

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct StatxInfo
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(28)] public ushort Mode;
    }

    [DllImport("libc", EntryPoint = "openat", SetLastError = true)]
    private static extern int OpenAt(int parent, string name, int flags, uint mode);
    [DllImport("libc", EntryPoint = "mkdirat", SetLastError = true)]
    private static extern int MkdirAt(int parent, string name, uint mode);
    [DllImport("libc", EntryPoint = "renameat", SetLastError = true)]
    private static extern int RenameAt(int source, string oldName, int target, string newName);
    [DllImport("libc", EntryPoint = "unlinkat", SetLastError = true)]
    private static extern int UnlinkAt(int parent, string name, int flags);
    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int Fsync(SafeFileHandle file);
    [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static extern int Flock(SafeFileHandle file, int operation);
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(SafeFileHandle file, string name, int flags, uint mask, out StatxInfo result);

    public void Dispose() => handle.Dispose();
}
