using System.Diagnostics;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>Standing admission fence that survives the server's lease handle closing on death.</summary>
internal sealed class RepositoryChildJournal
{
    private readonly string _path;
    private ChildRecord _record;

    private RepositoryChildJournal(string path, ChildRecord record) { _path = path; _record = record; }

    public static async Task<RepositoryChildJournal> BeginAsync(string repository, CancellationToken ct)
    {
        var common = await new LandingGit().CommonDirectoryAsync(repository, ct);
        var directory = Path.Combine(common, "antiphon", "children");
        Directory.CreateDirectory(directory);
        var journal = new RepositoryChildJournal(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json"),
            new ChildRecord(1, common, null, null));
        await journal.SaveAsync(ct); // Unknown/start intent is durable before Process.Start.
        return journal;
    }

    public async Task StartedAsync(Process child, CancellationToken ct)
        => await StartedAsync(child.Id, child.StartTime.ToUniversalTime().Ticks, ct);

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

    public async Task ExitedAsync(CancellationToken ct)
    {
        if (_record.ProcessId is null || _record.StartTicks is null
            || await new LandingGit().IsProcessAliveAsync(_record.ProcessId.Value, _record.StartTicks.Value, ct) != false)
            throw new InvalidOperationException("owned_child_exit_unconfirmed");
        File.Delete(_path);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        var temporary = _path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, _record, cancellationToken: ct);
            stream.Flush(true);
        }
        File.Move(temporary, _path, true);
    }

    public static async Task<bool> HasUnfinishedAsync(string common, ILandingGit git, CancellationToken ct)
    {
        var directory = Path.Combine(common, "antiphon", "children");
        try
        {
            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.Directory) == 0) return true;
            }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
            foreach (var path in Directory.EnumerateFiles(directory))
            {
                // A torn/unacknowledged start or PID save is ambiguous and fences admission too.
                if (!path.EndsWith(".json", StringComparison.Ordinal)) return true;
                using var stream = File.OpenRead(path);
                var record = await JsonSerializer.DeserializeAsync<ChildRecord>(stream, cancellationToken: ct);
                if (record is not { SchemaVersion: 1, ProcessId: not null, StartTicks: not null }
                    || !LandingGit.PathsEqual(record.CommonDirectory, common)
                    || await git.IsProcessAliveAsync(record.ProcessId.Value, record.StartTicks.Value, ct) != false)
                    return true;
                // A dead/reused root PID is not an acknowledged exit of its process tree.
                // Only the owning invocation removes its journal after awaiting its child.
                // A crash orphan needs explicit recovery inspection, never automatic admission.
                return true;
            }
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return true; }
    }

    internal sealed record ChildRecord(int SchemaVersion, string CommonDirectory, int? ProcessId, long? StartTicks);
}
