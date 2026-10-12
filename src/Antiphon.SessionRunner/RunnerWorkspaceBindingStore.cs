using System.Text.Json;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-1032. Durable mirror-creation ledger. The file lives under the runner's configured state
/// root, never inside a mirror and never at a path taken from the request. A torn, missing, or
/// unreadable file is not a completed binding.
/// </summary>
public sealed class RunnerWorkspaceBindingStore
{
    public const string Intent = "intent";
    public const string Added = "added";
    public const string Complete = "complete";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public RunnerWorkspaceBindingStore(string stateRoot)
    {
        StateRoot = stateRoot;
    }

    public string StateRoot { get; }

    public string RecordPath(Guid preparationId) =>
        StateRoot + "/" + preparationId.ToString("D") + ".json";

    public BindingRead Read(Guid preparationId)
    {
        var path = RecordPath(preparationId);
        try
        {
            if (Directory.Exists(path))
                return new BindingRead(BindingReadKind.Unreadable, null);
            if (!File.Exists(path))
                return new BindingRead(BindingReadKind.Missing, null);
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
                return new BindingRead(BindingReadKind.Unreadable, null);
            var record = JsonSerializer.Deserialize<RunnerWorkspaceBindingRecord>(text, Json);
            if (record is null || record.PreparationId != preparationId)
                return new BindingRead(BindingReadKind.Unreadable, null);
            var phase = record.Phase ?? "";
            if (phase == Complete)
            {
                return IsWellFormedComplete(record)
                    ? new BindingRead(BindingReadKind.Complete, record)
                    : new BindingRead(BindingReadKind.Unreadable, null);
            }

            if (phase == Intent)
                return new BindingRead(BindingReadKind.Intent, record);
            if (phase == Added)
                return new BindingRead(BindingReadKind.Added, record);
            return new BindingRead(BindingReadKind.Unreadable, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new BindingRead(BindingReadKind.Unreadable, null);
        }
    }

    public void WriteIntent(RunnerWorkspaceBindingRecord record)
    {
        if (Read(record.PreparationId).Kind != BindingReadKind.Missing)
            throw new InvalidOperationException("Mirror binding record already exists.");
        record.Phase = Intent;
        AtomicWrite(record);
    }

    public void MarkAdded(Guid preparationId)
    {
        var existing = Read(preparationId);
        if (existing.Kind != BindingReadKind.Intent || existing.Record is null || existing.Record.TreeExistedAtIntent)
            throw new InvalidOperationException("Mirror binding is not an owned intent.");
        existing.Record.Phase = Added;
        AtomicWrite(existing.Record);
    }

    public void WriteComplete(RunnerWorkspaceBindingRecord record)
    {
        var existing = Read(record.PreparationId);
        if (existing.Kind != BindingReadKind.Added || existing.Record is null
            || existing.Record.BindingId != record.BindingId || existing.Record.TreeExistedAtIntent
            || !IsWellFormedComplete(record))
            throw new InvalidOperationException("Mirror binding completion does not match the owned add.");
        record.Phase = Complete;
        record.TreeExistedAtIntent = false;
        AtomicWrite(record);
    }

    private void AtomicWrite(RunnerWorkspaceBindingRecord record)
    {
        Directory.CreateDirectory(StateRoot);
        var path = RecordPath(record.PreparationId);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(record, Json));
        File.Move(temporary, path, overwrite: true);
    }

    private static bool IsWellFormedComplete(RunnerWorkspaceBindingRecord record) =>
        record.TaskId != Guid.Empty
        && record.PreparationId != Guid.Empty
        && record.BindingId != Guid.Empty
        && record.Attempt >= 1
        && !string.IsNullOrWhiteSpace(record.RunnerId)
        && !string.IsNullOrWhiteSpace(record.RunnerStoreId)
        && !string.IsNullOrWhiteSpace(record.RepositoryIdentity)
        && !string.IsNullOrWhiteSpace(record.Branch)
        && !string.IsNullOrWhiteSpace(record.Name)
        && !string.IsNullOrWhiteSpace(record.SourceFullRef)
        && !string.IsNullOrWhiteSpace(record.CreationBaselineSha)
        && !string.IsNullOrWhiteSpace(record.WorktreePath)
        && !string.IsNullOrWhiteSpace(record.CommonDirectory)
        && !string.IsNullOrWhiteSpace(record.GitDirectory);
}

public enum BindingReadKind
{
    Missing,
    Unreadable,
    Intent,
    Added,
    Complete,
}

public readonly record struct BindingRead(BindingReadKind Kind, RunnerWorkspaceBindingRecord? Record);

public sealed class RunnerWorkspaceBindingRecord
{
    public string Phase { get; set; } = "";
    public Guid TaskId { get; set; }
    public int Attempt { get; set; }
    public Guid PreparationId { get; set; }
    public string RunnerId { get; set; } = "";
    public string RunnerStoreId { get; set; } = "";
    public Guid BindingId { get; set; }
    public string RepositoryIdentity { get; set; } = "";
    public string Branch { get; set; } = "";
    public string Name { get; set; } = "";
    public string SourceFullRef { get; set; } = "";
    public string CreationBaselineSha { get; set; } = "";
    public string WorktreePath { get; set; } = "";
    public string CommonDirectory { get; set; } = "";
    public string GitDirectory { get; set; } = "";
    public bool TreeExistedAtIntent { get; set; }
}
