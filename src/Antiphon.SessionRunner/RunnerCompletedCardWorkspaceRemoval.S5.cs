using System.Text.Json;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

public sealed partial class RunnerCompletedCardWorkspaceRemoval
{
    internal Func<Task>? AfterInitialInspectionForTest { get; set; }
    internal Action<string>? BeforeMutationForTest { get; set; }
    internal Action<string>? BeforeDeleteEntryForTest { get; set; }

    public async Task<RunnerCompletedCardWorkspaceRemovalResult> RemoveAsync(CompletedCardWorkspaceCommand command,
        RunnerWorkspaceRemovalJournal journal, RunnerWorkspaceUseGate gate, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(command, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var validation = CompletedCardWorkspaceContract.Validate(json);
        if (!validation.IsValid) return Hold(validation.Reason!);
        var digest = CompletedCardWorkspaceContract.CanonicalDigest(json);
        var records = journal.Read();
        if (records is null) return Hold("recipient_store_unreadable");
        var replay = records.SingleOrDefault(r => r.Command.OperationId == command.OperationId);
        if (replay is not null && replay.Digest != digest) return Hold("operation_digest_mismatch");
        if (journal.RunnerStoreId != command.Binding.RunnerStoreId) return Hold("runner_store_mismatch");
        if (workspace.InspectionBindingReason(command.Binding) is { } identityReason) return Hold(identityReason);
        if (replay is { Receipt: { } saved }) return new(true, null, saved);
        // A fresh request proves physical source before issuing an external intent. An already
        // set-aside command instead proves its exact recorded directory/registration components.
        if (replay?.SetAsidePath is null)
        {
            if (await CheckProofAsync(command.Binding, command.Proof, ct) is { } reason) return Hold(reason);
            if (AfterInitialInspectionForTest is { } after) await after();
        }
        RunnerCompletedCardWorkspaceRemovalResult result = Hold("workspace_in_use");
        try
        {
            var admitted = await gate.TryRetireAsync(command, async () =>
            {
                if (gate.HasOwnershipConflict(command.Binding.WorktreePath)) { result = Hold("workspace_in_use"); return; }
                var intent = journal.Read()!.Single(r => r.Command.OperationId == command.OperationId);
                if (intent.SetAsidePath is null)
                {
                    if (await CheckProofAsync(command.Binding, command.Proof, ct) is { } reason) { result = Hold(reason); return; }
                    if (gate.HasOwnershipConflict(command.Binding.WorktreePath)) { result = Hold("workspace_in_use"); return; }
                    var aside = RunnerWorkspaceRemovalJournal.SetAsideFor(command);
                    if (Exists(aside)) { result = Hold("set_aside_occupied"); return; }
                    if (command.ArtifactPointers.Any(p => RunnerWorkspaceUseGate.Overlaps(p, command.Binding.WorktreePath)
                        || RunnerWorkspaceUseGate.Overlaps(p, aside))) { result = Hold("artifact_inside_cleanup_root"); return; }
                    intent = intent with { SetAsidePath = aside,
                        DirectoryIdentity = RunnerWorkspaceNoFollowDelete.Identity(command.Binding.WorktreePath),
                        RegistrationIdentity = RunnerWorkspaceNoFollowDelete.Identity(command.Binding.GitDirectory),
                        DirectoryEntries = RunnerWorkspaceNoFollowDelete.Inventory(command.Binding.WorktreePath),
                        RegistrationEntries = RunnerWorkspaceNoFollowDelete.Inventory(command.Binding.GitDirectory), Phase = "Prepared" };
                    Save(journal, intent);
                }
                result = await ResumeAsync(command, journal, intent, ct);
            }, ct);
            return admitted ? result : Hold("workspace_in_use");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PhoneHomeAdmissionException
            or PlatformNotSupportedException)
        {
            return Hold("removal_interrupted");
        }
    }

    private async Task<RunnerCompletedCardWorkspaceRemovalResult> ResumeAsync(CompletedCardWorkspaceCommand command,
        RunnerWorkspaceRemovalJournal journal, RunnerWorkspaceRemovalJournal.RemovalIntent intent, CancellationToken ct)
    {
        var binding = command.Binding;
        var aside = intent.SetAsidePath!;
        if (!UnlinkedPath(Path.GetDirectoryName(aside)!) || !UnlinkedPath(binding.CommonDirectory)) return Hold("linked_content");
        if (intent.Phase == "Prepared")
        {
            if (Exists(aside))
            {
                if (RunnerWorkspaceNoFollowDelete.Identity(aside) != intent.DirectoryIdentity) return Hold("set_aside_occupied");
            }
            else
            {
                if (!Directory.Exists(binding.WorktreePath)) return Hold("absence_without_receipt");
                if (RunnerWorkspaceNoFollowDelete.Identity(binding.WorktreePath) != intent.DirectoryIdentity) return Hold("replacement_original");
                if (await CheckProofAsync(binding, command.Proof, ct) is { } reason) return Hold(reason);
                RunnerWorkspaceNoFollowDelete.ValidateRemaining(binding.WorktreePath, intent.DirectoryEntries!);
                intent = Spend(journal, intent);
                BeforeMutationForTest?.Invoke("rename");
                Directory.Move(binding.WorktreePath, aside);
            }
            intent = intent with { Phase = "Moved" }; Save(journal, intent);
        }
        if (intent.Phase is "Moved" or "Unregistering")
        {
            if (!Directory.Exists(aside) || RunnerWorkspaceNoFollowDelete.Identity(aside) != intent.DirectoryIdentity)
                return Hold("absence_without_receipt");
            RunnerWorkspaceNoFollowDelete.ValidateRemaining(aside, intent.DirectoryEntries!);
            if (Exists(binding.GitDirectory))
            {
                // The exact recorded admin inode/manifest is the target. There is no repository
                // prune or Git mutation command, so sibling registrations and refs are retained.
                if (RunnerWorkspaceNoFollowDelete.Identity(binding.GitDirectory) != intent.RegistrationIdentity)
                    return Hold("registration_replaced");
                intent = Spend(journal, intent with { Phase = "Unregistering" });
                BeforeMutationForTest?.Invoke("unregister");
                RunnerWorkspaceNoFollowDelete.DeleteOwned(binding.GitDirectory, intent.RegistrationIdentity!,
                    intent.RegistrationEntries!, null, ct);
            }
            else if (intent.Phase != "Unregistering") return Hold("registration_absence_unproved");
            intent = intent with { Phase = "Unregistered" }; Save(journal, intent);
        }
        if (intent.Phase is "Unregistered" or "Deleting")
        {
            if (Exists(binding.GitDirectory)) return Hold("registration_replaced");
            if (Exists(aside))
            {
                intent = Spend(journal, intent with { Phase = "Deleting" });
                BeforeMutationForTest?.Invoke("delete");
                RunnerWorkspaceNoFollowDelete.DeleteOwned(aside, intent.DirectoryIdentity!, intent.DirectoryEntries!,
                    BeforeDeleteEntryForTest, ct);
            }
            else if (intent.Phase != "Deleting") return Hold("absence_without_receipt");
            intent = intent with { Phase = "Deleted" }; Save(journal, intent);
        }
        if (intent.Phase != "Deleted" || Exists(aside) || Exists(binding.GitDirectory)) return Hold("components_unproved");
        BeforeMutationForTest?.Invoke("receipt");
        var proofDigest = CompletedCardWorkspaceContract.CanonicalDigest(JsonSerializer.Serialize(command.Proof,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var receipt = new CompletedCardWorkspaceReceipt(command.SchemaVersion, command.CardId, command.DoneRevisionId,
            command.TargetId, command.EndpointId, command.OperationId, binding.TaskId, binding.Attempt, binding.BindingId,
            binding.PreparationId, binding.RunnerId, binding.RunnerStoreId, binding.RepositoryIdentity, binding.CommonDirectory,
            binding.GitDirectory, binding.WorktreePath, binding.SourceFullRef, binding.CreationBaselineSha,
            command.Proof.ExpectedHeadSha, proofDigest, command.ArtifactProofDigest, command.DisposalPolicyDigest,
            command.Disposition, aside, command.OperationId.ToString("D") + ":" + intent.Digest, true, true);
        Save(journal, intent with { Phase = "Complete", Receipt = receipt });
        return new(true, null, receipt);
    }

    private static RunnerWorkspaceRemovalJournal.RemovalIntent Spend(RunnerWorkspaceRemovalJournal journal,
        RunnerWorkspaceRemovalJournal.RemovalIntent intent)
    {
        if (intent.RemainingAllowance <= 0) throw new IOException("command_allowance_spent");
        var next = intent with { RemainingAllowance = intent.RemainingAllowance - 1 }; Save(journal, next); return next;
    }
    private static void Save(RunnerWorkspaceRemovalJournal journal, RunnerWorkspaceRemovalJournal.RemovalIntent intent)
    {
        if (!journal.Save(intent)) throw new IOException("journal_write_failed");
    }
    private static bool Exists(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }
    private static RunnerCompletedCardWorkspaceRemovalResult Hold(string reason) => new(false, reason, null);
}

public sealed record RunnerCompletedCardWorkspaceRemovalResult(bool Complete, string? Reason,
    CompletedCardWorkspaceReceipt? Receipt);
