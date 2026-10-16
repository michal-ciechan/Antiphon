using Antiphon.HostCleanup.Checkpoints;

namespace Antiphon.Checkpoints;

// CARD-1241: file metadata read without opening the file. Linux reads statx (type, device, inode, size,
// mtime, ctime; link count is not part of it); other platforms read type, size and the last write time
// only, with no file identity and no change time. A failed read is Unknown, which matches nothing.
// Admission skips an entry's hash only for an equal stamp that HasIdentityAndChangeTime: a writer can put
// size and mtime back (utimes, touch -r, SetLastWriteTime), and those setters move the ctime
// (Review 157c4013 D1: portable_stat_rewrite_keeping_size_and_mtime_during_the_slot_wait_is_refused_before_launch,
// rewrite_keeping_size_and_mtime_on_the_real_file_system_is_refused_before_launch).
// The stamp is not a content version: a store through a shared memory mapping can leave it equal (Review 9f1c8a2e
// D1, shared_mapping_style_change_with_an_unchanged_stat_is_refused_at_admission_only_in_strict_mode); see AdmitLedger.
public readonly record struct LedgerStat(string Kind, ulong Device, ulong Inode, long Size, long Modified, long Changed)
{

    public static LedgerStat Unknown => new("unknown", 0, 0, 0, 0, 0);


    public bool Matches(LedgerStat other) => Kind != "unknown" && this == other;

    public bool HasIdentityAndChangeTime => Inode != 0 && Changed != 0;

    // Nanoseconds since the Unix epoch, the unit of Modified and Changed.
    public static long Now() => Now(CheckpointCleanupDefaults.Context);
    public static long Now(CheckpointCleanupContext context) => (context.Time.GetUtcNow().UtcTicks - DateTime.UnixEpoch.Ticks) * 100;

    public static LedgerStat Of(string full) => Of(CheckpointCleanupDefaults.Context, full);
    public static LedgerStat Of(CheckpointCleanupContext context, string full) => From(context.Io.Stat(full));
    public static LedgerStat Portable(string full) => From(CheckpointCleanupDefaults.Context.Io.Stat(full, portable: true));
    private static LedgerStat From(CheckpointStat stat) => new(stat.Kind, stat.Device, stat.Inode, stat.Size, stat.Modified, stat.Changed);
}
