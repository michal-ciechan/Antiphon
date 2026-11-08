namespace Antiphon.Server.Application.Settings;

/// <summary>
/// CARD-1677 S3: the <c>WorkspaceDiskLow</c> attention row. Report only: the alarm never deletes
/// anything. <see cref="Runners"/> maps an allow-listed Linux runner id to the volume path its host
/// stats report for the worktree mount (<c>server2 -&gt; /work</c> in <c>appsettings.json</c>).
/// The desktop runner is never alarmed, listed or not (D-11).
/// </summary>
public sealed class WorkspaceDiskAlarmSettings
{
    public const string SectionName = "WorkspaceDiskAlarm";
    public const int MinUsedPercent = 50;
    public const int MaxUsedPercent = 99;
    public const int MaxSampleFreshMinutes = 24 * 60;
    public const int MaxMinFreeGiB = 100_000;

    public bool Enabled { get; set; } = true;

    /// <summary>The row raises at this used percentage or above (integer compare, S-7). 50..99.</summary>
    public int UsedPercent { get; set; } = 85;

    /// <summary>A sample the server first observed longer ago than this raises nothing.</summary>
    public int SampleFreshMinutes { get; set; } = 15;

    /// <summary>
    /// CARD-1794 (5). Free space below this many GiB raises the row as an Error, even under <see cref="UsedPercent"/>
    /// (server2 reached 1.8 GB free with no alert). 0 turns the floor off. 0..<see cref="MaxMinFreeGiB"/>.
    /// </summary>
    public int MinFreeGiB { get; set; } = 10;

    /// <summary>
    /// Runner id to volume path. Empty in code: configuration binding merges dictionary keys, so a
    /// code default could never be removed by an operator. <c>appsettings.json</c> lists server2.
    /// </summary>
    public Dictionary<string, string> Runners { get; set; } = new(StringComparer.Ordinal);
}
