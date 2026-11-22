using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Review;

/// <summary>
/// Review runs, findings, gates and the human queue (docs/features/012-review-engine/api.md). Every
/// mutation publishes <c>ReviewRunChanged</c> and records the <see cref="ReviewViewer"/> as the actor.
/// Responses are stored with sync state Pending: nothing is posted to a code host in this slice.
/// </summary>
public sealed class ReviewService(
    AppDbContext db,
    IEventBus events,
    ReviewViewer viewer,
    IOptions<ReviewSettings> settings,
    TimeProvider time,
    ReviewWorkflowService workflows)
{
    public const string RunChangedEvent = "ReviewRunChanged";

    /// <summary>Working minutes after which a queue item warns, and is breached (design 11.3).</summary>
    public const int SlaWarnWorkingMinutes = 240;

    public const int SlaBreachWorkingMinutes = 480;

    private static readonly ReviewFindingStatus[] OpenStatuses =
        [ReviewFindingStatus.Open, ReviewFindingStatus.WillFix, ReviewFindingStatus.Disputed];

    private static readonly ReviewRunStatus[] ActiveStatuses =
        [ReviewRunStatus.Queued, ReviewRunStatus.Running, ReviewRunStatus.AwaitingFixes, ReviewRunStatus.AtGate];

    private static readonly ReviewFindingStatus[] AcceptedStatuses =
        [ReviewFindingStatus.Accepted, ReviewFindingStatus.WillFix, ReviewFindingStatus.Fixed];

    private static readonly JsonSerializerOptions GraphJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public ReviewViewerDto GetViewer() => viewer.ToDto();

    // ---- Runs ----

    public async Task<ReviewListDto<ReviewRunSummaryDto>> ListRunsAsync(
        ReviewRunStatus? status, string? repo, ReviewStage? stage, string? q, CancellationToken ct)
    {
        var query = db.ReviewRuns.AsNoTracking().Include(r => r.Subject).AsQueryable();
        if (status is { } s)
            query = query.Where(r => r.Status == s);
        if (stage is { } st)
            query = query.Where(r => r.Stage == st);
        if (!string.IsNullOrWhiteSpace(repo))
            query = query.Where(r => r.Subject.Repo == repo);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = "%" + q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            var number = int.TryParse(q.Trim().TrimStart('#'), out var n) ? n : (int?)null;
            query = query.Where(r => EF.Functions.ILike(r.Subject.Title, pattern) || EF.Functions.ILike(r.Subject.Repo, pattern)
                || EF.Functions.ILike(r.Subject.AuthorLogin, pattern) || (number != null && r.Subject.PrNumber == number));
        }

        var runs = await query.OrderByDescending(r => r.UpdatedAt).ToListAsync(ct);
        var summaries = await SummariesAsync(runs, ct);
        return new ReviewListDto<ReviewRunSummaryDto>(summaries, summaries.Count);
    }

    public async Task<ReviewListDto<ReviewRunSummaryDto>> GetSummariesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var runs = await db.ReviewRuns.AsNoTracking().Include(r => r.Subject)
            .Where(r => ids.Contains(r.Id)).OrderByDescending(r => r.UpdatedAt).ToListAsync(ct);
        var summaries = await SummariesAsync(runs, ct);
        return new ReviewListDto<ReviewRunSummaryDto>(summaries, summaries.Count);
    }

    public async Task<ReviewRunDetailDto> GetRunAsync(Guid id, CancellationToken ct)
    {
        var run = await db.ReviewRuns.AsNoTracking()
            .Include(r => r.Subject)
            .Include(r => r.Iterations)
            .Include(r => r.Nodes)
            .Include(r => r.Gates).ThenInclude(g => g.Passes)
            .AsSplitQuery()
            .SingleOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("Review run", id);
        var counts = await FindingCountsAsync([id], ct);
        var costs = await CostsAsync([id], ct);
        return ToDetail(run, counts.GetValueOrDefault(id), costs.GetValueOrDefault(id));
    }

    /// <summary>Manual intake: a subject and a queued run at round 1 (design 4.1). Nothing executes yet.</summary>
    public async Task<ReviewRunDetailDto> CreateRunAsync(CreateReviewRunRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Repo))
            throw new ValidationException("repo", "A repo (owner/name) is required.");
        if (request.Host == ReviewHost.GitHub && request.PrNumber is null)
            throw new ValidationException("prNumber", "A GitHub review needs a PR number.");
        if (request.PrNumber is <= 0)
            throw new ValidationException("prNumber", "A PR number is positive.");

        var now = Now();
        var repo = request.Repo.Trim();
        var subject = new ReviewSubject
        {
            Id = Guid.NewGuid(),
            Host = request.Host,
            Repo = repo,
            PrNumber = request.PrNumber,
            Title = string.IsNullOrWhiteSpace(request.Title)
                ? request.PrNumber is { } pr ? $"{repo} #{pr}" : $"{repo} local review"
                : request.Title.Trim(),
            Url = request.Host == ReviewHost.GitHub && request.PrNumber is { } number ? $"https://github.com/{repo}/pull/{number}" : null,
            AuthorLogin = request.Host == ReviewHost.Local ? viewer.Login : "",
            OpenedByLogin = request.Host == ReviewHost.Local ? viewer.Login : "",
            BaseSha = request.BaseSha?.Trim() ?? "",
            HeadSha = request.HeadSha?.Trim() ?? "",
            CreatedAt = now,
        };
        var run = new ReviewRun
        {
            Id = Guid.NewGuid(),
            SubjectId = subject.Id,
            Subject = subject,
            Status = ReviewRunStatus.Queued,
            Stage = ReviewStage.Intake,
            Round = 1,
            WaitingOn = ReviewWaitingOn.Bot,
            WorkflowName = workflows.Default.Name,
            WorkflowHash = workflows.DefaultHash,
            WorkflowRevisionId = await EnsureDefaultRevisionAsync(ct),
            StartedAt = now,
            UpdatedAt = now,
        };
        run.Iterations.Add(new ReviewIteration { Id = Guid.NewGuid(), RunId = run.Id, Iteration = 1, Round = 1, Trigger = "manual", StartedAt = now });
        run.Nodes.Add(PendingIntake(run.Id, 1));
        db.ReviewSubjects.Add(subject);
        db.ReviewRuns.Add(run);
        await db.SaveChangesAsync(ct);
        await PublishAsync(run.Id, ct);
        return await GetRunAsync(run.Id, ct);
    }

    /// <summary>The manual Refresh (design 8.5). No code host is wired yet, so it only re-announces the run.</summary>
    public async Task RefreshAsync(Guid id, CancellationToken ct)
    {
        if (!await db.ReviewRuns.AnyAsync(r => r.Id == id, ct))
            throw new NotFoundException("Review run", id);
        // TODO(review-engine P1): ask ICodeHost for new events (design 8.5) once the code host exists.
        await PublishAsync(id, ct);
    }

    // ---- Workflow revisions ----

    /// <summary>The stored revision of the default workflow, created on first use (design 3.3).</summary>
    public async Task<Guid> EnsureDefaultRevisionAsync(CancellationToken ct)
    {
        var hash = workflows.DefaultHash;
        var existing = await db.ReviewWorkflowRevisions.Where(w => w.Hash == hash).Select(w => (Guid?)w.Id).SingleOrDefaultAsync(ct);
        if (existing is { } id)
            return id;
        var revision = new ReviewWorkflowRevision
        {
            Id = Guid.NewGuid(),
            Name = workflows.Default.Name,
            Hash = hash,
            GraphJson = JsonSerializer.Serialize(ReviewWorkflowService.ToDto(workflows.Default, revisionId: null), GraphJson),
            CreatedAt = Now(),
        };
        db.ReviewWorkflowRevisions.Add(revision);
        try
        {
            await db.SaveChangesAsync(ct);
            return revision.Id;
        }
        catch (DbUpdateException)
        {
            // A concurrent first use stored the same hash; use that row.
            db.Entry(revision).State = EntityState.Detached;
            return await db.ReviewWorkflowRevisions.Where(w => w.Hash == hash).Select(w => w.Id).SingleAsync(ct);
        }
    }

    public async Task<WorkflowGraphDto> GetWorkflowRevisionAsync(Guid id, CancellationToken ct)
    {
        var revision = await db.ReviewWorkflowRevisions.AsNoTracking().SingleOrDefaultAsync(w => w.Id == id, ct)
            ?? throw new NotFoundException("Review workflow revision", id);
        var graph = JsonSerializer.Deserialize<WorkflowGraphDto>(revision.GraphJson, GraphJson)
            ?? throw new InvalidOperationException($"Workflow revision {id} holds no graph.");
        return graph with { RevisionId = revision.Id, Hash = revision.Hash };
    }

    // ---- Findings ----

    public async Task<ReviewListDto<ReviewFindingDto>> ListFindingsAsync(Guid runId, ReviewFindingStatus? status, CancellationToken ct)
    {
        if (!await db.ReviewRuns.AnyAsync(r => r.Id == runId, ct))
            throw new NotFoundException("Review run", runId);
        var query = db.ReviewFindings.AsNoTracking().Include(f => f.Events).Where(f => f.RunId == runId);
        if (status is { } s)
            query = query.Where(f => f.Status == s);
        var findings = await query.ToListAsync(ct);
        var items = findings.OrderBy(f => KeyOrder(f.Key)).ThenBy(f => f.Key, StringComparer.Ordinal).Select(ToDto).ToList();
        return new ReviewListDto<ReviewFindingDto>(items, items.Count);
    }

    public async Task<ReviewFindingDto> RespondAsync(Guid findingId, FindingResponseRequest request, CancellationToken ct)
    {
        var finding = await db.ReviewFindings.Include(f => f.Events).SingleOrDefaultAsync(f => f.Id == findingId, ct)
            ?? throw new NotFoundException("Review finding", findingId);
        var run = await db.ReviewRuns.SingleAsync(r => r.Id == finding.RunId, ct);
        EnsureRespondable(run);
        Apply(finding, request);
        run.UpdatedAt = Now();
        await db.SaveChangesAsync(ct);
        await PublishAsync(run.Id, ct);
        return ToDto(finding);
    }

    /// <summary>One submitted review: every response applies, or none does.</summary>
    public async Task<ReviewItemsDto<ReviewFindingDto>> RespondBatchAsync(Guid runId, BatchFindingResponsesRequest request, CancellationToken ct)
    {
        var run = await db.ReviewRuns.SingleOrDefaultAsync(r => r.Id == runId, ct)
            ?? throw new NotFoundException("Review run", runId);
        EnsureRespondable(run);
        if (request.Responses is not { Count: > 0 })
            throw new ValidationException("responses", "A submitted review needs at least one response.");

        var ids = request.Responses.Select(r => r.FindingId).Distinct().ToList();
        var findings = await db.ReviewFindings.Include(f => f.Events).Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);
        foreach (var response in request.Responses)
        {
            if (!findings.TryGetValue(response.FindingId, out var finding))
                throw new NotFoundException("Review finding", response.FindingId);
            if (finding.RunId != runId)
                throw new ValidationException("responses", $"Finding {response.FindingId} belongs to another run.");
            Apply(finding, response.ToRequest());
        }

        run.UpdatedAt = Now();
        await db.SaveChangesAsync(ct);
        await PublishAsync(runId, ct);
        return new ReviewItemsDto<ReviewFindingDto>(ids.Select(id => ToDto(findings[id])).ToList());
    }

    private void Apply(ReviewFinding finding, FindingResponseRequest request)
    {
        var blocking = finding.Severity == ReviewSeverity.Blocker && OpenStatuses.Contains(finding.Status);
        switch (request.Action)
        {
            case ReviewFindingAction.WillFix:
                finding.Status = ReviewFindingStatus.WillFix;
                break;
            case ReviewFindingAction.Fixed:
                finding.Status = ReviewFindingStatus.Fixed;
                break;
            case ReviewFindingAction.Dispute:
                finding.Status = ReviewFindingStatus.Disputed;
                break;
            case ReviewFindingAction.Defer:
                if (blocking)
                    throw new ConflictException($"Finding {finding.Key} is an open blocker; it cannot be deferred (D-21).", "review_blocker_open");
                finding.Status = ReviewFindingStatus.Deferred;
                break;
            case ReviewFindingAction.Dismiss:
                if (request.ReasonCategory is null)
                    throw new ValidationException("reasonCategory", "A dismissal needs a reason category.");
                if (blocking)
                    throw new ConflictException($"Finding {finding.Key} is a blocker; clear it with NotABlocker (design 11.6).", "review_blocker_open");
                // Path, Repo and Team scopes record the proposal intent on the event only; the learn
                // PR or team proposal of design 11.7 comes later. The instance itself is dismissed.
                finding.Status = ReviewFindingStatus.Dismissed;
                break;
            case ReviewFindingAction.NotABlocker:
                if (finding.Severity != ReviewSeverity.Blocker)
                    throw new ConflictException($"Finding {finding.Key} is not a blocker.", "review_not_a_blocker");
                if (request.ReasonCategory is not (ReviewReasonCategory.FalsePositive or ReviewReasonCategory.ByDesign or ReviewReasonCategory.AcceptedRisk))
                    throw new ValidationException("reasonCategory", "Clearing a blocker needs FalsePositive, ByDesign or AcceptedRisk (design 11.6).");
                // TODO(review-engine trust): a clear counts alone only at or above clearBlocker trust;
                // below it the clear is pending-confirmation and the blocker stays open (design 11.6, 11.9).
                finding.Status = ReviewFindingStatus.Dismissed;
                break;
            case ReviewFindingAction.Question:
            case ReviewFindingAction.Reply:
                if (string.IsNullOrWhiteSpace(request.Text))
                    throw new ValidationException("text", $"A {request.Action} needs text.");
                break;
        }

        var now = Now();
        finding.Events.Add(new ReviewFindingEvent
        {
            Id = Guid.NewGuid(),
            FindingId = finding.Id,
            Action = request.Action,
            ActorLogin = viewer.Login,
            ActorDisplayName = viewer.DisplayName,
            ReasonCategory = request.ReasonCategory,
            Scope = request.Action is ReviewFindingAction.Dismiss ? request.Scope ?? ReviewDismissScope.Instance : request.Scope,
            Text = string.IsNullOrWhiteSpace(request.Text) ? null : request.Text.Trim(),
            SyncState = ReviewSyncState.Pending,
            CreatedAt = now,
        });
        finding.SyncState = ReviewSyncState.Pending;
        finding.UpdatedAt = now;
    }

    private static void EnsureRespondable(ReviewRun run)
    {
        if (run.Status is ReviewRunStatus.Closed or ReviewRunStatus.Merged)
            throw new ConflictException($"Review run {run.Id} is {run.Status}; it takes no more responses.", "review_run_finished");
    }

    // ---- Gates and the queue ----

    /// <summary>
    /// Mine: assigned to the viewer (or <paramref name="login"/>), plus unassigned gates of the viewer's
    /// teams. Teams: every open gate of the viewer's teams. All: every open gate (design 11.3).
    /// </summary>
    public async Task<ReviewListDto<QueueItemDto>> GetQueueAsync(string? scope, string? login, CancellationToken ct)
    {
        var kind = string.IsNullOrWhiteSpace(scope) ? "All" : scope.Trim();
        var me = (string.IsNullOrWhiteSpace(login) ? viewer.Login : login.Trim()).ToLower();
        var teams = viewer.Teams.ToList();
        var query = db.ReviewGates.AsNoTracking().Include(g => g.Passes).Where(g => g.Status != ReviewQueueItemStatus.Done);
        switch (kind.ToLowerInvariant())
        {
            case "mine":
                query = query.Where(g => (g.AssigneeLogin != null && g.AssigneeLogin.ToLower() == me)
                    || (g.AssigneeLogin == null && g.TeamKey != null && teams.Contains(g.TeamKey)));
                break;
            case "teams":
                // TODO(review-engine teams): memberships come from org sync (design 12); with none
                // configured for the viewer, every team-owned gate is listed.
                query = teams.Count > 0
                    ? query.Where(g => g.TeamKey != null && teams.Contains(g.TeamKey))
                    : query.Where(g => g.TeamKey != null);
                break;
            case "all":
                break;
            default:
                throw new ValidationException("scope", "scope must be Mine, Teams or All.");
        }

        var gates = await query.OrderBy(g => g.OpenedAt).ToListAsync(ct);
        var runIds = gates.Select(g => g.RunId).Distinct().ToList();
        var runs = await db.ReviewRuns.AsNoTracking().Include(r => r.Subject).Where(r => runIds.Contains(r.Id)).ToListAsync(ct);
        var authors = runs.ToDictionary(r => r.Id, r => r.Subject.AuthorLogin);
        var summaries = (await SummariesAsync(runs, ct)).ToDictionary(s => s.Id);
        var now = Now();
        var items = gates.Where(g => summaries.ContainsKey(g.RunId)).Select(g =>
        {
            var age = WorkingMinutes(g.OpenedAt, now);
            var sla = age >= SlaBreachWorkingMinutes ? "Breached" : age >= SlaWarnWorkingMinutes ? "Warn" : "Ok";
            return new QueueItemDto(ToDto(g, authors[g.RunId]), summaries[g.RunId], age, SlaBreachWorkingMinutes, sla);
        }).ToList();
        return new ReviewListDto<QueueItemDto>(items, items.Count);
    }

    /// <summary>
    /// Assigns a gate. With no login, picks at random from the subject's user code owners, excluding the
    /// author and anyone who passed (design 11.4; team members and load weighting come later).
    /// </summary>
    public async Task<ReviewGateDto> AssignAsync(Guid gateId, string? login, CancellationToken ct)
    {
        var gate = await OpenGateAsync(gateId, ct);
        var subject = await db.ReviewRuns.Where(r => r.Id == gate.RunId).Select(r => r.Subject).SingleAsync(ct);
        string assignee;
        if (!string.IsNullOrWhiteSpace(login))
        {
            assignee = login.Trim();
        }
        else
        {
            var excluded = new HashSet<string>(gate.Passes.Select(p => p.Login), StringComparer.OrdinalIgnoreCase) { subject.AuthorLogin };
            var candidates = subject.OwnerUsers
                .Select(o => o.TrimStart('@'))
                .Where(o => o.Length > 0 && !excluded.Contains(o))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (candidates.Count == 0)
                throw new ConflictException("No eligible reviewer: every code owner is the author or has passed.", "review_no_eligible_reviewer");
            assignee = candidates[Random.Shared.Next(candidates.Count)];
        }

        gate.AssigneeLogin = assignee;
        gate.AssigneeDisplayName = settings.Value.Viewers.TryGetValue(assignee, out var known) ? known.DisplayName : null;
        gate.Status = ReviewQueueItemStatus.Assigned;
        await TouchAndSaveAsync(gate.RunId, ct);
        return ToDto(gate, subject.AuthorLogin);
    }

    /// <summary>"Not me": the viewer returns the gate to the team queue with a reason and is skipped by the next pick.</summary>
    public async Task<ReviewGateDto> PassAsync(Guid gateId, string? reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ValidationException("reason", "Passing a review needs a reason.");
        var gate = await OpenGateAsync(gateId, ct);
        var author = await db.ReviewRuns.Where(r => r.Id == gate.RunId).Select(r => r.Subject.AuthorLogin).SingleAsync(ct);
        var pass = new ReviewGatePass { Id = Guid.NewGuid(), GateId = gate.Id, Login = viewer.Login, Reason = reason.Trim(), At = Now() };
        gate.Passes.Add(pass);
        gate.AssigneeLogin = null;
        gate.AssigneeDisplayName = null;
        gate.Status = ReviewQueueItemStatus.Unassigned;
        await TouchAndSaveAsync(gate.RunId, ct);
        return ToDto(gate, author);
    }

    /// <summary>
    /// A human gate decision (design 3.3 gates): Approve approves the run; AnotherRound starts the next
    /// round, queued, carrying the steer prompt; Close closes the review. The author cannot decide.
    /// </summary>
    public async Task<ReviewRunDetailDto> DecideAsync(Guid gateId, GateDecisionRequest request, CancellationToken ct)
    {
        var gate = await OpenGateAsync(gateId, ct);
        var run = await db.ReviewRuns.Include(r => r.Subject).Include(r => r.Nodes).Include(r => r.Iterations)
            .AsSplitQuery().SingleAsync(r => r.Id == gate.RunId, ct);
        if (run.Status is ReviewRunStatus.Closed or ReviewRunStatus.Merged)
            throw new ConflictException($"Review run {run.Id} is {run.Status}.", "review_run_finished");
        if (CannotDecideReason(gate, run.Subject.AuthorLogin) is { } reason)
            throw new ForbiddenException(reason, "review_gate_not_decider");
        if (!AllowedDecisions(gate).Contains(request.Decision))
            throw new ConflictException($"Gate {gate.Id} does not allow {request.Decision}.", "review_gate_decision_not_allowed");

        var now = Now();
        var steer = string.IsNullOrWhiteSpace(request.SteerPrompt) ? null : request.SteerPrompt.Trim();
        gate.Status = ReviewQueueItemStatus.Done;
        gate.Decision = request.Decision;
        gate.DecidedAt = now;
        gate.DecidedBy = viewer.Login;
        gate.SteerPrompt = steer;
        if (gate.NodeId is not null)
        {
            foreach (var waiting in run.Nodes.Where(n => n.NodeId == gate.NodeId && n.Status is ReviewNodeStatus.Waiting or ReviewNodeStatus.Running))
            {
                waiting.Status = ReviewNodeStatus.Succeeded;
                waiting.Outcome = DecisionLabel(request.Decision);
                waiting.CompletedAt = now;
                waiting.Summary = $"{request.Decision} by {viewer.Login}";
            }
        }

        switch (request.Decision)
        {
            case ReviewGateDecision.Approve:
                run.Status = ReviewRunStatus.Approved;
                run.Stage = ReviewStage.Approval;
                run.WaitingOn = null;
                break;
            case ReviewGateDecision.AnotherRound:
                run.Round += 1;
                run.BotOnlyRounds = 0;
                run.Status = ReviewRunStatus.Queued;
                run.Stage = ReviewStage.Intake;
                run.WaitingOn = ReviewWaitingOn.Bot;
                run.SteerPrompt = steer;
                var iteration = Math.Max(run.Nodes.Select(n => n.Iteration).DefaultIfEmpty(0).Max(),
                    run.Iterations.Select(i => i.Iteration).DefaultIfEmpty(0).Max()) + 1;
                db.ReviewIterations.Add(new ReviewIteration
                {
                    Id = Guid.NewGuid(),
                    RunId = run.Id,
                    Iteration = iteration,
                    Round = run.Round,
                    Trigger = "another-round",
                    StartedAt = now,
                });
                db.ReviewNodeExecutions.Add(PendingIntake(run.Id, iteration));
                break;
            case ReviewGateDecision.Close:
                run.Status = ReviewRunStatus.Closed;
                run.Stage = ReviewStage.Done;
                run.WaitingOn = null;
                break;
        }

        run.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await PublishAsync(run.Id, ct);
        return await GetRunAsync(run.Id, ct);
    }

    private async Task<ReviewGate> OpenGateAsync(Guid gateId, CancellationToken ct)
    {
        var gate = await db.ReviewGates.Include(g => g.Passes).SingleOrDefaultAsync(g => g.Id == gateId, ct)
            ?? throw new NotFoundException("Review gate", gateId);
        if (gate.Status == ReviewQueueItemStatus.Done)
            throw new ConflictException($"Review gate {gateId} is already decided.", "review_gate_decided");
        return gate;
    }

    private async Task TouchAndSaveAsync(Guid runId, CancellationToken ct)
    {
        var run = await db.ReviewRuns.SingleAsync(r => r.Id == runId, ct);
        run.UpdatedAt = Now();
        await db.SaveChangesAsync(ct);
        await PublishAsync(runId, ct);
    }

    /// <summary>The gate node's decisions in the default workflow; review and approval gates fall back to all three.</summary>
    private IReadOnlyList<ReviewGateDecision> AllowedDecisions(ReviewGate gate)
    {
        var declared = gate.NodeId is null ? [] : workflows.Default.Node(gate.NodeId)?.Decisions ?? [];
        var mapped = declared.Select(d => d switch
        {
            "approve" => ReviewGateDecision.Approve,
            "another-round" => ReviewGateDecision.AnotherRound,
            "close" => ReviewGateDecision.Close,
            _ => (ReviewGateDecision?)null,
        }).OfType<ReviewGateDecision>().ToList();
        if (mapped.Count > 0)
            return mapped;
        return gate.Kind is ReviewGateKind.ReviewGate or ReviewGateKind.ApprovalGate
            ? [ReviewGateDecision.Approve, ReviewGateDecision.AnotherRound, ReviewGateDecision.Close]
            : [];
    }

    /// <summary>Why the viewer may not decide this gate, or null (design 11.10: not the author).</summary>
    private string? CannotDecideReason(ReviewGate gate, string authorLogin)
    {
        if (gate.Status == ReviewQueueItemStatus.Done)
            return "Already decided";
        if (authorLogin.Length > 0 && string.Equals(viewer.Login, authorLogin, StringComparison.OrdinalIgnoreCase))
            return "You are the author";
        // TODO(review-engine trust): deciders must also be at or above the gate's trust threshold (design 11.9, 11.10).
        return AllowedDecisions(gate).Count == 0 ? "This gate's decisions are not supported yet" : null;
    }

    private static string DecisionLabel(ReviewGateDecision decision) => decision switch
    {
        ReviewGateDecision.AnotherRound => "another-round",
        _ => decision.ToString().ToLowerInvariant(),
    };

    // ---- KPIs ----

    public async Task<ReviewKpisDto> GetKpisAsync(CancellationToken ct)
    {
        var inReview = await db.ReviewRuns.CountAsync(r => ActiveStatuses.Contains(r.Status), ct);
        var waitingOnHumans = await db.ReviewRuns.CountAsync(r => r.Status == ReviewRunStatus.AtGate || r.WaitingOn == ReviewWaitingOn.Humans, ct);
        var averageRounds = await db.ReviewRuns.AnyAsync(ct) ? await db.ReviewRuns.AverageAsync(r => (double)r.Round, ct) : 0;
        var today = new DateTime(Now().Date.Ticks, DateTimeKind.Utc);
        var costToday = await db.ReviewNodeExecutions.Where(n => (n.CompletedAt ?? n.StartedAt) >= today).SumAsync(n => n.CostUsd, ct);
        var totalCost = await db.ReviewRuns.SumAsync(r => r.CostUsd, ct);
        var accepted = await db.ReviewFindings.CountAsync(f => AcceptedStatuses.Contains(f.Status), ct);
        var usage = await db.ReviewNodeExecutions.AsNoTracking()
            .Where(n => n.Model != null)
            .GroupBy(n => new { n.Model, n.Provider, n.Transport })
            .Select(g => new { g.Key.Model, g.Key.Provider, g.Key.Transport, Calls = g.Count(), Cost = g.Sum(n => n.CostUsd) })
            .ToListAsync(ct);
        return new ReviewKpisDto(
            inReview,
            waitingOnHumans,
            Math.Round(averageRounds, 2),
            costToday,
            accepted == 0 ? null : Math.Round(totalCost / accepted, 4),
            usage.OrderByDescending(u => u.Calls)
                .Select(u => new ModelUsageDto(u.Model!, u.Provider ?? "", u.Transport ?? ReviewTransport.Http, u.Calls, u.Cost))
                .ToList());
    }

    // ---- Mapping ----

    private readonly record struct FindingCounts(int Open, int Blockers);

    private async Task<Dictionary<Guid, FindingCounts>> FindingCountsAsync(IReadOnlyCollection<Guid> runIds, CancellationToken ct)
    {
        var rows = await db.ReviewFindings.AsNoTracking()
            .Where(f => runIds.Contains(f.RunId) && OpenStatuses.Contains(f.Status))
            .GroupBy(f => f.RunId)
            .Select(g => new { RunId = g.Key, Open = g.Count(), Blockers = g.Count(f => f.Severity == ReviewSeverity.Blocker) })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.RunId, r => new FindingCounts(r.Open, r.Blockers));
    }

    /// <summary>Actual (API calls) and estimated (seat API-equivalent, D-20) cost per run, from its node executions.</summary>
    private readonly record struct RunCosts(decimal Actual, decimal Estimated);

    private async Task<Dictionary<Guid, RunCosts>> CostsAsync(IReadOnlyCollection<Guid> runIds, CancellationToken ct)
    {
        var rows = await db.ReviewNodeExecutions.AsNoTracking()
            .Where(n => runIds.Contains(n.RunId) && n.CostUsd != 0)
            .GroupBy(n => new { n.RunId, Estimated = n.CostBasis == "api-equivalent" })
            .Select(g => new { g.Key.RunId, g.Key.Estimated, Cost = g.Sum(n => n.CostUsd) })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.RunId).ToDictionary(g => g.Key, g => new RunCosts(
            g.Where(r => !r.Estimated).Sum(r => r.Cost), g.Where(r => r.Estimated).Sum(r => r.Cost)));
    }

    private async Task<List<ReviewRunSummaryDto>> SummariesAsync(List<ReviewRun> runs, CancellationToken ct)
    {
        var ids = runs.Select(r => r.Id).ToList();
        var counts = await FindingCountsAsync(ids, ct);
        var costs = await CostsAsync(ids, ct);
        var models = (await db.ReviewNodeExecutions.AsNoTracking()
                .Where(n => ids.Contains(n.RunId) && n.Model != null)
                .Select(n => new { n.RunId, n.Model })
                .Distinct()
                .ToListAsync(ct))
            .GroupBy(m => m.RunId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(m => m.Model!).Order(StringComparer.Ordinal).ToList());
        return runs.Select(r => ToSummary(r, counts.GetValueOrDefault(r.Id), costs.GetValueOrDefault(r.Id), models.GetValueOrDefault(r.Id) ?? []))
            .ToList();
    }

    private static ReviewRunSummaryDto ToSummary(ReviewRun run, FindingCounts counts, RunCosts costs, IReadOnlyList<string> models) => new()
    {
        Id = run.Id,
        Subject = ToDto(run.Subject),
        Status = run.Status,
        Stage = run.Stage,
        Round = run.Round,
        BotOnlyRounds = run.BotOnlyRounds,
        NeedsHumanApproval = run.NeedsHumanApproval,
        OpenFindings = counts.Open,
        OpenBlockers = counts.Blockers,
        Models = models,
        CostUsd = costs.Actual + costs.Estimated,
        CostActualUsd = costs.Actual,
        CostEstimatedUsd = costs.Estimated,
        BlastRadius = run.BlastReach is { } reach
            ? new BlastRadiusDto(reach, run.BlastExternalContract ?? 1, run.BlastData ?? 1, run.BlastRollbackAtRelease ?? 1,
                run.BlastReversalAfterMonths ?? 1, run.BlastExposure ?? 1)
            : null,
        StartedAt = run.StartedAt,
        UpdatedAt = run.UpdatedAt,
        WaitingOn = run.WaitingOn,
    };

    private ReviewRunDetailDto ToDetail(ReviewRun run, FindingCounts counts, RunCosts costs)
    {
        var models = run.Nodes.Where(n => n.Model != null).Select(n => n.Model!).Distinct().Order(StringComparer.Ordinal).ToList();
        var summary = ToSummary(run, counts, costs, models);
        return new ReviewRunDetailDto
        {
            Id = summary.Id,
            Subject = summary.Subject,
            Status = summary.Status,
            Stage = summary.Stage,
            Round = summary.Round,
            BotOnlyRounds = summary.BotOnlyRounds,
            NeedsHumanApproval = summary.NeedsHumanApproval,
            OpenFindings = summary.OpenFindings,
            OpenBlockers = summary.OpenBlockers,
            Models = summary.Models,
            CostUsd = summary.CostUsd,
            CostActualUsd = summary.CostActualUsd,
            CostEstimatedUsd = summary.CostEstimatedUsd,
            BlastRadius = summary.BlastRadius,
            StartedAt = summary.StartedAt,
            UpdatedAt = summary.UpdatedAt,
            WaitingOn = summary.WaitingOn,
            Categories = run.Categories,
            WorkflowRevisionId = run.WorkflowRevisionId,
            Iterations = run.Iterations.OrderBy(i => i.Iteration)
                .Select(i => new ReviewIterationDto(i.Iteration, i.Round, i.Trigger, i.StartedAt)).ToList(),
            Nodes = run.Nodes.OrderBy(n => n.Iteration).ThenBy(n => n.StartedAt ?? DateTime.MaxValue).ThenBy(n => n.NodeId, StringComparer.Ordinal)
                .ThenBy(n => n.ItemKey, StringComparer.Ordinal)
                .Select(ToDto).ToList(),
            Gates = run.Gates.OrderBy(g => g.OpenedAt).Select(g => ToDto(g, run.Subject.AuthorLogin)).ToList(),
        };
    }

    public static ReviewSubjectDto ToDto(ReviewSubject s) => new(
        s.Id, s.Host, s.Repo, s.PrNumber, s.Title, s.Url, s.AuthorLogin, s.AuthorDisplayName, s.AuthorResolvedBy,
        s.OpenedByLogin.Length > 0 ? s.OpenedByLogin : s.AuthorLogin,
        s.BaseRef, s.HeadRef, s.BaseSha, s.HeadSha,
        s.OwnerTeams.Select(t => new ReviewOwnerDto("Team", t)).Concat(s.OwnerUsers.Select(u => new ReviewOwnerDto("User", u))).ToList());

    public static NodeExecutionDto ToDto(ReviewNodeExecution n) => new(
        n.Id, n.NodeId, n.Iteration, n.Kind, n.Status, n.ItemKey, n.Outcome, n.Provider, n.Model, n.Transport, n.InputTokens,
        n.OutputTokens, n.CostUsd, n.StartedAt, n.CompletedAt, n.Summary);

    /// <summary>A gate as the viewer sees it: which decisions it allows and whether the viewer may decide.</summary>
    public ReviewGateDto ToDto(ReviewGate g, string authorLogin)
    {
        var cannot = CannotDecideReason(g, authorLogin);
        return new ReviewGateDto(
            g.Id, g.RunId, g.Kind, g.Status, g.TeamKey, g.AssigneeLogin, g.AssigneeDisplayName, g.OpenedAt, g.DecidedAt, g.DecidedBy,
            g.Decision, g.SteerPrompt, g.Reasons, AllowedDecisions(g), cannot is null, cannot,
            g.Passes.OrderBy(p => p.At).Select(p => new GatePassDto(p.Login, p.Reason, p.At)).ToList());
    }

    public static ReviewFindingDto ToDto(ReviewFinding f) => new(
        f.Id, f.RunId, f.Key, f.CheckId, f.CheckWhy, f.Severity, f.Flags, f.BypassesRoundBar, f.Confidence, f.FilePath,
        f.LineStart, f.LineEnd, f.Anchor, f.Title, f.Body, f.Suggestion, f.Hunk, f.Status, f.RoundRaised, f.Posted, f.SyncState,
        f.ExternalThreadUrl,
        f.Events.OrderBy(e => e.CreatedAt).Select(e => new FindingEventDto(e.Id, e.Action, e.ActorLogin, e.ActorDisplayName,
            e.ReasonCategory, e.Scope, e.Text, e.SyncState, e.CreatedAt)).ToList());

    private static ReviewNodeExecution PendingIntake(Guid runId, int iteration) => new()
    {
        Id = Guid.NewGuid(),
        RunId = runId,
        NodeId = "intake",
        Iteration = iteration,
        Kind = ReviewNodeKind.Code,
        Status = ReviewNodeStatus.Pending,
    };

    private static int KeyOrder(string key) => key.Length > 1 && int.TryParse(key.AsSpan(1), out var n) ? n : int.MaxValue;

    /// <summary>Working minutes, Monday to Friday 09:00-17:00 UTC, until work calendars exist (design 11.3).</summary>
    public static int WorkingMinutes(DateTime from, DateTime to)
    {
        if (to <= from)
            return 0;
        var minutes = 0.0;
        for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            var start = day.AddHours(9);
            var end = day.AddHours(17);
            var overlapStart = from > start ? from : start;
            var overlapEnd = to < end ? to : end;
            if (overlapEnd > overlapStart)
                minutes += (overlapEnd - overlapStart).TotalMinutes;
        }

        return (int)minutes;
    }

    private DateTime Now() => time.GetUtcNow().UtcDateTime;

    private Task PublishAsync(Guid runId, CancellationToken ct) =>
        events.PublishToAllAsync(RunChangedEvent, new ReviewRunChangedEvent(runId), ct);
}
