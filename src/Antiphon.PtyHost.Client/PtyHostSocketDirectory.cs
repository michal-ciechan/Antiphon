namespace Antiphon.PtyHost.Client;

/// <summary>What a no-follow stat says about one path.</summary>
public enum UnixFileKind { Directory, Socket, Symlink, Other }

/// <summary>The owner and mode a no-follow stat reported. <see cref="Mode"/> carries the type bits.</summary>
public sealed record UnixFileFacts(UnixFileKind Kind, uint Uid, uint Mode);

/// <summary>The two kernel facts the socket directory and socket file checks read.</summary>
public interface IUnixFileProbe
{
    /// <summary>Facts for <paramref name="path"/> itself (a symlink is not followed), or null when it is absent.</summary>
    UnixFileFacts? Lstat(string path);

    uint Euid();
}

/// <summary>The pre-connect verdict on a session's socket file.</summary>
public enum SocketFileVerdict { Absent, Ok, Refused }

/// <summary>CARD-1653 S2: the owned socket directory cannot be proven safe. The message is the fixed code only.</summary>
public sealed class PtyHostSocketDirUnsafeException(string code) : InvalidOperationException(code)
{
    public const string UnsafeCode = "pty_host_socket_dir_unsafe";
    public const string TooLongCode = "pty_host_socket_path_too_long";

    public string Code { get; } = code;
}

/// <summary>
/// CARD-1653 S2: the owned rendezvous directory for new Linux sockets and the pre-connect check
/// of a session's socket file.
/// </summary>
public static class PtyHostSocketDirectory
{
    /// <summary>sun_path holds 108 bytes including the terminating NUL.</summary>
    public const int MaxSocketPathBytes = 107;

    private const int SessionNameBytes = 32;
    private const uint PermissionBits = 0b111_111_111;
    private const uint OwnerOnly = 0b111_000_000;
    private const uint GroupOrOtherWrite = 0b000_010_010;
    private const uint Sticky = 0b1_000_000_000;

    public static IUnixFileProbe DefaultProbe { get; } = new LinuxUnixFileProbe();

    /// <summary>
    /// Creates <paramref name="path"/> owner-only when absent, tightens an owned wider mode, and
    /// throws <see cref="PtyHostSocketDirUnsafeException"/> unless the directory is owned by the
    /// euid, exactly 0700, not a symlink, under ancestors owned by the euid or root that are not
    /// group- or other-writable without the sticky bit, and short enough for a session socket.
    /// </summary>
    public static void EnsureSafe(string path, IUnixFileProbe probe)
    {
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The owned socket directory is Unix-only.");
        path = Path.TrimEndingDirectorySeparator(path);
        if (System.Text.Encoding.UTF8.GetByteCount(path) + 1 + SessionNameBytes > MaxSocketPathBytes)
            throw new PtyHostSocketDirUnsafeException(PtyHostSocketDirUnsafeException.TooLongCode);
        var euid = probe.Euid();
        try
        {
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw Unsafe();
        }

        var facts = probe.Lstat(path);
        if (facts is not { Kind: UnixFileKind.Directory } || facts.Uid != euid)
            throw Unsafe();
        if ((facts.Mode & PermissionBits) != OwnerOnly)
        {
            try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw Unsafe(); }
            facts = probe.Lstat(path);
            if (facts is not { Kind: UnixFileKind.Directory } || facts.Uid != euid
                || (facts.Mode & PermissionBits) != OwnerOnly)
                throw Unsafe();
        }

        // An owner of any ancestor could rename the directory after this check (D-S2-5).
        for (var ancestor = Path.GetDirectoryName(path); !string.IsNullOrEmpty(ancestor);
             ancestor = Path.GetDirectoryName(ancestor))
        {
            var checkedPath = ancestor;
            var ancestorFacts = probe.Lstat(ancestor);
            if (ancestorFacts is { Kind: UnixFileKind.Symlink })
            {
                string? target;
                try { target = Directory.ResolveLinkTarget(ancestor, returnFinalTarget: true)?.FullName; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw Unsafe(); }
                if (target is null) throw Unsafe();
                checkedPath = Path.TrimEndingDirectorySeparator(target);
                ancestorFacts = probe.Lstat(checkedPath);
            }
            if (ancestorFacts is not { Kind: UnixFileKind.Directory }
                || (ancestorFacts.Uid != euid && ancestorFacts.Uid != 0)
                || ((ancestorFacts.Mode & GroupOrOtherWrite) != 0 && (ancestorFacts.Mode & Sticky) == 0))
                throw Unsafe();
        }
    }

    /// <summary>Absent keeps retrying; only a socket owned by the euid is Ok; anything else is Refused.</summary>
    public static SocketFileVerdict CheckSocketFile(string path, IUnixFileProbe probe)
    {
        var facts = probe.Lstat(path);
        if (facts is null) return SocketFileVerdict.Absent;
        return facts.Kind == UnixFileKind.Socket && facts.Uid == probe.Euid()
            ? SocketFileVerdict.Ok : SocketFileVerdict.Refused;
    }

    private static PtyHostSocketDirUnsafeException Unsafe() => new(PtyHostSocketDirUnsafeException.UnsafeCode);

    /// <summary>
    /// <c>statx(AT_FDCWD, path, AT_SYMLINK_NOFOLLOW)</c> and <c>geteuid</c>, as in CheckpointNativeIo.
    /// An absent path is null; any other failure is <see cref="UnixFileKind.Other"/> with no owner,
    /// so it is never accepted.
    /// </summary>
    private sealed class LinuxUnixFileProbe : IUnixFileProbe
    {
        private const int AtFdCwd = -100;
        private const int AtSymlinkNoFollow = 0x100;
        private const uint TypeModeUid = 0x1 | 0x2 | 0x8;
        private const int NoEntry = 2;
        private const int NotDirectory = 20;

        public UnixFileFacts? Lstat(string path)
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("statx is Linux-only.");
            if (Statx(AtFdCwd, path, AtSymlinkNoFollow, TypeModeUid, out var stat) != 0)
            {
                var errno = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
                return errno is NoEntry or NotDirectory ? null : new UnixFileFacts(UnixFileKind.Other, uint.MaxValue, 0);
            }
            if ((stat.Mask & TypeModeUid) != TypeModeUid)
                return new UnixFileFacts(UnixFileKind.Other, uint.MaxValue, 0);
            var kind = (stat.Mode & 0xF000) switch
            {
                0x4000 => UnixFileKind.Directory,
                0xC000 => UnixFileKind.Socket,
                0xA000 => UnixFileKind.Symlink,
                _ => UnixFileKind.Other,
            };
            return new UnixFileFacts(kind, stat.Uid, stat.Mode);
        }

        public uint Euid() => OperatingSystem.IsLinux() ? GetEuid()
            : throw new PlatformNotSupportedException("geteuid is read on Linux only.");

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = 256)]
        private struct LinuxStatx
        {
            [System.Runtime.InteropServices.FieldOffset(0)] public uint Mask;
            [System.Runtime.InteropServices.FieldOffset(20)] public uint Uid;
            [System.Runtime.InteropServices.FieldOffset(28)] public ushort Mode;
        }

        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "statx", SetLastError = true)]
        private static extern int Statx(int fd, string path, int flags, uint mask, out LinuxStatx stat);

        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "geteuid")]
        private static extern uint GetEuid();
    }
}
