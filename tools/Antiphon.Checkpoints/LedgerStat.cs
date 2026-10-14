using System.Runtime.InteropServices;

namespace Antiphon.Checkpoints;

// CARD-1241: file metadata read without opening the file. Linux reads statx (type, device, inode, size,
// mtime, ctime; link count is not part of it); other platforms read type, size and the last write time
// only, with no file identity and no change time. A failed read is Unknown, which matches nothing.
// Admission skips an entry's hash only for an equal stamp that HasIdentityAndChangeTime: a writer can put
// size and mtime back (utimes, touch -r, SetLastWriteTime), and those setters move the ctime
// (Review 157c4013 D1: portable_stat_rewrite_keeping_size_and_mtime_during_the_slot_wait_is_refused_before_launch,
// rewrite_keeping_size_and_mtime_on_the_real_file_system_is_refused_before_launch).
public readonly record struct LedgerStat(string Kind, ulong Device, ulong Inode, long Size, long Modified, long Changed)
{
    private const uint Mask = 0x1 | 0x2 | 0x40 | 0x80 | 0x100 | 0x200; // TYPE MODE MTIME CTIME INO SIZE

    public static LedgerStat Unknown => new("unknown", 0, 0, 0, 0, 0);

    private static LedgerStat Absent => new("absent", 0, 0, 0, 0, 0);

    public bool Matches(LedgerStat other) => Kind != "unknown" && this == other;

    public bool HasIdentityAndChangeTime => Inode != 0 && Changed != 0;

    // Nanoseconds since the Unix epoch, the unit of Modified and Changed.
    public static long Now() => (DateTime.UtcNow.Ticks - DateTime.UnixEpoch.Ticks) * 100;

    public static LedgerStat Of(string full)
    {
        if (!OperatingSystem.IsLinux())
            return Portable(full);
        try
        {
            return Linux(full);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return Portable(full);
        }
    }

    private static LedgerStat Linux(string full)
    {
        if (Statx(-100, full, 0x100, Mask, out var stat) != 0) // AT_FDCWD, AT_SYMLINK_NOFOLLOW
            return Marshal.GetLastPInvokeError() is 2 or 20 ? Absent : Unknown; // ENOENT, ENOTDIR
        if ((stat.Mask & Mask) != Mask)
            return Unknown;
        var kind = (stat.Mode & 0xF000) switch { 0x8000 => "file", 0x4000 => "directory", 0xA000 => "symlink", _ => "other" };
        return new LedgerStat(kind, ((ulong)stat.DevMajor << 32) | stat.DevMinor, stat.Inode, (long)stat.Size,
            stat.MtimeSec * 1_000_000_000 + stat.MtimeNsec, stat.CtimeSec * 1_000_000_000 + stat.CtimeNsec);
    }

    public static LedgerStat Portable(string full)
    {
        try
        {
            var file = new FileInfo(full);
            var kind = file.LinkTarget is not null ? "symlink" : file.Exists ? "file" : Directory.Exists(full) ? "directory" : "absent";
            if (kind == "absent")
                return Absent;
            var written = kind == "directory" ? Directory.GetLastWriteTimeUtc(full) : file.LastWriteTimeUtc;
            return new LedgerStat(kind, 0, 0, kind == "file" ? file.Length : 0, (written.Ticks - DateTime.UnixEpoch.Ticks) * 100, 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Unknown;
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxStatx
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(40)] public ulong Size;
        [FieldOffset(96)] public long CtimeSec;
        [FieldOffset(104)] public uint CtimeNsec;
        [FieldOffset(112)] public long MtimeSec;
        [FieldOffset(120)] public uint MtimeNsec;
        [FieldOffset(136)] public uint DevMajor;
        [FieldOffset(140)] public uint DevMinor;
    }

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(int fd, string path, int flags, uint mask, out LinuxStatx stat);
}
