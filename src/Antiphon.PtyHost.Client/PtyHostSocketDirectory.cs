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
    public static IUnixFileProbe DefaultProbe { get; } = new UnsupportedProbe();

    public static void EnsureSafe(string path, IUnixFileProbe probe) => throw new NotImplementedException();

    public static SocketFileVerdict CheckSocketFile(string path, IUnixFileProbe probe) =>
        throw new NotImplementedException();

    private sealed class UnsupportedProbe : IUnixFileProbe
    {
        public UnixFileFacts? Lstat(string path) => throw new NotImplementedException();
        public uint Euid() => throw new NotImplementedException();
    }
}
