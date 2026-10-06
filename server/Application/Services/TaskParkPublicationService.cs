using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Dormant S4 source policy. Short task-before-session transactions surround external I/O.
/// Publication is evidence, not permission to release, autosave, launch a child or settle a task.
/// </summary>
public sealed class TaskParkPublicationService(AppDbContext db, LocalTaskParkPublisher local,
    ISessionRunnerDirectory runners, IWorkspaceReservationJournal reservations, TimeProvider clock,
    IOptions<BlockedTaskParkingOptions> options)
{
    // Pause only at the real receipt persistence boundary; never substitutes eligibility or I/O.
    internal Func<TaskParkPublicationEvidence, CancellationToken, Task>? BeforeReceiptSaveAsync { get; init; }
    /// <param name="remoteRepositoryIdentity">The bound runner's canonical common-directory
    /// identity, captured by its workspace owner. Never substitute the desktop common directory.
    /// Missing identity holds. S5 owns caller composition; this service has no automatic caller.</param>
    public async Task<TaskParkPublicationResult> PrepareAsync(Guid parkId, string? remoteRepositoryIdentity,
        CancellationToken ct)
    {
        if (!options.Value.Enabled || !CanOwnTransaction()) return Held("park_disabled_or_busy");
        var candidate = await LoadAsync(parkId, ct);
        if (candidate is null) return Held("park_episode_changed");
        var (task, park, baseline) = candidate;
        if (await RefusalAsync(task, park, baseline, ct) is { } refusal) return await HoldAsync(park, refusal, ct);
        var binding = await runners.GetBindingAsync(park.SessionId!.Value, ct);
        var remote = binding is SessionRunnerBinding.Remote;
        if (binding is SessionRunnerBinding.Missing) return await HoldAsync(park, "park_runner_unknown", ct);
        if (!remote && !string.IsNullOrEmpty(park.RemoteWorktreePath))
            return await HoldAsync(park, "park_runner_changed", ct);
        if (remote && (task.Workspace != WorkspaceMode.Worktree
            || binding is not SessionRunnerBinding.Remote r || r.Owner.RunnerId != park.RunnerId
            || r.Owner.RunnerStoreId != park.RunnerStoreId || r.Owner.RunnerCwd != park.RemoteWorktreePath))
            return await HoldAsync(park, "park_runner_changed", ct);
        var identity = remote ? remoteRepositoryIdentity : LocalTaskParkPublisher.Identity(baseline.CanonicalCommonDirectory);
        if (string.IsNullOrWhiteSpace(identity)) return await HoldAsync(park, "park_repository_unknown", ct);
        var endpoint = baseline.Remote.EndpointFingerprint;
        if (string.IsNullOrWhiteSpace(endpoint) && task.Workspace != WorkspaceMode.ReadOnly)
            return await HoldAsync(park, "park_endpoint_unknown", ct);
        endpoint ??= "no_remote_required";
        if (park.RepositoryIdentity is not null && (park.RepositoryIdentity != identity || park.EndpointFingerprint != endpoint))
            return await HoldAsync(park, "park_source_intent_changed", ct);

        // This durable intent precedes the first Git or wire mutation, and survives lost ACKs.
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var current = await LockAndLoadAsync(parkId, ct);
            if (current is null || current.Park.Revision != park.Revision || !SameEpisode(current.Task, current.Park))
                return Held("park_episode_changed");
            if (current.Task.ProgressBaselineJson != task.ProgressBaselineJson
                || await RefusalAsync(current.Task, current.Park, current.Baseline, ct) is not null)
                return Held("park_policy_changed");
            if (current.Park.State == AgentTaskParkState.Published)
                return Held("park_already_published");
            if (current.Park.State is not (AgentTaskParkState.Requested or AgentTaskParkState.Held)
                || current.Park.PublicationReceiptId is not null) return Held("park_state_changed");
            var changed = await db.AgentTaskParks.Where(p => p.Id == parkId && p.Revision == park.Revision)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.RepositoryIdentity, identity)
                    .SetProperty(p => p.EndpointFingerprint, endpoint)
                    .SetProperty(p => p.State, AgentTaskParkState.Requested)
                    .SetProperty(p => p.HeldFromState, (AgentTaskParkState?)null)
                    .SetProperty(p => p.ReasonCode, "park_publication_requested")
                    .SetProperty(p => p.Revision, p => p.Revision + 1), ct);
            if (changed != 1) return Held("park_episode_changed");
            await tx.CommitAsync(ct);
        }
        park.RepositoryIdentity = identity;
        park.EndpointFingerprint = endpoint;
        park.Revision++;
        var request = Request(park, remote);
        var key = WorkspaceReservationKey.For(park.WorktreePath, park.FullRef, baseline.CanonicalRepository);
        var admitted = await reservations.TryAdmitConsumerAsync(new(key, WorkspaceReservationKind.Launch,
            park.TaskId, park.SessionId), ct);
        if (!admitted.Accepted || admitted.Snapshot is null) return await HoldAsync(park, "park_workspace_reserved", ct);
        try
        {
            if (await RefusalAsync(task, park, baseline, ct) is { } finalRefusal)
                return await HoldAsync(park, finalRefusal, ct);
            var result = await InspectAsync(task, park, baseline, request, remote, null, ct);
            if (result.Evidence is null) return await HoldAsync(park, result.Reason, ct, result.Outcome);
            if (BeforeReceiptSaveAsync is not null) await BeforeReceiptSaveAsync(result.Evidence, ct);
            return await AcceptAsync(parkId, result.Evidence, ct)
                ? result : Held("park_receipt_changed");
        }
        finally
        {
            await reservations.ReleaseConsumerAsync(admitted.Snapshot.Id, admitted.Snapshot.Generation, CancellationToken.None);
        }
    }

    /// <summary>Fresh, read-only source verification of a durable receipt, for the future coordinator.</summary>
    public async Task<TaskParkPublicationResult> VerifyAsync(Guid parkId, CancellationToken ct)
    {
        if (!CanOwnTransaction()) return Held("park_busy");
        var c = await LoadAsync(parkId, ct);
        if (c is null || c.Park.PublicationReceiptId is null) return Held("park_receipt_missing");
        var binding = await runners.GetBindingAsync(c.Park.SessionId!.Value, ct);
        var remote = binding is SessionRunnerBinding.Remote;
        var kind = c.Park.VerifiedRemoteSha is null ? TaskParkPublicationOutcome.NoSourceChanges : TaskParkPublicationOutcome.Published;
        var evidence = new TaskParkPublicationEvidence(c.Park.PublicationReceiptId.Value, Request(c.Park, remote),
            c.Park.SourceSha!, c.Park.VerifiedRemoteSha, true, true, kind);
        if (Digest(evidence) != c.Park.PublicationReceiptDigest) return Held("park_receipt_changed");
        return await AcceptAsync(parkId, evidence, ct)
            ? new(kind, kind == TaskParkPublicationOutcome.Published ? "park_published" : "park_no_source_changes", evidence)
            : Held("park_receipt_changed");
    }

    /// <summary>Never trusts a receipt against itself: compare persisted intent, verify real source,
    /// then compare the episode again under task/session locks. Tests exercise this actual CAS.</summary>
    internal async Task<bool> AcceptAsync(Guid parkId, TaskParkPublicationEvidence evidence, CancellationToken ct)
    {
        if (!CanOwnTransaction()) return false;
        var c = await LoadAsync(parkId, ct);
        if (c is null || c.Park.RepositoryIdentity is null || c.Park.EndpointFingerprint is null
            || await RefusalAsync(c.Task, c.Park, c.Baseline, ct) is not null) return false;
        var binding = await runners.GetBindingAsync(c.Park.SessionId!.Value, ct);
        if (binding is SessionRunnerBinding.Missing) return false;
        var remote = binding is SessionRunnerBinding.Remote;
        if (evidence.Request != Request(c.Park, remote) || !evidence.Clean || !evidence.DescendsFromBaseline
            || evidence.ReceiptId == Guid.Empty || evidence.Outcome is not (TaskParkPublicationOutcome.Published or TaskParkPublicationOutcome.NoSourceChanges)
            || c.Park.PublicationReceiptId is not null && Digest(evidence) != c.Park.PublicationReceiptDigest) return false;
        var fresh = await InspectAsync(c.Task, c.Park, c.Baseline, evidence.Request, remote, evidence, ct);
        if (fresh.Evidence != evidence) return false;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var current = await LockAndLoadAsync(parkId, ct);
        if (current is null || current.Park.Revision != c.Park.Revision || !SameEpisode(current.Task, current.Park)
            || current.Task.ProgressBaselineJson != c.Task.ProgressBaselineJson
            || current.Task.CommitOnSettle != c.Task.CommitOnSettle || current.Task.SourceLandingOperationId is not null
            || await RefusalAsync(current.Task, current.Park, current.Baseline, ct) is not null) return false;
        if (current.Park.PublicationReceiptId is not null)
            return current.Park.PublicationReceiptDigest == Digest(evidence);
        if (current.Park.State != AgentTaskParkState.Requested) return false;
        var now = clock.GetUtcNow().UtcDateTime;
        var changed = await db.AgentTaskParks.Where(p => p.Id == parkId && p.Revision == c.Park.Revision
                && p.State == AgentTaskParkState.Requested && p.PublicationReceiptId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.PublicationReceiptId, evidence.ReceiptId)
                .SetProperty(p => p.PublicationReceiptDigest, Digest(evidence))
                .SetProperty(p => p.SourceSha, evidence.SourceSha).SetProperty(p => p.VerifiedRemoteSha, evidence.RemoteSha)
                .SetProperty(p => p.State, AgentTaskParkState.Published).SetProperty(p => p.ReasonCode, fresh.Reason)
                .SetProperty(p => p.PublishedAt, now).SetProperty(p => p.UpdatedAt, now)
                .SetProperty(p => p.Revision, p => p.Revision + 1), ct);
        await tx.CommitAsync(ct);
        return changed == 1;
    }

    private async Task<TaskParkPublicationResult> InspectAsync(AgentTask task, AgentTaskPark park,
        ProgressSourceBaseline baseline, WorkspaceParkRequest request, bool remote,
        TaskParkPublicationEvidence? previous, CancellationToken ct)
    {
        if (!remote) return await local.InspectAsync(request, task.Workspace, baseline.CanonicalRepository, previous, ct);
        var owner = await runners.GetBindingAsync(park.SessionId!.Value, ct);
        if (owner is not SessionRunnerBinding.Remote r || r.Owner.RunnerId != park.RunnerId
            || r.Owner.RunnerStoreId != park.RunnerStoreId || r.Owner.RunnerCwd != park.RemoteWorktreePath
            || !WorkspaceParkCommand.Supported((await runners.DescribeAsync(park.RunnerId, ct))?.Capabilities))
            return Held("park_runner_changed");
        var result = await runners.Resolve(park.RunnerId).ParkWorkspaceAsync(previous is null
            ? new(park.SessionId.Value, Prepare: request) : new(park.SessionId.Value, Verify: previous.ToRunnerReceipt()), ct);
        return result.Outcome == WorkspaceParkOutcome.Published && result.Receipt is { } receipt
            ? new(TaskParkPublicationOutcome.Published, "park_published", TaskParkPublicationEvidence.From(receipt))
            : new(result.Outcome == WorkspaceParkOutcome.Unknown ? TaskParkPublicationOutcome.Unknown : TaskParkPublicationOutcome.Held,
                "park_runner_refused");
    }

    private async Task<string?> RefusalAsync(AgentTask task, AgentTaskPark park, ProgressSourceBaseline baseline, CancellationToken ct)
    {
        if (!SameEpisode(task, park)) return "park_episode_changed";
        if (task.SourceLandingOperationId is not null || task.Role == AgentTaskRole.Mutation) return "park_source_landing";
        if (task.CommitOnSettle == CommitOnSettlePolicy.Never) return "park_no_commit";
        if (await CommitRecoveryObligations.LoadUnresolvedAsync(db, task.Id, ct) is { Count: > 0 }) return "park_commit_recovery";
        if (string.IsNullOrWhiteSpace(baseline.CanonicalRepository) || string.IsNullOrWhiteSpace(baseline.CanonicalCommonDirectory)
            || !Antiphon.Server.Domain.GitObjectId.IsFull(park.BaselineSha)
            || park.FullRef?.StartsWith("refs/heads/", StringComparison.Ordinal) != true
            || baseline.OwnerTaskId != task.Id || baseline.FullRef != park.FullRef || baseline.LocalSha != park.BaselineSha
            || !WorkspaceReservationKey.PathsEqual(baseline.RegisteredCheckout ?? "", park.WorktreePath ?? ""))
            return "park_ownership_unknown";
        var key = WorkspaceReservationKey.For(park.WorktreePath, park.FullRef, baseline.CanonicalRepository);
        if ((await reservations.ReadActiveAsync(key, ct)).Any(r => r.Kind != WorkspaceReservationKind.Launch
            || r.TaskId != task.Id || r.SessionId != park.SessionId)) return "park_other_writer";
        var others = await db.AgentTasks.AsNoTracking().Where(t => t.Id != task.Id
            && t.Status != AgentTaskStatus.Succeeded && t.Status != AgentTaskStatus.Failed && t.Status != AgentTaskStatus.Canceled)
            .ToListAsync(ct);
        if (others.Any(t => t.AgentSessionId == park.SessionId || WorkspaceReservationKey.PathsEqual(
            t.WorktreePath ?? t.WorkingDirectory ?? t.RepoPath ?? "", park.WorktreePath ?? ""))) return "park_other_writer";
        var sessions = await db.AgentSessions.AsNoTracking().Where(s => s.Id != park.SessionId
            && s.Status != SessionStatus.Stopped && s.Status != SessionStatus.Failed).ToListAsync(ct);
        if (sessions.Any(s => WorkspaceReservationKey.PathsEqual(s.Cwd, park.WorktreePath ?? "")
            || park.RemoteWorktreePath is not null && s.RunnerId == park.RunnerId
                && s.RunnerCwd == park.RemoteWorktreePath)) return "park_other_writer";
        return null;
    }

    private sealed record Candidate(AgentTask Task, AgentTaskPark Park, ProgressSourceBaseline Baseline);
    private async Task<Candidate?> LoadAsync(Guid id, CancellationToken ct)
    {
        var park = await db.AgentTaskParks.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        if (park is null || park.AgentId is null || park.SessionId is null || park.RunnerStoreId is null
            || park.AcceptedStartedAt is null || park.RunnerId is null || park.ReportDigest is null) return null;
        var task = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == park.TaskId, ct);
        var baseline = TaskProgressJson.TryReadBaseline(task?.ProgressBaselineJson);
        if (task is null || baseline?.SchemaVersion != 1) return null;
        var session = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == park.SessionId, ct);
        var block = await db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == park.TaskId && e.Type == AgentTaskEventType.Blocked)
            .OrderByDescending(e => e.At).ThenByDescending(e => e.Id).Select(e => e.Id).FirstOrDefaultAsync(ct);
        return session is not null && session.RunnerId == park.RunnerId && session.RunnerStoreId == park.RunnerStoreId
            && session.StartedAt == park.AcceptedStartedAt && block == park.BlockEventId && SameEpisode(task, park)
            ? new(task, park, baseline.Primary) : null;
    }

    private async Task<Candidate?> LockAndLoadAsync(Guid id, CancellationToken ct)
    {
        var park = await db.AgentTaskParks.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        if (park is null) return null;
        await db.Database.ExecuteSqlInterpolatedAsync($"""SELECT "Id" FROM "AgentTasks" WHERE "Id" = {park.TaskId} FOR UPDATE""", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""SELECT "Id" FROM "AgentSessions" WHERE "Id" = {park.SessionId} FOR UPDATE""", ct);
        return await LoadAsync(id, ct);
    }

    private async Task<TaskParkPublicationResult> HoldAsync(AgentTaskPark park, string reason, CancellationToken ct,
        TaskParkPublicationOutcome outcome = TaskParkPublicationOutcome.Held)
    {
        await new BlockedTaskParkingService(db, clock, options).PersistStateAsync(park.Id, park.Revision,
            AgentTaskParkState.Requested, AgentTaskParkState.Held, reason, ct);
        return new(outcome, reason);
    }

    private static bool SameEpisode(AgentTask t, AgentTaskPark p) => t.Status == AgentTaskStatus.Blocked
        && t.Attempt == p.Attempt && t.ConcurrencyToken == p.TaskConcurrencyToken && t.AgentId == p.AgentId
        && t.AgentSessionId == p.SessionId && t.RunnerId == p.RunnerId && t.Workspace == p.Workspace
        && t.WorktreeId == p.WorktreeId && (t.WorktreePath ?? t.WorkingDirectory ?? t.RepoPath) == p.WorktreePath
        && t.RemoteWorktreePath == p.RemoteWorktreePath && t.ResultFilePath == p.ReportReference
        && (t.WorktreeBranch is null || "refs/heads/" + t.WorktreeBranch == p.FullRef)
        && (t.WorktreeBaseSha is null || t.WorktreeBaseSha == p.BaselineSha)
        && BlockedTaskParkingService.Digest(t.Result) == p.ReportDigest;
    private static WorkspaceParkRequest Request(AgentTaskPark p, bool remote) => new(
        (remote ? p.RemoteWorktreePath : p.WorktreePath)!, new(p.Id, p.Id, p.TaskId, p.Attempt, p.BlockEventId,
            p.AgentId!.Value, p.RunnerId!, p.RunnerStoreId!.Value, p.SessionId!.Value, p.AcceptedStartedAt!.Value,
            p.TaskConcurrencyToken, p.ReportDigest!, p.RepositoryIdentity!, p.EndpointFingerprint!, p.FullRef!, p.BaselineSha!));
    private static string Digest(TaskParkPublicationEvidence evidence) => BlockedTaskParkingService.Digest(JsonSerializer.Serialize(evidence))!;
    private bool CanOwnTransaction() => db.Database.CurrentTransaction is null
        && System.Transactions.Transaction.Current is null && !db.ChangeTracker.HasChanges();
    private static TaskParkPublicationResult Held(string reason) => new(TaskParkPublicationOutcome.Held, reason);
}
