using Antiphon.HostCleanup.Checkpoints;

namespace Antiphon.Checkpoints;

// CARD-1242 F1: seal-time custody for entries a build child wrote under a fresh output root.
// Proven by OutputCleanupTests: the root is created owner-only by this process; an entry is adopted
// only when it is a regular file with one link or a directory, owned by this uid, with ctime inside
// [root creation, build exit as observed by the driver] and mtime not after that exit, and its
// statx is unchanged across the content read. A write by another process after the exit, a hard
// link, a foreign owner, a ctime before the root (except the Windows copy case below), or a seal without driver-observed exit adopts
// nothing; the entry stays unrecorded and cleanup refuses it. Boundary, not closed: a same-uid
// writer inside the build window (the root is 0700, so it shares the build's trust domain) and a
// write within one kernel timestamp tick after the observed exit are indistinguishable from the
// build. Windows has no statx. CustodyStat reads the opened handle after the observed exit and
// adopts a regular file or directory with one link that is not a reparse, owned by this process,
// with NTFS change time inside that window and mtime not after the exit. CopyFile keeps the
// source change time, so an entry whose change time predates the root is adopted only when its
// NTFS creation time lies inside the window and its change time is not after the exit. Stamping
// creation or last-write time moves change time to now, past an already observed exit, so that
// stamp-back stays refused. Windows change time is settable by the same account, the same trust
// boundary as a same-uid writer inside the window. The fresh root is created with an owner-only
// DACL; a DACL that grants another SID sets the low mode bits, so For returns no window.
// CreateFile error 32 (sharing violation) is retried for a short bound and then stays unknown,
// so cleanup still refuses. A second link or a reparse stays unrecorded. Other platforms adopt
// nothing. The build ledger still uses Stat, which on Windows has no inode and no change time.
public static class OutputCustody
{
    public readonly record struct Entry(ushort Mode, uint Links, uint Owner, ulong Inode, ulong Size, DateTimeOffset Changed, DateTimeOffset Modified, DateTimeOffset Created = default)
    {
        public bool IsRegular => (Mode & 0xF000) == 0x8000;
        public bool IsDirectory => (Mode & 0xF000) == 0x4000;
    }

    public sealed record Window(DateTimeOffset RootCreated, DateTimeOffset BuildExited, uint Owner);

    public static uint Owner => CheckpointCleanupDefaults.Context.Io.Owner;

    public static string? Refusal(Entry entry, Window window)
    {
        if (!entry.IsRegular && !entry.IsDirectory) return "not-regular";
        if (entry.IsRegular && entry.Links != 1) return "multiple-links";
        if (entry.Owner != window.Owner) return "foreign-owner";
        if (entry.Changed < window.RootCreated && !CopiedInsideWindow(entry, window)) return "changed-before-root";
        if (entry.Changed > window.BuildExited) return "changed-after-build";
        if (entry.Modified > window.BuildExited) return "modified-after-build";
        return null;
    }

    // CopyFile keeps the source change time and stamps creation at the copy. Admit that only when
    // creation is inside the window. Change time still has to be no later than the exit: stamping
    // creation or last-write moves it to now.
    private static bool CopiedInsideWindow(Entry entry, Window window) =>
        OperatingSystem.IsWindows()
        && entry.Created >= window.RootCreated
        && entry.Created <= window.BuildExited
        && entry.Changed <= window.BuildExited;

    // Fail closed: no driver-observed exit, a root this process did not create owner-only, or a
    // root whose owner/mode changed since gives no window, so Seal adopts nothing.
    public static Window? For(string root, DateTimeOffset? buildExited) => For(CheckpointCleanupDefaults.Context, root, buildExited);

    public static Window? For(CheckpointCleanupContext context, string root, DateTimeOffset? buildExited)
    {
        if (buildExited is not { } exited || context.Registry.FreshRoot(root) is not { } created || exited < created) return null;
        return Stat(context, root) is { IsDirectory: true } dir && dir.Owner == context.Io.Owner && (dir.Mode & 0x3F) == 0
            ? new Window(created, exited, context.Io.Owner) : null;
    }

    public static Entry? Stat(string path) => Stat(CheckpointCleanupDefaults.Context, path);

    public static Entry? Stat(CheckpointCleanupContext context, string path)
    {
        if (context.Io.Platform == CheckpointPlatform.Other) return null;
        var stat = context.Io.CustodyStat(path);
        if (stat.Kind is "unknown" or "absent" || stat.Inode == 0 || stat.Changed == 0) return null;
        var created = stat.Created == 0 ? default : DateTimeOffset.UnixEpoch.AddTicks(stat.Created / 100);
        return new Entry(stat.Mode, stat.Links, stat.Owner, stat.Inode, (ulong)stat.Size,
            DateTimeOffset.UnixEpoch.AddTicks(stat.Changed / 100), DateTimeOffset.UnixEpoch.AddTicks(stat.Modified / 100), created);
    }
}
