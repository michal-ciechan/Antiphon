using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;

namespace Antiphon.Server.Application.Services;

/// <summary>One admitted immutable read source. No fetch, file fallback, or branch-relative content.</summary>
internal sealed record RefOnlyCommittedSource(TaskWorkspaceContext Workspace, string BaselineSha, string Sha)
{
    public static bool BaselineMatches(AgentTask task, TaskWorkspaceContext context, ProgressBaselineSnapshot? snapshot) =>
        snapshot is { SchemaVersion: 2 } && context.Kind == DesktopWorkspaceKind.RefOnly
        && context.PreparationState == TaskRefWorkspacePreparationState.Published
        && snapshot.Primary.DesktopWorkspaceKind == DesktopWorkspaceKind.RefOnly
        && snapshot.Primary.TaskRefWorkspaceBindingId == context.BindingId
        && snapshot.Primary.OwnerTaskId == task.Id && snapshot.Primary.RegisteredCheckout is null
        && snapshot.Primary.FullRef == context.FullRef && snapshot.Primary.LocalSha == context.BaseSha
        && GitObjectId.IsFull(context.BaseSha)
        && WorkspaceReservationKey.PathsEqual(snapshot.Primary.CanonicalRepository, context.GitWorkingDirectory)
        && WorkspaceReservationKey.PathsEqual(snapshot.Primary.CanonicalCommonDirectory, context.CommonDirectory!);

    public static async Task<RefOnlyCommittedSource?> ResolveAsync(AppDbContext db, GitWorkspaceService git,
        AgentTask task, CancellationToken ct)
    {
        if (task.DesktopWorkspaceKind != DesktopWorkspaceKind.RefOnly) return null;
        try
        {
            var context = await new TaskWorkspaceContextResolver(db).ResolveCurrentAsync(task, ct);
            var baseline = TaskProgressJson.TryReadBaseline(task.ProgressBaselineJson);
            if (!BaselineMatches(task, context, baseline)) return null;
            var evidence = TaskProgressJson.TryReadEvidence(task.CompletionProgressEvidenceJson)?.RemoteSync;
            if (evidence is null || evidence.Attempt != task.Attempt
                || evidence.DesktopWorkspaceKind != context.Kind || evidence.TaskRefWorkspaceBindingId != context.BindingId
                || evidence.FullRef != context.FullRef || !GitObjectId.IsFull(evidence.ObservedSha)) return null;
            var sha = evidence.ObservedSha!;
            if (evidence.State is RemoteSettlementSyncState.Synchronized or RemoteSettlementSyncState.NoPushedProgress)
            {
                if (evidence.ConfirmedSha != sha) return null;
            }
            else if (evidence.State == RemoteSettlementSyncState.Pending)
            {
                if (evidence.ConfirmedSha is not null || evidence.SourceDescends != true) return null;
            }
            else return null;
            var repo = context.GitWorkingDirectory;
            var (commonCode, common, _) = await git.RunReadOnlyAsync(repo, ct, "rev-parse", "--path-format=absolute", "--git-common-dir");
            if (commonCode != 0) return null;
            TaskWorkspaceContextResolver.RequireRepositoryIdentity(context, common.Trim());
            var (present, _, _) = await git.RunReadOnlyAsync(repo, ct, "cat-file", "-e", sha + "^{commit}");
            if (present != 0) return null;
            var (descends, _, _) = await git.RunReadOnlyAsync(repo, ct, "merge-base", "--is-ancestor", baseline!.Primary.LocalSha, sha);
            if (descends != 0) return null;
            // Settlement's retained pin is durable read custody. A moving task ref is not a
            // replacement for an admitted Pending pin, and a missing pin never becomes disk.
            var pin = evidence.ObservationRef;
            if (pin is null || !pin.StartsWith("refs/antiphon/progress/" + task.Id.ToString("N") + "/", StringComparison.Ordinal))
                pin = RemoteWorkspaceService.SettlementPin(task.Id);
            var pinned = await git.RevParseAsync(repo, pin + "^{commit}", ct);
            if (pinned != sha) return null;
            return new(context, baseline.Primary.LocalSha, sha);
        }
        catch (ConflictException) { return null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
