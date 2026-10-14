using System.Runtime.InteropServices;

namespace Antiphon.Checkpoints;

// CARD-1242 F1: seal-time custody for entries a build child wrote under a fresh output root.
// Proven by OutputCleanupTests: the root is created owner-only by this process; an entry is adopted
// only when it is a regular file with one link or a directory, owned by this uid, with ctime inside
// [root creation, build exit as observed by the driver] and mtime not after that exit, and its
// statx is unchanged across the content read. A write by another process after the exit, a hard
// link, a foreign owner, a ctime before the root, or a seal without driver-observed exit adopts
// nothing; the entry stays unrecorded and cleanup refuses it. Boundary, not closed: a same-uid
// writer inside the build window (the root is 0700, so it shares the build's trust domain) and a
// write within one kernel timestamp tick after the observed exit are indistinguishable from the
// build. Off Linux there is no statx, so nothing is adopted.
public static class OutputCustody
{
    public readonly record struct Entry(ushort Mode, uint Links, uint Owner, ulong Inode, ulong Size, DateTimeOffset Changed, DateTimeOffset Modified)
    {
        public bool IsRegular => (Mode & 0xF000) == 0x8000;
        public bool IsDirectory => (Mode & 0xF000) == 0x4000;
    }

    public sealed record Window(DateTimeOffset RootCreated, DateTimeOffset BuildExited, uint Owner);

    public static uint Owner => OperatingSystem.IsLinux() ? GetEuid() : uint.MaxValue;

    // RED-FIRST scaffold (replaced in the next commit): no custody, the 2b293e51f behaviour.
    public static string? Refusal(Entry entry, Window window) => null;

    public static Window? For(string root, DateTimeOffset? buildExited) =>
        new(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, Owner);

    public static Entry? Stat(string path)
    {
        if (!OperatingSystem.IsLinux()) return null;
        // AT_SYMLINK_NOFOLLOW; TYPE|MODE|NLINK|UID|MTIME|CTIME|INO|SIZE.
        const uint mask = 0x3CF;
        if (Statx(-100, path, 0x100, mask, out var stat) != 0 || (stat.Mask & mask) != mask) return null;
        return new Entry(stat.Mode, stat.Links, stat.Uid, stat.Inode, stat.Size, Time(stat.CtimeSeconds, stat.CtimeNanoseconds), Time(stat.MtimeSeconds, stat.MtimeNanoseconds));
    }

    private static DateTimeOffset Time(long seconds, uint nanoseconds) =>
        DateTimeOffset.FromUnixTimeSeconds(seconds).AddTicks(nanoseconds / 100);

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxStatx
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(16)] public uint Links;
        [FieldOffset(20)] public uint Uid;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(40)] public ulong Size;
        [FieldOffset(96)] public long CtimeSeconds;
        [FieldOffset(104)] public uint CtimeNanoseconds;
        [FieldOffset(112)] public long MtimeSeconds;
        [FieldOffset(120)] public uint MtimeNanoseconds;
    }
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)] private static extern int Statx(int fd, string path, int flags, uint mask, out LinuxStatx stat);
    [DllImport("libc", EntryPoint = "geteuid")] private static extern uint GetEuid();
}
