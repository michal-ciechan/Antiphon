using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.SessionRunner;

/// <summary>Open only a regular file through directories that are not links.</summary>
internal sealed class AgentPinPosixReader
{
    // Linux ABI values. Callers must qualify the platform before entering this helper.
    private const int NoFollow = 0x20000;
    private const int CloseOnExec = 0x80000;
    private const int DirectoryOnly = 0x10000;
    private const int NonBlocking = 0x800;

    public FileStream Open(string canonicalPath)
    {
        var parent = OpenHandle(-100 /* AT_FDCWD */, "/", DirectoryOnly);
        try
        {
            var parts = canonicalPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            foreach (var directory in parts[..^1])
            {
                var child = OpenHandle(parent.DangerousGetHandle().ToInt32(), directory, DirectoryOnly);
                parent.Dispose();
                parent = child;
            }
            var file = OpenHandle(parent.DangerousGetHandle().ToInt32(), parts[^1], NonBlocking);
            try
            {
                // AT_EMPTY_PATH inspects this exact opened descriptor, not its current name.
                // Refuse FIFO/socket/device targets before any read: opening a FIFO without
                // O_NONBLOCK could hang even when it is merely an authored foreign file.
                if (Statx(file, "", 0x1000, 1 /* STATX_TYPE */, out var stat) != 0)
                    throw new IOException("Cannot inspect pin file type.");
                if ((stat.Mask & 1) == 0 || (stat.Mode & 0xf000) != 0x8000 /* S_IFREG */)
                    throw new AgentPinNonRegularFileException();
                return new FileStream(file, FileAccess.Read);
            }
            catch { file.Dispose(); throw; }
        }
        finally { parent.Dispose(); }
    }

    private static SafeFileHandle OpenHandle(int parent, string name, int flags)
    {
        var fd = OpenAt(parent, name, flags | NoFollow | CloseOnExec);
        if (fd < 0) throw new IOException("Cannot open native pin path.");
        return new SafeFileHandle((IntPtr)fd, ownsHandle: true);
    }

    // statx has a stable 256-byte Linux ABI on both x64 and arm64; only type is used.
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct StatxInfo
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(28)] public ushort Mode;
    }

    [DllImport("libc", EntryPoint = "openat", SetLastError = true)]
    private static extern int OpenAt(int parent, string path, int flags);

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(SafeFileHandle file, string path, int flags, uint mask, out StatxInfo result);
}

internal sealed class AgentPinNonRegularFileException : IOException;
