using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0544 D-1/D-2/D-3/D-5/D-7. Admission rules for an explicitly requested Interim round. Omitted
/// or Final requests never reach anything here beyond <see cref="ResolveRound"/>: they need no
/// policy, baseline, selection or nightly readiness. Every Interim gate fails closed with a stable
/// code and never silently turns the request into a different round.
///
/// <para>Gate order (each one-field-invalid request stops at its own gate): request shape (422) →
/// explicit subject/baseline/selection (422) → card role policy (409) → subject links (409) →
/// owner landing exclusion (409) → baseline eligibility (409) → committed selection (409) →
/// deployment readiness (409). The owner latch is written by <see cref="LatchOwnerLockedAsync"/>
/// inside the creating transaction.</para>
/// </summary>
public sealed class InterimVerificationPolicy(
    AppDbContext db,
    IInterimVerificationReadinessReader readiness,
    TimeProvider clock,
    ILandingGit? git = null)
{
    public const int ProfileVersion = 1;

    public const string RoundRoleCode = "verification_round_role";
    public const string RoundInvalidCode = "verification_round_invalid";
    public const string InterimDisallowedCode = "verification_interim_disallowed";
    public const string BaselineInvalidCode = "verification_baseline_invalid";
    public const string BackstopUnreadyCode = "verification_backstop_unready";
    public const string OwnerLandingCode = "verification_owner_landing";
    public const string SelectionInvalidCode = "verification_selection_invalid";

    public static bool HasProfile(AgentTaskRole role) => role is AgentTaskRole.Code or AgentTaskRole.Review;

    /// <summary>
    /// D-4: an Interim task cannot relabel itself land-ready. A clean Interim Review asking for
    /// <c>land</c> is routed to a Final Review; every other handoff is unchanged.
    /// </summary>
    public static PipelineHandoff.Result CapHandoff(AgentTask task, PipelineHandoff.Result parsed) =>
        task.VerificationRound == VerificationRound.Interim && parsed.Kind == PipelineHandoffKind.Land
            ? parsed with { Kind = PipelineHandoffKind.Review }
            : parsed;

    /// <summary>
    /// Syntax gate, run before anything else about the request is resolved. Returns the round a
    /// Code/Review task is commissioned with (Final when omitted) and null for every other role.
    /// </summary>
    public static VerificationRound? ResolveRound(CreateAgentTaskRequest request)
    {
        if (request.VerificationRound is { } asked && !Enum.IsDefined(asked))
            throw new ValidationException("verificationRound", "verificationRound must be Final or Interim.", RoundInvalidCode);
        var profileFields = request.VerificationSubjectTaskId is not null || request.VerificationBaselineOutcomeId is not null
            || request.VerificationSelection is not null;
        if (!HasProfile(request.Role))
        {
            if (request.VerificationRound is not null || profileFields)
                throw new ValidationException("verificationRound",
                    $"A verification round applies only to Code and Review; {request.Role} has no ordinary-round profile.",
                    RoundRoleCode);
            return null;
        }

        var round = request.VerificationRound ?? VerificationRound.Final;
        if (round == VerificationRound.Final && profileFields)
            throw new ValidationException("verificationRound",
                "verificationSubjectTaskId, verificationBaselineOutcomeId and verificationSelection are Interim-only.",
                RoundRoleCode);
        return round;
    }

    /// <summary>Initial Interim support is Worker Code/Worktree and Worker Review/ReadOnly only.</summary>
    public static void RequireInterimShape(AgentTaskKind kind, AgentTaskRole role, WorkspaceMode workspace)
    {
        var supported = kind == AgentTaskKind.Worker
            && (role == AgentTaskRole.Code && workspace == WorkspaceMode.Worktree
                || role == AgentTaskRole.Review && workspace == WorkspaceMode.ReadOnly);
        if (!supported)
            throw new ValidationException("verificationRound",
                $"Interim is supported only for Worker Code/Worktree and Worker Review/ReadOnly (got {kind} {role}/{workspace}).",
                RoundRoleCode);
    }

    /// <summary>Explicit identities are required; the latest same-card outcome is never inferred.</summary>
    public static void RequireExplicitIdentities(Guid? subject, Guid? baseline, VerificationSelectionReference? selection)
    {
        if (subject is null || subject == Guid.Empty || baseline is null || baseline == Guid.Empty)
            throw new ValidationException("verificationBaselineOutcomeId",
                "Interim requires an explicit verificationSubjectTaskId and a full-scope verificationBaselineOutcomeId.",
                BaselineInvalidCode);
        ValidateSelectionShape(selection);
    }

    public sealed record AdmissionInput(
        AgentTaskKind Kind,
        AgentTaskRole Role,
        WorkspaceMode Workspace,
        Guid? CardId,
        Guid? ProjectId,
        string? RepoPath,
        Guid? RepairSourceTaskId,
        Guid? FollowUpOfTaskId,
        Guid? SubjectTaskId,
        Guid? BaselineOutcomeId,
        VerificationSelectionReference? Selection);

    /// <summary>
    /// Every Interim gate after the request shape. Throws the first refusal; returns the immutable
    /// admission snapshot on success. The owner latch is not written here.
    /// </summary>
    public async Task<VerificationAdmission> AdmitAsync(AdmissionInput input, CancellationToken ct)
    {
        RequireInterimShape(input.Kind, input.Role, input.Workspace);
        RequireExplicitIdentities(input.SubjectTaskId, input.BaselineOutcomeId, input.Selection);
        var card = await RequireCardPolicyAsync(input.CardId, input.Role, ct);
        var subject = await RequireSubjectAsync(input, card.Id, ct);
        await RequireOwnerNotLandingAsync(subject, ct);
        var baseline = await RequireBaselineAsync(input.BaselineOutcomeId!.Value, subject, card.Id, input.ProjectId, input.RepoPath, ct);
        var selection = await RequireCommittedSelectionAsync(input.Selection!, input.RepoPath, ct);
        var ready = await RequireReadyAsync(input.RepoPath, input.ProjectId, ct);
        return new VerificationAdmission(
            ProfileVersion, VerificationRound.Interim, subject.Id, baseline.Id, baseline.ReviewedSourceSha!,
            card.Id, card.RevisionCount, input.Role == AgentTaskRole.Code ? card.CodeVerificationPolicy : card.ReviewVerificationPolicy,
            selection, ready, clock.GetUtcNow().UtcDateTime);
    }

    /// <summary>
    /// D-7 queued recheck before launch: card policy, baseline eligibility and readiness again.
    /// Returns a stable hold reason, or null when the task may launch. Never throws for a refusal.
    /// </summary>
    public async Task<string?> RecheckQueuedAsync(AgentTask task, CancellationToken ct)
    {
        try
        {
            var card = await RequireCardPolicyAsync(task.CardId, task.Role, ct);
            if (task.VerificationSubjectTaskId is not Guid subjectId || task.VerificationBaselineOutcomeId is not Guid baselineId)
                return BaselineInvalidCode + ": admitted identities are missing";
            var subject = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == subjectId, ct);
            if (subject is null)
                return BaselineInvalidCode + ": subject is missing";
            await RequireBaselineAsync(baselineId, subject, card.Id, task.ProjectId, task.RepoPath, ct);
            await RequireReadyAsync(task.RepoPath, task.ProjectId, ct);
            return null;
        }
        catch (HttpException ex) when (ex.Code is not null)
        {
            return $"{ex.Code}: {ex.Message}";
        }
    }

    /// <summary>
    /// D-5 owner latch. Must run inside the creating transaction: the owner row is locked with the
    /// same <c>FOR UPDATE</c> the land admission takes, the landing exclusion is re-read under it,
    /// and the latch is set on the tracked row so it commits with the task or not at all.
    /// </summary>
    public async Task<AgentTask> LatchOwnerLockedAsync(Guid ownerId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("The owner latch must be written inside the admission transaction.");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AgentTasks\" WHERE \"Id\" = {ownerId} FOR UPDATE", ct);
        var owner = await db.AgentTasks.SingleAsync(t => t.Id == ownerId, ct);
        await db.Entry(owner).ReloadAsync(ct);
        await RequireOwnerNotLandingAsync(owner, ct);
        owner.RequiresFinalVerificationReview = true;
        owner.ConcurrencyToken = Guid.NewGuid();
        return owner;
    }

    private async Task<Card> RequireCardPolicyAsync(Guid? cardId, AgentTaskRole role, CancellationToken ct)
    {
        var card = cardId is Guid id ? await db.Cards.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct) : null;
        if (card is null)
            throw new ConflictException("Interim requires a card whose policy permits it; this task has no card.", InterimDisallowedCode);
        var policy = role == AgentTaskRole.Code ? card.CodeVerificationPolicy : card.ReviewVerificationPolicy;
        if (policy != CardVerificationPolicy.AllowInterim)
            throw new ConflictException(
                $"Card '{card.Identifier}' {role} verification policy is {policy}; request Final instead.", InterimDisallowedCode);
        return card;
    }

    private async Task<AgentTask> RequireSubjectAsync(AdmissionInput input, Guid cardId, CancellationToken ct)
    {
        var subjectId = input.SubjectTaskId!.Value;
        var subject = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == subjectId, ct)
            ?? throw Baseline("verification subject was not found");
        if (subject.Role != AgentTaskRole.Code || subject.Workspace != WorkspaceMode.Worktree
            || subject.RepairSourceTaskId is not null || string.IsNullOrWhiteSpace(subject.WorktreeBranch))
            throw Baseline("verification subject is not an original Code/Worktree landing owner");
        if (subject.CardId != cardId)
            throw Baseline("verification subject is on another card");
        if (subject.ProjectId != input.ProjectId)
            throw Baseline("verification subject is in another project");
        if (!SamePath(subject.RepoPath, input.RepoPath))
            throw Baseline("verification subject is in another repository");

        if (input.Role == AgentTaskRole.Code && input.RepairSourceTaskId != subjectId)
            throw Baseline("an Interim Code round must be a repair of the named subject (RepairSourceTaskId)");
        if (input.RepairSourceTaskId is Guid repairOwner && repairOwner != subjectId)
            throw Baseline("RepairSourceTaskId does not name the verification subject");
        if (input.FollowUpOfTaskId is Guid followed)
        {
            var prior = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == followed, ct)
                ?? throw Baseline("followed-up task was not found");
            var owner = prior.RepairSourceTaskId ?? prior.VerificationSubjectTaskId ?? prior.Id;
            if (owner != subjectId)
                throw Baseline("the followed-up task's original landing owner is not the verification subject");
        }
        return subject;
    }

    private async Task RequireOwnerNotLandingAsync(AgentTask owner, CancellationToken ct)
    {
        var pending = owner.LandRequestedAt is not null
            || await db.AgentTaskLandRequests.AsNoTracking().AnyAsync(r => r.TaskId == owner.Id && r.IsPending, ct);
        if (pending)
            throw new ConflictException("The landing owner has a pending land; Interim work cannot race it.", OwnerLandingCode);
        var landings = await db.AgentTaskLandings.AsNoTracking().Where(o => o.TaskId == owner.Id).ToListAsync(ct);
        var state = new AgentTaskLandingState();
        if (landings.Any(state.HasPublication))
            throw new ConflictException("The landing owner is already published; Interim work is closed.", OwnerLandingCode);
    }

    private async Task<StageOutcome> RequireBaselineAsync(Guid baselineId, AgentTask subject, Guid cardId, Guid? projectId,
        string? repoPath, CancellationToken ct)
    {
        var outcome = await db.StageOutcomes.AsNoTracking().SingleOrDefaultAsync(o => o.Id == baselineId, ct)
            ?? throw Baseline("baseline outcome was not found");
        if (outcome.Stage != OrchestrationStage.Review)
            throw Baseline("baseline is not a Review outcome");
        if (outcome.OrdinaryScopeCompleted != VerificationScope.Full || outcome.CommissionedRound != VerificationRound.Final
            || outcome.VerificationProfileVersion != ProfileVersion)
            throw Baseline("baseline did not complete Full scope under a Final profile");
        if (outcome.Source != StageOutcomeSource.Delegate)
            throw Baseline("baseline was not settled from a delegate Review report");
        if (outcome.Outcome is not (StageOutcomeKind.Clean or StageOutcomeKind.Found) || !GitObjectId.IsFull(outcome.ReviewedSourceSha))
            throw Baseline("baseline has no completed Clean/Found verdict with a reviewed SHA");
        var stageTask = outcome.StageTaskId is Guid stageTaskId
            ? await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == stageTaskId, ct)
            : null;
        if (stageTask is null || stageTask.Role != AgentTaskRole.Review || stageTask.Status != AgentTaskStatus.Succeeded
            || stageTask.CompletedAt is null)
            throw Baseline("baseline Review task did not complete successfully");
        if (outcome.SubjectTaskId != subject.Id)
            throw Baseline("baseline reviewed another landing owner");
        if (outcome.CardId != cardId)
            throw Baseline("baseline is on another card");
        if (!SamePath(outcome.ReviewedRepositoryPath, repoPath) || !SamePath(outcome.ReviewedRepositoryPath, subject.RepoPath))
            throw Baseline("baseline reviewed another repository");
        if (stageTask.ProjectId != projectId)
            throw Baseline("baseline Review ran in another project");
        if (await db.StageOutcomes.AsNoTracking().AnyAsync(o => o.SupersedesId == outcome.Id, ct))
            throw Baseline("baseline outcome has been superseded");
        return outcome;
    }

    internal static void ValidateSelectionShape(VerificationSelectionReference? selection)
    {
        if (selection is null)
            throw new ValidationException("verificationSelection", "Interim requires a committed selection reference.",
                SelectionInvalidCode);
        var path = selection.ArtifactPath;
        if (string.IsNullOrWhiteSpace(path) || path != path.Trim() || path.Contains('\\') || path.Contains(':')
            || path.StartsWith('/') || Path.IsPathRooted(path) || path.IndexOfAny(['\r', '\n', '\0', '*', '?']) >= 0)
            throw new ValidationException("verificationSelection.artifactPath",
                "artifactPath must be a repository-relative docs/**/*.md path.", SelectionInvalidCode);
        var segments = path.Split('/');
        if (segments.Length < 2 || segments[0] != "docs" || segments.Any(s => s.Length == 0 || s == "." || s == "..")
            || !path.EndsWith(".md", StringComparison.Ordinal))
            throw new ValidationException("verificationSelection.artifactPath",
                "artifactPath must be a repository-relative docs/**/*.md path.", SelectionInvalidCode);
        if (!GitObjectId.IsFull(selection.ArtifactCommitSha))
            throw new ValidationException("verificationSelection.artifactCommitSha",
                "artifactCommitSha must be a full 40- or 64-character object ID.", SelectionInvalidCode);
        if (string.IsNullOrWhiteSpace(selection.Section) || selection.Section.Length > 200
            || selection.Section.IndexOfAny(['\r', '\n']) >= 0)
            throw new ValidationException("verificationSelection.section", "section must name one selection anchor.",
                SelectionInvalidCode);
    }

    /// <summary>
    /// Reads the named object — never the working tree or HEAD — from the authorized repository:
    /// the path must be a regular blob at that commit, and the section must hold a non-empty table.
    /// </summary>
    private async Task<VerificationSelectionReference> RequireCommittedSelectionAsync(
        VerificationSelectionReference selection, string? repoPath, CancellationToken ct)
    {
        ValidateSelectionShape(selection);
        if (git is null || string.IsNullOrWhiteSpace(repoPath))
            throw new ConflictException("The selection cannot be read from an authorized repository.", SelectionInvalidCode);
        var commit = selection.ArtifactCommitSha!;
        var exists = await git.RunAsync(repoPath, ["cat-file", "-e", commit + "^{commit}"], ct);
        if (!exists.Succeeded)
            throw new ConflictException("Selection commit is not in the authorized repository.", SelectionInvalidCode);
        var tree = await git.RunAsync(repoPath, ["ls-tree", commit, "--", selection.ArtifactPath!], ct);
        var entry = tree.Succeeded ? tree.Output.Trim() : "";
        var match = Regex.Match(entry, @"^(\d{6}) (\w+) [0-9a-f]+\t(.+)$");
        if (!match.Success || match.Groups[2].Value != "blob" || match.Groups[1].Value is not ("100644" or "100755")
            || match.Groups[3].Value != selection.ArtifactPath)
            throw new ConflictException("Selection artifact is not a regular committed file at that commit.", SelectionInvalidCode);
        var content = await git.RunAsync(repoPath, ["show", commit + ":" + selection.ArtifactPath], ct);
        if (!content.Succeeded || !HasSelectionRows(content.Output, selection.Section!))
            throw new ConflictException("Selection section is missing or has no rows at that commit.", SelectionInvalidCode);
        return selection with { ArtifactCommitSha = commit.ToLowerInvariant() };
    }

    internal static bool HasSelectionRows(string markdown, string section)
    {
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var wanted = section.Trim().TrimStart('#').Trim();
        for (var i = 0; i < lines.Length; i++)
        {
            var heading = Regex.Match(lines[i], @"^(#{1,6})\s+(.+?)\s*#*\s*$");
            if (!heading.Success) continue;
            var text = heading.Groups[2].Value;
            if (!string.Equals(text, wanted, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Anchor(text), Anchor(wanted), StringComparison.Ordinal))
                continue;
            var level = heading.Groups[1].Value.Length;
            var tableLines = 0;
            var separator = false;
            for (var j = i + 1; j < lines.Length; j++)
            {
                var next = Regex.Match(lines[j], @"^(#{1,6})\s");
                if (next.Success && next.Groups[1].Value.Length <= level) break;
                var trimmed = lines[j].Trim();
                if (!trimmed.StartsWith('|')) continue;
                if (Regex.IsMatch(trimmed, @"^\|(\s*:?-{3,}:?\s*\|)+$")) { separator = true; continue; }
                tableLines++;
            }
            return separator && tableLines >= 2;
        }
        return false;
    }

    private static string Anchor(string text) =>
        Regex.Replace(Regex.Replace(text.Trim().ToLowerInvariant(), @"[^a-z0-9 _-]", ""), @"\s", "-");

    private async Task<InterimReadinessSnapshot> RequireReadyAsync(string? repoPath, Guid? projectId, CancellationToken ct)
    {
        InterimReadiness verdict;
        try { verdict = await readiness.ReadAsync(repoPath, projectId, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { verdict = InterimReadiness.Unready("readiness_read_failed"); }
        if (!verdict.Ready || verdict.Snapshot is null)
            throw new ConflictException($"Nightly backstop is not ready for Interim ({verdict.Reason}); request Final instead.",
                BackstopUnreadyCode);
        return verdict.Snapshot;
    }

    private static ConflictException Baseline(string detail) =>
        new($"Interim baseline refused: {detail}.", BaselineInvalidCode);

    private static bool SamePath(string? left, string? right) =>
        Infrastructure.Files.InterimVerificationReadinessReader.SamePath(left, right);
}

/// <summary>CARD-0544 D-7. The immutable admission snapshot persisted on an Interim task.</summary>
public sealed record VerificationAdmission(
    int Version,
    VerificationRound Round,
    Guid SubjectTaskId,
    Guid BaselineOutcomeId,
    string BaselineReviewedSha,
    Guid CardId,
    int CardRevision,
    CardVerificationPolicy CardPolicy,
    VerificationSelectionReference Selection,
    InterimReadinessSnapshot Readiness,
    DateTime AdmittedAt)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public static VerificationAdmission? TryRead(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<VerificationAdmission>(json, Json); }
        catch (JsonException) { return null; }
    }
}
