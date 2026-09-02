namespace Antiphon.Server.Application.Settings;

/// <summary>
/// CARD-0004: how card rows are rendered into <c>docs/cards/&lt;board-slug&gt;/</c> and when those
/// files are committed. Bound from the <c>CardFileSync</c> configuration section.
/// </summary>
public sealed class CardFileSyncSettings
{
    public const string SectionName = "CardFileSync";

    /// <summary>
    /// Off means the feature does not exist: no tick, and the endpoint answers 409
    /// <c>card_file_sync_disabled</c>. Rendering and writing are safe and reviewable, so this
    /// defaults on.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Off writes files and never commits (<c>CommitSkipReason</c> <c>autocommit_disabled</c>).
    /// Defaults to <c>false</c>, not the plan's original <c>true</c>: auto-committing ~315 files
    /// onto master unreviewed on the first AppHost restart after S3 is not acceptable. A human
    /// runs a manual <c>dryRun</c> sync, reviews it, then a real manual sync, before this is
    /// ever turned on. Do not flip this default back to <c>true</c> in S2/S3.
    /// </summary>
    public bool AutoCommit { get; set; } = false;

    /// <summary>
    /// Tick cadence in seconds, floor 5. <c>0</c> disables the tick and leaves the endpoint on
    /// (manual-only mode). The hosted tick itself lands in S3.
    /// </summary>
    public int IntervalSeconds { get; set; } = 60;
}
