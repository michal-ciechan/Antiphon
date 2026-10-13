using System.Text.Json;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>External command/fence and S5 component progress; C1032_RemovalComponentRecovery reads it afresh.</summary>
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
                if (!Valid(intent.Command) || Digest(intent.Command) != intent.Digest || !ValidProgress(intent))
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

    internal bool Save(RemovalIntent intent)
    {
        var records = Read();
        if (records is null || !ValidProgress(intent)) return false;
        var index = records.ToList().FindIndex(i => i.Command.OperationId == intent.Command.OperationId);
        if (index < 0 || records[index].Digest != intent.Digest || Digest(intent.Command) != intent.Digest) return false;
        var updated = records.ToArray(); updated[index] = intent;
        try { AtomicWrite(JournalPath, JsonSerializer.Serialize(new Journal(1, RunnerStoreId, updated.ToList()), _json)); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    internal static string SetAsideFor(CompletedCardWorkspaceCommand command) =>
        command.Binding.WorktreePath + ".cleanup-" + command.OperationId.ToString("N");

    private bool ValidProgress(RemovalIntent intent)
    {
        if (intent.SetAsidePath is null) return intent.Phase == "Intent" && intent.Receipt is null;
        if (intent.SetAsidePath != SetAsideFor(intent.Command) || intent.DirectoryIdentity is null
            || intent.RegistrationIdentity is null || intent.DirectoryEntries is null || intent.RegistrationEntries is null
            || intent.RemainingAllowance is < 0 or > 8 || intent.ChargedSteps is null
            || intent.ChargedSteps.Distinct(StringComparer.Ordinal).Count() != intent.ChargedSteps.Count
            || intent.ChargedSteps.Any(s => s is not ("rename" or "unregister" or "delete"))
            || intent.RemainingAllowance != 8 - intent.ChargedSteps.Count || intent.Phase is not
                ("Prepared" or "Moved" or "Unregistering" or "Unregistered" or "Deleting" or "Deleted" or "Complete")) return false;
        if (!ValidEntries(intent.DirectoryEntries) || !ValidEntries(intent.RegistrationEntries)) return false;
        return intent.Phase == "Complete" ? intent.Receipt == ExpectedReceipt(intent) : intent.Receipt is null;
    }
    internal static CompletedCardWorkspaceReceipt ExpectedReceipt(RemovalIntent intent)
    {
        var command = intent.Command; var binding = command.Binding;
        var proofDigest = CompletedCardWorkspaceContract.CanonicalDigest(JsonSerializer.Serialize(command.Proof,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return new(command.SchemaVersion, command.CardId, command.DoneRevisionId, command.TargetId, command.EndpointId,
            command.OperationId, binding.TaskId, binding.Attempt, binding.BindingId, binding.PreparationId,
            binding.RunnerId, binding.RunnerStoreId, binding.RepositoryIdentity, binding.CommonDirectory,
            binding.GitDirectory, binding.WorktreePath, binding.SourceFullRef, binding.CreationBaselineSha,
            command.Proof.ExpectedHeadSha, proofDigest, command.ArtifactProofDigest, command.DisposalPolicyDigest,
            command.Disposition, intent.SetAsidePath!, command.OperationId.ToString("D") + ":" + intent.Digest, true, true);
    }

    private static bool ValidEntries(IReadOnlyList<RunnerWorkspaceNoFollowDelete.Entry> entries) =>
        entries.Select(e => e.Path).Distinct(StringComparer.Ordinal).Count() == entries.Count
        && entries.All(e => !Path.IsPathRooted(e.Path) && !e.Path.Split(Path.DirectorySeparatorChar).Contains("..")
            && !string.IsNullOrWhiteSpace(e.Path) && (e.Directory ? e.Hash is null : e.Hash is { Length: 64 }));

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

    public sealed record RemovalIntent(CompletedCardWorkspaceCommand Command, string Digest)
    {
        public string? SetAsidePath { get; init; }
        public string? DirectoryIdentity { get; init; }
        public string? RegistrationIdentity { get; init; }
        [System.Text.Json.Serialization.JsonInclude]
        internal IReadOnlyList<RunnerWorkspaceNoFollowDelete.Entry>? DirectoryEntries { get; init; }
        [System.Text.Json.Serialization.JsonInclude]
        internal IReadOnlyList<RunnerWorkspaceNoFollowDelete.Entry>? RegistrationEntries { get; init; }
        public string Phase { get; init; } = "Intent";
        public int RemainingAllowance { get; init; } = 8;
        public IReadOnlyList<string> ChargedSteps { get; init; } = [];
        public CompletedCardWorkspaceReceipt? Receipt { get; init; }
    }
    private sealed record Journal(int SchemaVersion, string RunnerStoreId, List<RemovalIntent> Intents);
}
