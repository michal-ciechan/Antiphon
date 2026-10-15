using Antiphon.HostCleanup.Checkpoints;

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

    public static uint Owner => CheckpointCleanupDefaults.Context.Io.Owner;

    public static string? Refusal(Entry entry, Window window)
    {
        if (!entry.IsRegular && !entry.IsDirectory) return "not-regular";
        if (entry.IsRegular && entry.Links != 1) return "multiple-links";
        if (entry.Owner != window.Owner) return "foreign-owner";
        if (entry.Changed < window.RootCreated) return "changed-before-root";
        if (entry.Changed > window.BuildExited) return "changed-after-build";
        if (entry.Modified > window.BuildExited) return "modified-after-build";
        return null;
    }

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
        if (context.Io.Platform != CheckpointPlatform.Linux) return null;
        var stat = context.Io.Stat(path);
        if (stat.Kind is "unknown" or "absent" || stat.Inode == 0 || stat.Changed == 0) return null;
        return new Entry(stat.Mode, stat.Links, stat.Owner, stat.Inode, (ulong)stat.Size,
            DateTimeOffset.UnixEpoch.AddTicks(stat.Changed / 100), DateTimeOffset.UnixEpoch.AddTicks(stat.Modified / 100));
    }
}
