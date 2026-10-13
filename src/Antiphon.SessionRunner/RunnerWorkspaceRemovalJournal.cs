using System.Text.Json;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>S3 admission intent only; component disposal and receipts belong to S5.</summary>
public sealed class RunnerWorkspaceRemovalJournal
{
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly string _identityPath;
    public string StateRoot { get; }
    public string RunnerStoreId { get; }
    public string JournalPath => Path.Combine(StateRoot, "journal.json");

    public RunnerWorkspaceRemovalJournal(string stateRoot, string runnerStoreId)
    {
        StateRoot = Path.GetFullPath(stateRoot);
        RunnerStoreId = runnerStoreId;
        _identityPath = StateRoot + ".identity";
        // The sibling identity survives a lost journal. Fresh roots can initialize; a previously
        // initialized root with missing content is unknown (C1032_FenceUnknownJournal).
        if (!File.Exists(_identityPath) && !Directory.Exists(_identityPath)
            && !Directory.Exists(StateRoot) && !File.Exists(StateRoot))
        {
            Directory.CreateDirectory(StateRoot);
            AtomicWrite(_identityPath, runnerStoreId);
            AtomicWrite(JournalPath, JsonSerializer.Serialize(new Journal(1, runnerStoreId, []), _json));
        }
    }

    public IReadOnlyList<RemovalIntent>? Read()
    {
        try
        {
            if (RunnerWorkspaceUseGate.Normalize(StateRoot) != Path.TrimEndingDirectorySeparator(StateRoot)
                || new FileInfo(_identityPath).LinkTarget is not null
                || new FileInfo(JournalPath).LinkTarget is not null
                || File.ReadAllText(_identityPath) != RunnerStoreId)
                return null;
            var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(JournalPath), _json);
            if (journal is null || journal.SchemaVersion != 1 || journal.RunnerStoreId != RunnerStoreId
                || journal.Intents is null || journal.Intents.Select(i => i.Command.OperationId).Distinct().Count() != journal.Intents.Count)
                return null;
            foreach (var intent in journal.Intents)
            {
                if (!Valid(intent.Command) || Digest(intent.Command) != intent.Digest)
                    return null;
            }
            return journal.Intents;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
            or ArgumentException or NotSupportedException or NullReferenceException)
        {
            return null;
        }
    }

    internal bool Record(CompletedCardWorkspaceCommand command)
    {
        if (!Valid(command)) return false;
        var existing = Read();
        if (existing is null) return false;
        var digest = Digest(command);
        var replay = existing.SingleOrDefault(i => i.Command.OperationId == command.OperationId);
        if (replay is not null) return replay.Digest == digest;
        if (existing.Any(i => RunnerWorkspaceUseGate.Overlaps(i.Command.Binding.WorktreePath, command.Binding.WorktreePath)))
            return false;
        try
        {
            AtomicWrite(JournalPath, JsonSerializer.Serialize(
                new Journal(1, RunnerStoreId, [.. existing, new(command, digest)]), _json));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private bool Valid(CompletedCardWorkspaceCommand command)
    {
        var serialized = JsonSerializer.Serialize(command, _json);
        return CompletedCardWorkspaceContract.Validate(serialized).IsValid
            && command.Binding.RunnerStoreId == RunnerStoreId
            && !RunnerWorkspaceUseGate.Overlaps(StateRoot, command.Binding.WorktreePath)
            && !RunnerWorkspaceUseGate.Overlaps(_identityPath, command.Binding.WorktreePath);
    }

    private string Digest(CompletedCardWorkspaceCommand command) =>
        CompletedCardWorkspaceContract.CanonicalDigest(JsonSerializer.Serialize(command, _json));

    private static void AtomicWrite(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.WriteThrough))
            {
                stream.Write(System.Text.Encoding.UTF8.GetBytes(content));
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public sealed record RemovalIntent(CompletedCardWorkspaceCommand Command, string Digest);
    private sealed record Journal(int SchemaVersion, string RunnerStoreId, List<RemovalIntent> Intents);
}
