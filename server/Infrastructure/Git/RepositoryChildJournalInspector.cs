using System.Text.Json;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Infrastructure.Git;

public enum JournalRecordState
{
    Alive,
    Dead,
    Completed,
    Unknown,
    Malformed,
}

public sealed record JournalRecordFinding(
    string File,
    JournalRecordState State,
    TimeSpan Age,
    bool Stale,
    int? ProcessId);

public sealed record JournalInspection(string? CommonDirectory, IReadOnlyList<JournalRecordFinding> Findings)
{
    public int StaleCount => Findings.Count(finding => finding.Stale);
}

/// <summary>CARD-0726 D-8: read-only classification of one repository's child journal.</summary>
public class RepositoryChildJournalInspector(ILandingGit git)
{
    public virtual async Task<JournalInspection> InspectAsync(
        string repository, TimeSpan staleAfter, DateTimeOffset now, CancellationToken ct)
    {
        var common = await git.CommonDirectoryAsync(repository, ct);
        return await InspectCommonAsync(common, staleAfter, now, ct);
    }

    public virtual async Task<JournalInspection> InspectCommonAsync(
        string common, TimeSpan staleAfter, DateTimeOffset now, CancellationToken ct)
    {
        var children = Path.Combine(common, "antiphon", "children");
        if (!PathExists(children))
            return new JournalInspection(common, []);

        var attributes = File.GetAttributes(children);
        if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
        {
            return new JournalInspection(common, [ClassifyPath(children, JournalRecordState.Malformed, null, staleAfter, now)]);
        }

        var findings = new List<JournalRecordFinding>();
        foreach (var path in Directory.EnumerateFiles(children))
        {
            ct.ThrowIfCancellationRequested();
            findings.Add(await ClassifyFileAsync(path, common, staleAfter, now, ct));
        }

        return new JournalInspection(common, findings);
    }

    private async Task<JournalRecordFinding> ClassifyFileAsync(
        string path, string common, TimeSpan staleAfter, DateTimeOffset now, CancellationToken ct)
    {
        var attributes = File.GetAttributes(path);
        var name = Path.GetFileName(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0
            || !name.EndsWith(".json", StringComparison.Ordinal))
            return ClassifyPath(path, JournalRecordState.Malformed, null, staleAfter, now);

        RepositoryChildJournal.ChildRecord? record;
        try
        {
            await using var stream = File.OpenRead(path);
            record = await JsonSerializer.DeserializeAsync<RepositoryChildJournal.ChildRecord>(stream, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return ClassifyPath(path, JournalRecordState.Malformed, null, staleAfter, now);
        }

        if (record is not { SchemaVersion: 1 })
            return ClassifyPath(path, JournalRecordState.Unknown, record?.ProcessId, staleAfter, now);

        if (record.StartTicks is null && record.Completed)
            return ClassifyPath(path, JournalRecordState.Completed, record.ProcessId, staleAfter, now);

        if (record.ProcessId is int processId && record.StartTicks is long startTicks
            && LandingGit.PathsEqual(record.CommonDirectory, common))
        {
            var alive = await git.IsProcessAliveAsync(processId, startTicks, ct);
            var state = alive switch
            {
                true => JournalRecordState.Alive,
                false => JournalRecordState.Dead,
                _ => JournalRecordState.Unknown,
            };
            return ClassifyPath(path, state, processId, staleAfter, now);
        }

        return ClassifyPath(path, JournalRecordState.Unknown, record.ProcessId, staleAfter, now);
    }

    private static JournalRecordFinding ClassifyPath(
        string path, JournalRecordState state, int? processId, TimeSpan staleAfter, DateTimeOffset now)
    {
        var age = now.UtcDateTime - File.GetLastWriteTimeUtc(path);
        if (age < TimeSpan.Zero)
            age = TimeSpan.Zero;
        var stale = state != JournalRecordState.Alive && age >= staleAfter;
        return new JournalRecordFinding(path, state, age, stale, processId);
    }

    private static bool PathExists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }
}
