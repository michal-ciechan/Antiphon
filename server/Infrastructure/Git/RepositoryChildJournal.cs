using System.Diagnostics;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>Standing admission fence that survives the server's lease handle closing on death.</summary>
internal sealed class RepositoryChildJournal
{
    // A worker-scoped crash barrier used by repository recovery tests. It observes the fully
    // flushed temporary before the only replacement; normal server processes leave it null.
    internal static Func<string, Task>? BeforeReplaceForTests { get; set; }
    private readonly string _path;
    private ChildRecord _record;

    private RepositoryChildJournal(string path, ChildRecord record) { _path = path; _record = record; }

    public static Task<RepositoryChildJournal> BeginAsync(string repository, CancellationToken ct)
        => BeginAsync(repository, tag: null, ct);

    /// <summary>CARD-1076: <paramref name="tag"/> is stored on the record. A null tag is the untagged journal.</summary>
    internal static async Task<RepositoryChildJournal> BeginAsync(string repository, RepositoryChildTag? tag, CancellationToken ct)
    {
        var common = await new LandingGit().CommonDirectoryAsync(repository, ct);
        var directory = Path.Combine(common, "antiphon", "children");
        Directory.CreateDirectory(directory);
        var journal = new RepositoryChildJournal(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json"),
            new ChildRecord(1, common, null, null, Purpose: tag?.Purpose, TaskId: tag?.TaskId));
        await journal.SaveAsync(ct); // Unknown/start intent is durable before Process.Start.
        return journal;
    }

    public Task StartedAsync(Process child, CancellationToken ct)
        => StartedAsync(child.Id, TryStartTicks(child, out var startTicks) ? startTicks : null, ct);

    /// <summary>Records the child, or with no start identity records its already-exited root.</summary>
    internal async Task StartedAsync(int processId, long? startTicks, CancellationToken ct)
    {
        if (startTicks is { } ticks)
        {
            await StartedAsync(processId, ticks, ct);
            return;
        }
        // CARD-0661: a fast child exited (and on Linux was reaped, taking its /proc start
        // identity with it) before we read it. Its own handle has exited, so record it as
        // completed rather than failing the command. The file still fences admission until
        // Exited removes it after the streams drain, exactly as a started record does.
        _record = _record with { ProcessId = processId, Completed = true };
        await SaveAsync(ct);
    }

    /// <summary>The child's start identity, or false when it has already exited and the
    /// platform can no longer report it. A still-running child that cannot be read throws.</summary>
    internal static bool TryStartTicks(Process child, out long startTicks)
    {
        try
        {
            startTicks = child.StartTime.ToUniversalTime().Ticks;
            return true;
        }
        catch (Exception ex) when ((ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                                   && child.HasExited)
        {
            startTicks = 0;
            return false;
        }
    }

    public async Task StartedAsync(int processId, long startTicks, CancellationToken ct)
    {
        _record = _record with { ProcessId = processId, StartTicks = startTicks };
        await SaveAsync(ct);
    }

    public void Exited(Process child)
    {
        if (!child.HasExited) throw new InvalidOperationException("owned_child_still_running");
        File.Delete(_path); // Only this operation's journal, after its exact handle exited.
    }

    /// <summary>Process.Start definitely created no child (LandingGit.CreatedNoChild), so none needs fencing.</summary>
    public void NotStarted() => File.Delete(_path);

    public async Task ExitedAsync(CancellationToken ct)
    {
        if (_record.ProcessId is null || _record.StartTicks is null
            || await new LandingGit().IsProcessAliveAsync(_record.ProcessId.Value, _record.StartTicks.Value, ct) != false)
            throw new InvalidOperationException("owned_child_exit_unconfirmed");
        File.Delete(_path);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        // A pre-replace crash must not leave an unknown file in children: the process never
        // started on the first save, and an update must retain its prior durable record.
        var staging = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(_path)!)!, "children-staging");
        Directory.CreateDirectory(staging);
        var temporary = Path.Combine(staging, Guid.NewGuid().ToString("N") + ".tmp");
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, _record, cancellationToken: ct);
            stream.Flush(true);
        }
        if (BeforeReplaceForTests is { } beforeReplace) await beforeReplace(temporary);
        File.Move(temporary, _path, true);
    }

    /// <summary>Reader-side purposes that do not fence while the recorded process is alive.</summary>
    internal static readonly IReadOnlySet<string> LiveNonFencingPurposes = new HashSet<string>(StringComparer.Ordinal)
    {
        "remote-prep-push",
    };

    public static async Task<bool> HasUnfinishedAsync(string common, ILandingGit git, CancellationToken ct)
        => await FirstFencingAsync(common, git, ct) is not null;

    /// <summary>The first record that still fences, or null when every record is a live non-fencing push.</summary>
    internal static async Task<FencingRecord?> FirstFencingAsync(string common, ILandingGit git, CancellationToken ct)
    {
        var directory = Path.Combine(common, "antiphon", "children");
        try
        {
            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.Directory) == 0) return FencingRecord.Untagged;
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            foreach (var path in Directory.EnumerateFiles(directory))
            {
                // A torn/unacknowledged start or PID save is ambiguous and fences admission too.
                // So does a Completed record (CARD-0661): its root exited, but a crash before the
                // streams drained leaves descendants unproven. There is no startup auto-clear; the
                // explicit script recovers it after descendant inspection, as for a dead root.
                // CARD-1076: a well-formed live remote-prep-push does not fence. Dead, reused,
                // unknown and unreadable records still do.
                if (!path.EndsWith(".json", StringComparison.Ordinal)) return FencingRecord.Untagged;
                using var stream = File.OpenRead(path);
                var record = await JsonSerializer.DeserializeAsync<ChildRecord>(stream, cancellationToken: ct);
                if (record is { SchemaVersion: 1, ProcessId: int processId, StartTicks: long startTicks }
                    && LandingGit.PathsEqual(record.CommonDirectory, common))
                {
                    var alive = await git.IsProcessAliveAsync(processId, startTicks, ct);
                    if (alive == true && record.Purpose is string purpose && LiveNonFencingPurposes.Contains(purpose))
                        continue;
                }
                // A dead/reused root PID is not an acknowledged exit of its process tree.
                // Only the owning invocation removes its journal after awaiting its child.
                // recover-repository-children.ps1 provides explicit recovery after descendant inspection.
                return new FencingRecord(record?.Purpose, record?.TaskId);
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return FencingRecord.Untagged; }
    }

    internal sealed record FencingRecord(string? Purpose, Guid? TaskId)
    {
        public static readonly FencingRecord Untagged = new(null, null);
    }

    internal sealed record ChildRecord(int SchemaVersion, string CommonDirectory, int? ProcessId, long? StartTicks,
        bool Completed = false, string? Purpose = null, Guid? TaskId = null);
}
