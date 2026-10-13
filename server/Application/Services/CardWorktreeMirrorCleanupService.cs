using Antiphon.Server.Application.Dtos;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Files;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

// Callable S7 coordinator. Program and the residue sweep do not compose it.
public sealed class CardWorktreeMirrorCleanupService(AppDbContext db, ISessionRunnerDirectory runners,
    ILandingGit git, CardDoneArtifactPreservation artifacts, WorkspaceUseAdmission admission, TimeProvider clock,
    IOptions<WorktreeResidueSettings> settings, ICompletedCardWorkspaceClient? testClient = null,
    IOptions<PhoneHomeRunnerSettings>? phoneHome = null)
{
    internal Func<CancellationToken, Task>? BeforeIntentAsync { get; set; }
    internal Func<CancellationToken, Task>? AfterIntentAsync { get; set; }

    public async Task<CompletedCardWorkspaceRemovalResponse> TryAsync(Guid endpointId, Guid? runId, CancellationToken ct)
    {
        var endpoint = await db.CardWorktreeCleanupEndpoints.Include(e => e.Target).ThenInclude(t => t.Cleanup)
            .Include(e => e.MirrorBinding).SingleAsync(e => e.Id == endpointId, ct);
        var preview = runId is Guid run && await db.WorktreeResidueRuns.AsNoTracking()
            .Where(r => r.Id == run).Select(r => (bool?)r.Preview).SingleOrDefaultAsync(ct) == true;
        if (CardWorktreeCleanupPolicy.DestructiveAdmissionRefusal(settings.Value, endpoint.Target.Cleanup.DoneAt, preview) is { } policy)
            return Hold(policy);
        if (endpoint.MirrorBinding is not { } row || endpoint.RunnerId is null || endpoint.RunnerStoreId is null)
            return await RefuseAsync(endpoint, "mirror_binding_required", ct);
        if (runners.GetLiveStoreId(endpoint.RunnerId)?.ToString("D") != endpoint.RunnerStoreId)
            return await RefuseAsync(endpoint, "runner_store_mismatch", ct);
        var client = testClient ?? runners.Resolve(endpoint.RunnerId) as ICompletedCardWorkspaceClient;
        if (client is null) return await RefuseAsync(endpoint, PhoneHomeProblemTypes.UnsupportedOperation, ct);
        if (endpoint.OperationId is not null)
        {
            var replay = ReadCommand(endpoint);
            if (replay is null) return Hold("immutable_request_unreadable");
            return await SendAsync(endpoint, replay, client, ct);
        }
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == endpoint.Target.TaskId, ct);
        if (await EligibilityAsync(task, endpoint, row, ct) is { } eligibilityReason) return await RefuseAsync(endpoint, eligibilityReason, ct);
        var binding = Binding(row);
        var inspection = await client.InspectCompletedCardWorkspaceAsync(new(1, binding), ct);
        if (inspection.Reason is not null && inspection.Reason != "evidence_requires_admission")
            return await RefuseAsync(endpoint, inspection.Reason, ct);
        if (inspection.SourceFullRef != binding.SourceFullRef || !Oid(inspection.HeadSha))
            return await RefuseAsync(endpoint, "source_changed", ct);
        // The read-only recipient inventory has no report bytes. Remote-only task reports
        // stay held rather than being certified by a same-looking desktop path.
        if (inspection.IgnoredContent.Any(e => e.Path.Replace('\\', '/').StartsWith(".antiphon/task-", StringComparison.Ordinal)
            && e.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)))
            return await RefuseAsync(endpoint, "artifact_unpreserved", ct);
        var targetRef = WorkspaceReservationKey.NormalizeRef(task.MergeTargetRef ?? "master");
        var destination = await git.DestinationAsync(endpoint.Target.RepositoryPath, targetRef, ct);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(binding.RepositoryIdentity)));
        if (destination.Fingerprint != fingerprint) return await RefuseAsync(endpoint, "preservation_destination_mismatch", ct);
        var publication = await PublicationAsync(task, endpoint, row, inspection.HeadSha, destination.Fingerprint, ct);
        var preserved = publication?.RebasedSourceSha ?? inspection.HeadSha;
        var observed = await git.ObserveAsync(endpoint.Target.RepositoryPath, destination, preserved,
            $"refs/antiphon/land/{task.Id:N}/{Guid.NewGuid():N}/mirror-observed", ct);
        if (!observed.ContainsSource || observed.Reason is not null || !Oid(observed.Sha))
            return await RefuseAsync(endpoint, observed.Reason ?? "unlanded_work", ct);
        var roots = await CardDoneArtifactRoots.ReadAsync(db, endpoint.Target.CleanupId, ct);
        var artifact = await artifacts.VerifyMirrorAsync(task, roots, ct);
        if (!artifact.Preserved) return await RefuseAsync(endpoint, "artifact_unpreserved", ct);
        var rebased = publication?.RebasedSourceSha is { } p && p != inspection.HeadSha;
        if (rebased && (phoneHome?.Value is not { Enabled: true } auth || auth.AllowedRunnerId != binding.RunnerId
            || string.IsNullOrWhiteSpace(auth.SharedSecret))) return await RefuseAsync(endpoint, "landing_authority_unavailable", ct);
        var proof = new CompletedCardWorkspaceProof(inspection.HeadSha, binding.RepositoryIdentity, targetRef,
            observed.Sha!, rebased ? "Rebased" : "Contained", publication?.Id.ToString("D") ?? "observed:" + observed.Sha,
            preserved, rebased ? preserved : null);
        var command = new CompletedCardWorkspaceCommand(1, endpoint.Target.Cleanup.CardId, endpoint.Target.Cleanup.DoneRevisionId,
            endpoint.TargetId, endpoint.Id, Guid.NewGuid(), "CardDone", binding, proof,
            CardDoneArtifactPreservation.Digest(task.Result), artifact.Digest, CardDoneArtifactPreservation.Digest("runner-protected-minima-v1"),
            artifact.Pointers, []);
        if (rebased) command = CompletedCardWorkspaceLandingAuthority.Sign(command, publication!.Id, phoneHome!.Value.SharedSecret);
        if (BeforeIntentAsync is not null) await BeforeIntentAsync(ct);

        // C1032_AdmissionIntentOrdering: shared admission and card writer locks cover only DB
        // revalidation/intent. Inspection, artifact reads and outbound socket work are outside.
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await WorkspaceReservationJournal.LockAdmissionAsync(db, ct);
            await db.Cards.FromSqlInterpolated($"SELECT * FROM \"Cards\" WHERE \"Id\" = {command.CardId} FOR UPDATE")
                .AsNoTracking().SingleAsync(ct);
            var freshEndpoint = await db.CardWorktreeCleanupEndpoints
                .FromSqlInterpolated($"SELECT * FROM \"CardWorktreeCleanupEndpoints\" WHERE \"Id\" = {endpoint.Id} FOR UPDATE")
                .AsNoTracking().SingleAsync(ct);
            if (freshEndpoint.OperationId is not null)
            {
                command = ReadCommand(freshEndpoint) ?? throw new InvalidOperationException("immutable_request_unreadable");
                await tx.CommitAsync(ct); await db.Entry(endpoint).ReloadAsync(ct);
            }
            else
            {
                var fresh = await db.AgentTasks.FromSqlInterpolated($"SELECT * FROM \"AgentTasks\" WHERE \"Id\" = {task.Id} FOR UPDATE")
                    .AsNoTracking().SingleAsync(ct);
                var freshBinding = await db.TaskWorktreeMirrorBindings.AsNoTracking().SingleAsync(b => b.Id == row.Id, ct);
                var reason = await EligibilityAsync(fresh, endpoint, freshBinding, ct);
                if (reason is null && (fresh.Result != task.Result || fresh.ResultFilePath != task.ResultFilePath
                    || fresh.DeliverablePath != task.DeliverablePath || fresh.DeliverablePdfPath != task.DeliverablePdfPath
                    || fresh.DeliverableBundleDir != task.DeliverableBundleDir)) reason = "artifact_changed";
                if (reason is null && Binding(freshBinding) != binding) reason = "stale_mirror_binding";
                if (reason is null && fresh.ConcurrencyToken != task.ConcurrencyToken) reason = "task_snapshot_changed";
                var currentPublication = await PublicationAsync(fresh, endpoint, freshBinding, inspection.HeadSha, destination.Fingerprint, ct);
                if (reason is null && JsonSerializer.Serialize(currentPublication) != JsonSerializer.Serialize(publication)) reason = "publication_identity_changed";
                if (reason is not null)
                { await tx.RollbackAsync(ct); return await RefuseAsync(endpoint, reason, ct); }
                endpoint.OperationId = command.OperationId;
                endpoint.ImmutableRequest = JsonSerializer.SerializeToUtf8Bytes(command, PhoneHomeFraming.Json);
                endpoint.RequestDigest = CompletedCardWorkspaceContract.CanonicalDigest(Encoding.UTF8.GetString(endpoint.ImmutableRequest));
                endpoint.SourceSha = inspection.HeadSha; endpoint.ReportDigest = command.ReportDigest;
                endpoint.IntentAt = clock.GetUtcNow().UtcDateTime; endpoint.State = CardWorktreeCleanupEndpointState.IntentRecorded;
                endpoint.Reason = null; endpoint.UpdatedAt = endpoint.IntentAt.Value; endpoint.ConcurrencyToken = Guid.NewGuid();
                db.WorkspaceUseReservations.Add(new WorkspaceUseReservation
                {
                    Id = Guid.NewGuid(), CanonicalPath = binding.WorktreePath, SourceFullRef = binding.SourceFullRef,
                    CommonDirectory = binding.CommonDirectory, TaskId = task.Id, Kind = WorkspaceReservationKind.HistoricalFence,
                    Active = true, CreatedAt = endpoint.IntentAt.Value
                });
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            }
        }
        if (AfterIntentAsync is not null) await AfterIntentAsync(ct);
        return await SendAsync(endpoint, command, client, ct);
    }

    private async Task<string?> EligibilityAsync(AgentTask task, CardWorktreeCleanupEndpoint endpoint,
        TaskWorktreeMirrorBinding binding, CancellationToken ct)
    {
        var card = await db.Cards.AsNoTracking().SingleAsync(c => c.Id == endpoint.Target.Cleanup.CardId, ct);
        var revision = await db.CardRevisions.AsNoTracking().Where(r => r.CardId == card.Id && r.ToStatus == CardStatus.Done && r.FromStatus != CardStatus.Done)
            .OrderByDescending(r => r.RevisionNumber).Select(r => r.Id).FirstOrDefaultAsync(ct);
        if (card.Status != CardStatus.Done || revision != endpoint.Target.Cleanup.DoneRevisionId) return "done_generation_revoked";
        if (task.Status is not (AgentTaskStatus.Succeeded or AgentTaskStatus.Failed or AgentTaskStatus.Canceled)) return "terminal_required";
        if (CardWorktreeCleanupPolicy.SettlingRefusal(task.CompletedAt, clock.GetUtcNow().UtcDateTime, settings.Value.MinSettledMinutes) is not null) return "settling";
        if (task.CardId != card.Id || task.Attempt != endpoint.Target.TaskAttempt || binding.TaskId != task.Id
            || binding.Attempt != task.Attempt || endpoint.SourceFullRef != binding.SourceFullRef
            || WorkspaceReservationKey.NormalizeRef(task.WorktreeBranch) != binding.SourceFullRef
            || !WorkspaceReservationKey.PathsEqual(task.RepoPath ?? "", endpoint.Target.RepositoryPath)
            || !WorkspaceReservationKey.PathsEqual(task.WorktreePath ?? "", endpoint.Target.WorktreePath)) return "task_snapshot_changed";
        if (task.SourceLandingOperationId is not null || task.RepairSourceTaskId is not null || task.Role == AgentTaskRole.Mutation
            || task.Workspace != WorkspaceMode.Worktree || endpoint.Target.ExclusionReason is not null) return "ordinary_owner_required";
        if (binding.BindingId == Guid.Empty || binding.PreparationId == Guid.Empty || binding.RunnerStoreId != endpoint.RunnerStoreId
            || binding.RunnerId != endpoint.RunnerId || binding.WorktreePath != endpoint.WorktreePath
            || binding.CommonDirectory != endpoint.CommonDirectory || binding.GitDirectory != endpoint.GitDirectory
            || binding.RepositoryIdentity != endpoint.RepositoryPath) return "stale_mirror_binding";
        if (await db.AgentTaskLandRequests.AnyAsync(r => r.TaskId == task.Id && r.IsPending, ct)) return "recovery_debt";
        if (await db.AgentTaskLandings.AnyAsync(r => r.TaskId == task.Id && r.Active, ct)
            && (task.ActiveLandingId is null || !await db.AgentTaskLandings.AsNoTracking().AnyAsync(r => r.Id == task.ActiveLandingId
                && r.RemoteConfirmedAt != null && (r.Publication == LandPublicationOutcome.Landed || r.Publication == LandPublicationOutcome.AlreadyPresent), ct))) return "recovery_debt";
        if (task.AgentId is not null) return "process_ownership_unknown";
        if (await admission.HasLiveTaskConsumerAsync(binding.WorktreePath, binding.SourceFullRef, task.Id, ct)
            || await admission.HasLiveSessionOwnerAsync(binding.WorktreePath, ct)) return "live_owner";
        var reservations = await db.WorkspaceUseReservations.AsNoTracking().Where(r => r.Active).ToListAsync(ct);
        if (reservations.Any(r => admission.Overlaps(r.CanonicalPath, binding.WorktreePath)
            || r.CommonDirectory == binding.CommonDirectory && WorkspaceReservationKey.NormalizeRef(r.SourceFullRef) == binding.SourceFullRef)) return "live_owner";
        return null;
    }

    private async Task<AgentTaskLanding?> PublicationAsync(AgentTask task, CardWorktreeCleanupEndpoint endpoint,
        TaskWorktreeMirrorBinding binding, string head, string fingerprint, CancellationToken ct)
    {
        if (task.Status != AgentTaskStatus.Succeeded || task.ActiveLandingId is not Guid id) return null;
        var publications = await db.AgentTaskLandings.AsNoTracking().Where(o => o.TaskId == task.Id && o.Active).ToListAsync(ct);
        if (publications.Count != 1) return null;
        var op = publications[0];
        // No mapping is inferred from a caller SHA, equal path or task-branch push. An ambiguous
        // historical attempt stays unproved: publication must postdate this binding and terminal execution.
        return op.Id == id && new AgentTaskLandingState().HasPublication(op)
            && op.CreatedAt >= binding.CreatedAt && task.CompletedAt is { } completed && op.CreatedAt >= completed
            && op.SourceFullRef == binding.SourceFullRef && op.TargetFullRef == WorkspaceReservationKey.NormalizeRef(task.MergeTargetRef ?? "master")
            && op.RemoteFingerprint == fingerprint && op.OriginalSourceSha == head
            && WorkspaceReservationKey.PathsEqual(op.RepositoryPath, endpoint.Target.RepositoryPath)
            && WorkspaceReservationKey.PathsEqual(op.WorktreePath, endpoint.Target.WorktreePath)
            && op.RebasedSourceSha == op.VerifiedSourceSha && Oid(op.RebasedSourceSha)
            ? op : null;
    }

    private async Task<CompletedCardWorkspaceRemovalResponse> SendAsync(CardWorktreeCleanupEndpoint endpoint,
        CompletedCardWorkspaceCommand command, ICompletedCardWorkspaceClient client, CancellationToken ct)
    {
        if (endpoint.RunnerStoreId != command.Binding.RunnerStoreId || endpoint.RunnerId != command.Binding.RunnerId
            || runners.GetLiveStoreId(command.Binding.RunnerId)?.ToString("D") != command.Binding.RunnerStoreId) return Hold("runner_store_mismatch");
        var response = await client.RemoveCompletedCardWorkspaceAsync(command, ct);
        if (!response.Complete) return await RefuseAsync(endpoint, response.Reason ?? "receipt_mismatch", ct);
        var b = command.Binding;
        var expected = new CompletedCardWorkspaceReceipt(command.SchemaVersion, command.CardId, command.DoneRevisionId,
            command.TargetId, command.EndpointId, command.OperationId, b.TaskId, b.Attempt, b.BindingId, b.PreparationId,
            b.RunnerId, b.RunnerStoreId, b.RepositoryIdentity, b.CommonDirectory, b.GitDirectory, b.WorktreePath,
            b.SourceFullRef, b.CreationBaselineSha, command.Proof.ExpectedHeadSha,
            CompletedCardWorkspaceContract.CanonicalDigest(JsonSerializer.Serialize(command.Proof, PhoneHomeFraming.Json)),
            command.ArtifactProofDigest, command.DisposalPolicyDigest, command.Disposition,
            b.WorktreePath + ".cleanup-" + command.OperationId.ToString("N"), command.OperationId.ToString("D") + ":" + endpoint.RequestDigest, true, true);
        if (response.Reason is not null || response.Receipt != expected) return await RefuseAsync(endpoint, "receipt_mismatch", ct);
        endpoint.ImportedReceipt = JsonSerializer.SerializeToUtf8Bytes(response.Receipt, PhoneHomeFraming.Json);
        endpoint.DirectoryRemoved = true; endpoint.RegistrationRemoved = true; endpoint.BranchRemoved = false;
        endpoint.State = CardWorktreeCleanupEndpointState.Complete; endpoint.Reason = null;
        endpoint.UpdatedAt = clock.GetUtcNow().UtcDateTime; endpoint.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return response;
    }

    private async Task<CompletedCardWorkspaceRemovalResponse> RefuseAsync(CardWorktreeCleanupEndpoint endpoint, string reason, CancellationToken ct)
    {
        endpoint.State = reason == "done_generation_revoked" && endpoint.OperationId is null
            ? CardWorktreeCleanupEndpointState.Revoked : CardWorktreeCleanupEndpointState.Refused;
        endpoint.Reason = reason; endpoint.UpdatedAt = clock.GetUtcNow().UtcDateTime; endpoint.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct); return Hold(reason);
    }
    private static CompletedCardWorkspaceRemovalResponse Hold(string reason) => new(false, reason, null);
    private static bool Oid(string? value) => value?.Length is 40 or 64 && value.All(char.IsAsciiHexDigit);
    private static CompletedCardWorkspaceBinding Binding(TaskWorktreeMirrorBinding b) => new(b.TaskId, b.Attempt, b.BindingId,
        b.PreparationId, b.RunnerId, b.RunnerStoreId, b.RepositoryIdentity, b.CommonDirectory, b.GitDirectory,
        b.WorktreePath, b.SourceFullRef, b.CreationBaselineSha);
    private static CompletedCardWorkspaceCommand? ReadCommand(CardWorktreeCleanupEndpoint endpoint)
    {
        try
        {
            if (endpoint.ImmutableRequest is null) return null;
            var json = Encoding.UTF8.GetString(endpoint.ImmutableRequest);
            if (!CompletedCardWorkspaceContract.Validate(json).IsValid || CompletedCardWorkspaceContract.CanonicalDigest(json) != endpoint.RequestDigest) return null;
            var command = JsonSerializer.Deserialize<CompletedCardWorkspaceCommand>(json, PhoneHomeFraming.Json);
            return command?.OperationId == endpoint.OperationId && command.EndpointId == endpoint.Id
                && command.TargetId == endpoint.TargetId ? command : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }
    }
}
