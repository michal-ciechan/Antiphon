using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;

namespace Antiphon.Server.Application.Services;

/// <summary>The accepted ref workspace episode, persisted with the debt, never reconstructed from disk.</summary>
internal sealed record SettlementSyncWorkspaceSnapshot(
    int Version, Guid TaskId, int Attempt, Guid TaskRevision,
    DesktopWorkspaceKind Kind, Guid BindingId, long BindingRevision,
    string BaselineJson, string RepositoryPath, string IdentityPath,
    string RunnerId, string RemoteWorktreePath)
{
    public static async Task<string?> CaptureAsync(AppDbContext db, AgentTask task, CancellationToken ct)
    {
        if (task.DesktopWorkspaceKind != DesktopWorkspaceKind.RefOnly) return null;
        var context = await new TaskWorkspaceContextResolver(db).ResolveCurrentAsync(task, ct);
        if (context.PreparationState != TaskRefWorkspacePreparationState.Published
            || context.BindingId is not Guid binding || context.BindingRevision is not long revision
            || string.IsNullOrWhiteSpace(task.ProgressBaselineJson)
            || string.IsNullOrWhiteSpace(task.RunnerId) || string.IsNullOrWhiteSpace(task.RemoteWorktreePath))
            throw new InvalidOperationException("ref_only_debt_snapshot_unavailable");
        return JsonSerializer.Serialize(new SettlementSyncWorkspaceSnapshot(1, task.Id, task.Attempt,
            task.ConcurrencyToken, context.Kind, binding, revision, task.ProgressBaselineJson,
            context.GitWorkingDirectory, context.IdentityPath, task.RunnerId, task.RemoteWorktreePath));
    }

    public static async Task<bool> MatchesAsync(AppDbContext db, AgentTask task, AgentTaskSyncDebt debt,
        CancellationToken ct)
    {
        // An existing Checkout debt has no snapshot and keeps its legacy contract. A snapshot
        // cannot become Checkout debt by changing the live task's mode or binding.
        if (debt.WorkspaceSnapshotJson is null)
            return task.DesktopWorkspaceKind == DesktopWorkspaceKind.Checkout && task.TaskRefWorkspaceBindingId is null;
        SettlementSyncWorkspaceSnapshot? snapshot;
        try { snapshot = JsonSerializer.Deserialize<SettlementSyncWorkspaceSnapshot>(debt.WorkspaceSnapshotJson); }
        catch (JsonException) { return false; }
        if (snapshot is null || snapshot.Version != 1 || snapshot.Kind != DesktopWorkspaceKind.RefOnly
            || task.DesktopWorkspaceKind != snapshot.Kind || task.Id != snapshot.TaskId
            || task.Attempt != snapshot.Attempt || task.ConcurrencyToken != snapshot.TaskRevision
            || task.TaskRefWorkspaceBindingId != snapshot.BindingId
            || task.ProgressBaselineJson != snapshot.BaselineJson || task.RepoPath != snapshot.RepositoryPath
            || task.WorktreePath != snapshot.IdentityPath || task.RunnerId != snapshot.RunnerId
            || task.RemoteWorktreePath != snapshot.RemoteWorktreePath
            || debt.RepositoryPath != snapshot.RepositoryPath || debt.WorktreePath != snapshot.IdentityPath
            || debt.RunnerId != snapshot.RunnerId || debt.RemoteWorktreePath != snapshot.RemoteWorktreePath)
            return false;
        var baseline = TaskProgressJson.TryReadBaseline(snapshot.BaselineJson);
        if (baseline is not { SchemaVersion: 2 } || baseline.Primary.DesktopWorkspaceKind != snapshot.Kind
            || baseline.Primary.TaskRefWorkspaceBindingId != snapshot.BindingId
            || baseline.Primary.LocalSha != debt.BaselineSha || baseline.Primary.FullRef != debt.FullRef
            || baseline.Primary.Remote.EndpointFingerprint != debt.EndpointFingerprint)
            return false;
        try
        {
            var current = await new TaskWorkspaceContextResolver(db).ResolveCurrentAsync(task, ct);
            return current.PreparationState == TaskRefWorkspacePreparationState.Published
                && current.BindingRevision == snapshot.BindingRevision;
        }
        catch (ConflictException) { return false; }
    }
}
