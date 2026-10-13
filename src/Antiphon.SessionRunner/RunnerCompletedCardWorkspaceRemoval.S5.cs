using System.Text.Json;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

public sealed partial class RunnerCompletedCardWorkspaceRemoval
{
    internal Func<Task>? AfterInitialInspectionForTest { get; set; }
    internal Action<string>? BeforeMutationForTest { get; set; }
    internal Action<string>? BeforeDeleteEntryForTest { get; set; }
    internal Action<string>? AfterDeleteValidationForTest { get; set; }
    internal Action<string>? AfterJournalStateForTest { get; set; }
    internal Action<string>? AfterMutationForTest { get; set; }

    // C1017_RunnerNoForceBypass reaches this parser with a raw Force member.
    public async Task<RunnerCompletedCardWorkspaceRemovalResult> RemoveJsonAsync(string json,
        RunnerWorkspaceRemovalJournal journal, RunnerWorkspaceUseGate gate, CancellationToken ct)
    {
        var validation = CompletedCardWorkspaceContract.Validate(json);
        if (!validation.IsValid) return Hold(validation.Reason!);
        var command = JsonSerializer.Deserialize<CompletedCardWorkspaceCommand>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return await RemoveAsync(command, journal, gate, ct);
    }

    private async Task<string?> CheckRemovalProofAsync(CompletedCardWorkspaceCommand command, CancellationToken ct)
    {
        var binding = command.Binding;
        var inspected = await InspectAsync(new(command.SchemaVersion, binding), ct);
        if (inspected.Reason is not null && (inspected.Reason != "evidence_requires_admission" || !EvidenceAuthority(command)))
            return inspected.Reason;
        if (command.PolicyPatterns.Count != 0)
        {
            var classifier = new RunnerWorkspaceIgnoredContent(command.PolicyPatterns);
            if (inspected.IgnoredContent.Any(e => classifier.Classify(e.Path) == RunnerIgnoredContentKind.Protected))
                return "protected_content";
        }
        if (inspected.HeadSha != command.Proof.ExpectedHeadSha) return "head_mismatch";
        return await CheckRemoteProofAsync(binding, command.Proof, binding.WorktreePath, ct);
    }

    private static bool EvidenceAuthority(CompletedCardWorkspaceCommand command) =>
        command.Disposition == CompletedCardWorkspaceProtocol.DispositionCardDone
        && IsDigest(command.ReportDigest) && IsDigest(command.ArtifactProofDigest) && IsDigest(command.DisposalPolicyDigest)
        && command.ArtifactPointers.Count != 0 && command.ArtifactPointers.All(p => Path.IsPathFullyQualified(p)
            && !RunnerWorkspaceUseGate.Overlaps(p, command.Binding.WorktreePath)
            && !RunnerWorkspaceUseGate.Overlaps(p, command.Binding.CommonDirectory)
            && !RunnerWorkspaceUseGate.Overlaps(p, RunnerWorkspaceRemovalJournal.SetAsideFor(command)));

    private static bool IsDigest(string value) => value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

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
            if (await CheckRemovalProofAsync(command, ct) is { } reason) return Hold(reason);
            if (AfterInitialInspectionForTest is { } after) await after();
        }
        RunnerCompletedCardWorkspaceRemovalResult result = Hold("workspace_in_use");
        try
        {
            var admitted = await gate.TryRetireRemovalAsync(command, async () =>
            {
                if (gate.HasOwnershipConflict(command.Binding.WorktreePath)) { result = Hold("workspace_in_use"); return; }
                var intent = journal.Read()!.Single(r => r.Command.OperationId == command.OperationId);
                if (intent.Phase == "Intent") AfterJournalStateForTest?.Invoke("Intent");
                if (intent.SetAsidePath is null)
                {
                    if (await CheckRemovalProofAsync(command, ct) is { } reason) { result = Hold(reason); return; }
                    if (gate.HasOwnershipConflict(command.Binding.WorktreePath)) { result = Hold("workspace_in_use"); return; }
                    var aside = RunnerWorkspaceRemovalJournal.SetAsideFor(command);
                    if (Exists(aside)) { result = Hold("set_aside_occupied"); return; }
                    if (command.ArtifactPointers.Any(p => RunnerWorkspaceUseGate.Overlaps(p, command.Binding.WorktreePath)
                        || RunnerWorkspaceUseGate.Overlaps(p, aside))) { result = Hold("artifact_inside_cleanup_root"); return; }
                    var directoryIdentity = RunnerWorkspaceNoFollowDelete.Identity(command.Binding.WorktreePath);
                    var registrationIdentity = RunnerWorkspaceNoFollowDelete.Identity(command.Binding.GitDirectory);
                    intent = intent with { SetAsidePath = aside,
                        DirectoryIdentity = directoryIdentity, RegistrationIdentity = registrationIdentity,
                        DirectoryEntries = await RunnerWorkspaceNoFollowDelete.InventoryAsync(command.Binding.WorktreePath, ct, directoryIdentity),
                        RegistrationEntries = await RunnerWorkspaceNoFollowDelete.InventoryAsync(command.Binding.GitDirectory, ct, registrationIdentity), Phase = "Prepared" };
                    Save(journal, intent);
                }
                result = await ResumeAsync(command, journal, gate, intent, ct);
            }, ct);
            return admitted ? result : Hold("workspace_in_use");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PhoneHomeAdmissionException
            or PlatformNotSupportedException)
        {
            return Hold(ex is RunnerWorkspaceNoFollowDelete.Refusal refusal ? refusal.Reason : "removal_interrupted");
        }
    }

    private async Task<RunnerCompletedCardWorkspaceRemovalResult> ResumeAsync(CompletedCardWorkspaceCommand command,
        RunnerWorkspaceRemovalJournal journal, RunnerWorkspaceUseGate gate, RunnerWorkspaceRemovalJournal.RemovalIntent intent, CancellationToken ct)
    {
        var binding = command.Binding;
        var aside = intent.SetAsidePath!;
        if (!UnlinkedPath(Path.GetDirectoryName(aside)!) || !UnlinkedPath(binding.CommonDirectory)) return Hold("linked_content");
        if (gate.HasOwnershipConflict(binding.WorktreePath)) return Hold("workspace_in_use");
        if (await CheckRemoteProofAsync(binding, command.Proof, binding.CommonDirectory, ct) is { } replayProofReason)
            return Hold(replayProofReason);
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
                if (await CheckRemovalProofAsync(command, ct) is { } reason) return Hold(reason);
                await RunnerWorkspaceNoFollowDelete.ValidateRemainingAsync(binding.WorktreePath, intent.DirectoryEntries!, ct);
                intent = Spend(journal, intent, "rename");
                BeforeMutationForTest?.Invoke("rename");
                Directory.Move(binding.WorktreePath, aside);
                AfterMutationForTest?.Invoke("rename");
            }
            intent = intent with { Phase = "Moved" }; Save(journal, intent);
        }
        if (intent.Phase is "Moved" or "Unregistering")
        {
            if (!Directory.Exists(aside) || RunnerWorkspaceNoFollowDelete.Identity(aside) != intent.DirectoryIdentity)
                return Hold("absence_without_receipt");
            await RunnerWorkspaceNoFollowDelete.ValidateRemainingAsync(aside, intent.DirectoryEntries!, ct);
            if (Exists(binding.GitDirectory))
            {
                // The exact recorded admin inode/manifest is the target. There is no repository
                // prune or Git mutation command, so sibling registrations and refs are retained.
                if (RunnerWorkspaceNoFollowDelete.Identity(binding.GitDirectory) != intent.RegistrationIdentity)
                    return Hold("registration_replaced");
                intent = Spend(journal, intent with { Phase = "Unregistering" }, "unregister");
                BeforeMutationForTest?.Invoke("unregister");
                await RunnerWorkspaceNoFollowDelete.DeleteOwnedAsync(binding.GitDirectory, intent.RegistrationIdentity!,
                    intent.RegistrationEntries!, null, ct);
                AfterMutationForTest?.Invoke("unregister");
            }
            else if (intent.Phase != "Unregistering") return Hold("registration_absence_unproved");
            intent = intent with { Phase = "Unregistered" }; Save(journal, intent);
        }
        if (intent.Phase is "Unregistered" or "Deleting")
        {
            if (Exists(binding.GitDirectory)) return Hold("registration_replaced");
            if (Exists(aside))
            {
                intent = Spend(journal, intent with { Phase = "Deleting" }, "delete");
                BeforeMutationForTest?.Invoke("delete");
                await RunnerWorkspaceNoFollowDelete.DeleteOwnedAsync(aside, intent.DirectoryIdentity!, intent.DirectoryEntries!,
                    BeforeDeleteEntryForTest, ct, AfterDeleteValidationForTest);
                AfterMutationForTest?.Invoke("delete");
            }
            else if (intent.Phase != "Deleting") return Hold("absence_without_receipt");
            intent = intent with { Phase = "Deleted" }; Save(journal, intent);
        }
        if (intent.Phase != "Deleted" || Exists(aside) || Exists(binding.GitDirectory)) return Hold("components_unproved");
        BeforeMutationForTest?.Invoke("receipt");
        var receipt = RunnerWorkspaceRemovalJournal.ExpectedReceipt(intent);
        Save(journal, intent with { Phase = "Complete", Receipt = receipt });
        AfterMutationForTest?.Invoke("receipt");
        return new(true, null, receipt);
    }

    private RunnerWorkspaceRemovalJournal.RemovalIntent Spend(RunnerWorkspaceRemovalJournal journal,
        RunnerWorkspaceRemovalJournal.RemovalIntent intent, string step)
    {
        if (intent.ChargedSteps.Contains(step)) { Save(journal, intent); return intent; }
        if (intent.RemainingAllowance <= 0) throw new IOException("command_allowance_spent");
        var next = intent with { RemainingAllowance = intent.RemainingAllowance - 1, ChargedSteps = [.. intent.ChargedSteps, step] }; Save(journal, next); return next;
    }
    private void Save(RunnerWorkspaceRemovalJournal journal, RunnerWorkspaceRemovalJournal.RemovalIntent intent)
    {
        if (!journal.Save(intent)) throw new IOException("journal_write_failed");
        AfterJournalStateForTest?.Invoke(intent.Phase);
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
