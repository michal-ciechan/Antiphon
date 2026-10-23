using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.HostCleanup.Diagnostics;

// Independent writer: never calls filesystem hooks which can themselves trigger a capture.
public sealed class FileUseEvidenceWriter : IFileUseEvidenceWriter
{
    private static JsonSerializerOptions Options() => new() { Converters = { new JsonStringEnumConverter() } };
    private static byte[] Serialize(FileUseSnapshot snapshot) => JsonSerializer.SerializeToUtf8Bytes(snapshot, Options());
    public static FileUseSnapshot Bound(FileUseSnapshot snapshot)
    {
        // Keep the envelope and first holder, dropping optional ancestry then extra holders.
        var owners = snapshot.Owners.ToArray(); var omitted = snapshot.Omitted;
        while (Serialize(snapshot with { Owners = owners }).Length > 30000)
        {
            if (owners.Any(o => o.Ancestors.Count > 0))
            { omitted += owners.Sum(o => o.Ancestors.Count); owners = owners.Select(o => o with { Ancestors = [] }).ToArray(); }
            else if (owners.Length > 1) { owners = owners[..^1]; omitted++; }
            else if (owners.Length == 1 && (owners[0].CommandLine?.Length > 256 || owners[0].Resource.Length > 256))
            { owners[0] = owners[0] with { CommandLine = "[redacted: size limit]", Resource = "[clipped: see path identity]" }; omitted++; }
            else break;
            snapshot = snapshot with { Omitted = omitted, Reasons = snapshot.Reasons.Append(FileUseReason.TextLimit).Distinct().ToArray() };
        }
        var result = snapshot with { Owners = owners, Omitted = omitted };
        if (Serialize(result).Length > 32768) throw new IOException("evidence envelope exceeds bound");
        return result;
    }

    public string? Write(FileUseSnapshot snapshot, string root, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux()) return null; // Windows no-follow/ACL writer is S2 debt.
        cancellationToken.ThrowIfCancellationRequested();
        var payload = Serialize(Bound(snapshot)); if (payload.Length > 32768) throw new IOException("evidence exceeds bound");
        using var directory = OpenRoot(root);
        var filename = $"file-use-{snapshot.ObservationUtc:yyyyMMddTHHmmssfffffffZ}-{snapshot.CaptureId}.json";
        var temporary = "." + filename + ".tmp";
        var descriptor = directory.DangerousGetHandle().ToInt32(); var published = false;
        try
        {
            // O_WRONLY|O_CREAT|O_EXCL|O_NOFOLLOW|O_CLOEXEC, mode 0600.
            var fd = OpenAt(descriptor, temporary, 1 | 0x40 | 0x80 | 0x20000 | 0x80000, 0x180);
            if (fd < 0) throw new IOException("evidence open refused");
            using (var handle = new SafeFileHandle((IntPtr)fd, true))
            using (var stream = new FileStream(handle, FileAccess.Write))
            {
                stream.Write(payload); stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // RENAME_NOREPLACE never overwrites an existing capture; all names are relative to the opened root.
            if (RenameAt2(descriptor, temporary, descriptor, filename, 1) != 0) throw new IOException("evidence publication refused");
            published = true;
            return Path.Combine(root, filename);
        }
        finally { if (!published) UnlinkAt(descriptor, temporary, 0); }
    }

    private static SafeFileHandle OpenRoot(string root)
    {
        if (!Path.IsPathFullyQualified(root)) throw new IOException("absolute evidence root required");
        // Walk with openat/O_NOFOLLOW, so a linked parent or a racing path replacement cannot redirect writes.
        var fd = OpenAt(-100, "/", 0x10000 | 0x20000 | 0x80000, 0);
        if (fd < 0) throw new IOException("root unavailable");
        var current = new SafeFileHandle((IntPtr)fd, true);
        try
        {
            foreach (var component in Path.GetFullPath(root).Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                var parent = current.DangerousGetHandle().ToInt32();
                var next = OpenAt(parent, component, 0x10000 | 0x20000 | 0x80000, 0);
                if (next < 0 && Marshal.GetLastPInvokeError() == 2)
                {
                    if (MakeDirectoryAt(parent, component, 0x1C0) != 0 && Marshal.GetLastPInvokeError() != 17) throw new IOException("evidence directory unavailable");
                    next = OpenAt(parent, component, 0x10000 | 0x20000 | 0x80000, 0);
                }
                if (next < 0) throw new IOException("linked or unavailable evidence directory");
                current.Dispose(); current = new SafeFileHandle((IntPtr)next, true);
            }
            // Check the opened directory, not the original pathname.
            var mode = File.GetUnixFileMode($"/proc/self/fd/{current.DangerousGetHandle().ToInt32()}");
            if ((mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
                throw new IOException("evidence root must be owner-only");
            return current;
        }
        catch { current.Dispose(); throw; }
    }
    [DllImport("libc", EntryPoint = "openat", SetLastError = true)] private static extern int OpenAt(int fd, string path, int flags, uint mode);
    [DllImport("libc", EntryPoint = "mkdirat", SetLastError = true)] private static extern int MakeDirectoryAt(int fd, string path, uint mode);
    [DllImport("libc", EntryPoint = "renameat2", SetLastError = true)] private static extern int RenameAt2(int fromFd, string from, int toFd, string to, uint flags);
    [DllImport("libc", EntryPoint = "unlinkat", SetLastError = true)] private static extern int UnlinkAt(int fd, string path, int flags);
}
