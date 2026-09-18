using System.Globalization;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <param name="Created">0 or 1. One create per tick, oldest <c>ReadySince</c> first.</param>
/// <param name="Reason">
/// Exactly why this tick did what it did. A gate that skipped and a create that 409'd are both
/// "created nothing"; without the reason neither is decidable, in a test or in a log.
/// </param>
public sealed record MutationAutoDispatchTick(
    int Created,
    string Reason,
    Guid? TaskId = null,
    Guid? OperationId = null,
    Guid? CompanionCardId = null);

/// <summary>
/// CARD-0552 D-9. The Diagnose sweep's shape for ONE role: take the oldest Mutation debt row and
/// create ONE sourced Mutation task through the ordinary <see cref="AgentTaskService.CreateAsync"/>
/// admission, under the pause, WIP, window, budget and hold ceilings.
/// </summary>
/// <remarks>
/// It never retries, reroutes, escalates, moves a card, closes a card, creates a card or spawns a
/// session, and it never passes an <c>Ignore*</c> flag: a refusal at the create door is a refusal,
/// and the row stays <c>ready</c> for the next tick or for a human. The only reason this lives in
/// the server rather than a scheduler is that PCs must run as local inherited SourceLanding
/// children under runner custody, so every scheduler would end at <c>POST /api/agent-tasks</c>
/// anyway and the server already holds every gate the decision needs.
/// </remarks>
public sealed class MutationAutoDispatchSweep
{
    public const string ReasonDisabled = "disabled";
    public const string ReasonPaused = "paused";
    public const string ReasonMutationOpen = "mutation-open";
    public const string ReasonOutsideWindow = "outside-window";
    public const string ReasonBudgetMet = "budget-met";
    public const string ReasonRouteHeld = "route-held";
    public const string ReasonNoCandidate = "no-candidate";
    public const string ReasonCreated = "created";
    public const string ReasonRefusedPrefix = "create-refused:";

    /// <summary>The revision reason and editor the companion's auto-dispatch line carries.</summary>
    public const string RevisionReason = "mutation-auto-dispatch";
    public const string RevisionEditor = "mutation-sweep";

    private const int GoalCap = 20_000;

    private readonly AppDbContext _db;
    private readonly AgentTaskService _tasks;
    private readonly DelegationSettings _settings;
    private readonly TimeProvider _clock;
    private readonly OrchestratorControlState _control;
    private readonly ILogger<MutationAutoDispatchSweep> _logger;
    private readonly IModelAvailability? _availability;
    private readonly RoutingPinService? _routingPins;

    public MutationAutoDispatchSweep(
        AppDbContext db,
        AgentTaskService tasks,
        IOptions<DelegationSettings> settings,
        TimeProvider clock,
        OrchestratorControlState control,
        ILogger<MutationAutoDispatchSweep> logger,
        IModelAvailability? availability = null,
        RoutingPinService? routingPins = null)
    {
        _db = db;
        _tasks = tasks;
        _settings = settings.Value;
        _clock = clock;
        _control = control;
        _logger = logger;
        _availability = availability;
        _routingPins = routingPins;
    }

    public async Task<MutationAutoDispatchTick> TickAsync(CancellationToken ct)
    {
        var options = _settings.MutationAutoDispatch;
        if (!options.Enabled) return Skip(ReasonDisabled);
        if (_control.IsPaused) return Skip(ReasonPaused);

        // Fleet-wide, not per project: one battery at a time anywhere. The create-time role cap is
        // per project and stays as shipped; this gate exists so the sweep never burns a 409.
        var mutationOpen = await _db.AgentTasks.AsNoTracking()
            .AnyAsync(t => t.Role == AgentTaskRole.Mutation
                && (t.Status == AgentTaskStatus.Queued
                    || t.Status == AgentTaskStatus.Dispatched
                    || t.Status == AgentTaskStatus.Working
                    || t.Status == AgentTaskStatus.Blocked), ct);
        if (mutationOpen) return Skip(ReasonMutationOpen);

        var now = _clock.GetUtcNow();
        if (!MutationAutoDispatchWindow.IsOpen(options, now)) return Skip(ReasonOutsideWindow);

        if (await DailySpendUsdAsync(ct) >= options.DailyBudgetUsd) return Skip(ReasonBudgetMet);

        var debt = await NextDebtAsync(ct);
        if (debt is null) return Skip(ReasonNoCandidate);

        var companion = await _db.Cards.SingleAsync(c => c.Id == debt.Companion.Id, ct);
        var op = await _db.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.Id == debt.Source.Id, ct);
        var owner = await _db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == debt.Source.TaskId, ct);
        var original = owner.CardId is Guid originalId
            ? await _db.Cards.AsNoTracking().SingleOrDefaultAsync(c => c.Id == originalId, ct)
            : null;

        // The route the create would resolve through. Asking before creating keeps a held model
        // from producing a task that the dispatcher would then sit on.
        var (kind, level, pinId) = await ResolveRouteAsync(companion.Id, ct);
        if (_availability is not null
            && await _availability.IsHeldAsync(kind, ModelLevelAliases.For(kind, level), ct))
        {
            return Skip(ReasonRouteHeld);
        }

        var review = original is null ? null : await _db.AgentTasks.AsNoTracking()
            .Where(t => t.CardId == original.Id && t.Role == AgentTaskRole.Review
                && t.Status == AgentTaskStatus.Succeeded && t.NextStage == PipelineHandoffKind.Land)
            .OrderByDescending(t => t.CompletedAt).ThenByDescending(t => t.Id)
            .FirstOrDefaultAsync(ct);
        var plan = original is null ? null : (await _db.AgentTasks.AsNoTracking()
            .Where(t => t.CardId == original.Id && t.Role == AgentTaskRole.Plan
                && t.Status == AgentTaskStatus.Succeeded && t.DeliverablePath != null)
            .OrderByDescending(t => t.CompletedAt).ThenByDescending(t => t.Id)
            .Select(t => t.DeliverablePath)
            .ToListAsync(ct))
            .FirstOrDefault(AgentTaskPipelineStatusService.IsVerifiedPlanDeliverable);

        var request = new CreateAgentTaskRequest(
            Goal: ComposeGoal(original, companion, owner, review?.Id, plan, op),
            Title: $"post-land mutation checks: {original?.Identifier ?? companion.Identifier}",
            Kind: AgentTaskKind.Worker,
            Role: AgentTaskRole.Mutation,
            Workspace: WorkspaceMode.Worktree,
            WorkingDirectory: op.RepositoryPath,
            Card: companion.Id.ToString("D"),
            ExpectedMinutes: options.ExpectedMinutes,
            SourceLandingOperationId: op.Id);

        AgentTaskCreatedDto created;
        try
        {
            created = await _tasks.CreateAsync(request,
                new AgentTaskService.Caller(null, null, op.RepositoryPath,
                    ProjectId: owner.ProjectId, BoardId: companion.BoardId),
                ct);
        }
        catch (HttpException ex)
        {
            // A quota, capacity, routing or custody refusal ends the tick. The row stays ready.
            _logger.LogInformation(
                "Mutation auto-dispatch refused for operation {Operation} on {Identifier}: {Code}",
                op.Id, companion.Identifier, ex.Code ?? "refused");
            return new MutationAutoDispatchTick(0, ReasonRefusedPrefix + (ex.Code ?? "refused"),
                null, op.Id, companion.Id);
        }

        // The task binding is the durable record; this line only says who started the battery.
        CardRevisionLog.AppendContentEdit(companion, RevisionReason, RevisionEditor, now.UtcDateTime);
        companion.Description = Clip(companion.Description
            + $"\nAuto-dispatched Mutation {created.Id:D} for O={op.Id:D} at "
            + now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        companion.UpdatedAt = now.UtcDateTime;
        companion.ConcurrencyToken = Guid.NewGuid();
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Mutation auto-dispatch created {Task} for operation {Operation} on {Identifier} (pin {Pin})",
            created.Id, op.Id, companion.Identifier, pinId);
        return new MutationAutoDispatchTick(1, ReasonCreated, created.Id, op.Id, companion.Id);
    }

    private MutationAutoDispatchTick Skip(string reason)
    {
        _logger.LogDebug("Mutation auto-dispatch tick created nothing: {Reason}", reason);
        return new MutationAutoDispatchTick(0, reason);
    }

    /// <summary>
    /// The oldest debt row whose operation has NEVER been attempted. A Failed, Canceled or
    /// Blocked prior attempt stays visible in the glance (D-5) and is never auto-retried here:
    /// CARD-0478 D-5 requires an evidence and restoration assessment first.
    /// </summary>
    private async Task<MutationDebtProjection.Debt?> NextDebtAsync(CancellationToken ct)
    {
        var rows = await _db.AgentTaskLandings.AsNoTracking()
            .Where(o => o.VerificationCardId != null
                && (o.Publication == LandPublicationOutcome.Landed
                    || o.Publication == LandPublicationOutcome.AlreadyPresent)
                && o.RemoteConfirmedAt != null)
            .ToListAsync(ct);
        var state = new AgentTaskLandingState();
        var landings = rows.Where(state.HasPublication)
            .Select(o => new MutationDebtProjection.LandingRow(
                o.Id, o.TaskId, o.VerificationCardId!.Value, o.RemoteConfirmedAt!.Value,
                o.OriginalSourceSha, o.VerifiedSourceSha ?? "", o.ObservedRemoteTargetSha ?? "", o.Publication))
            .ToList();
        if (landings.Count == 0) return null;

        var sourced = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.SourceLandingOperationId != null)
            .Select(t => new MutationDebtProjection.SourcedRow(
                t.Id, t.SourceLandingOperationId!.Value, t.CardId, t.Role, t.Status,
                t.CreatedAt, t.DispatchedAt, t.CompletedAt))
            .ToListAsync(ct);

        var boundStages = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.CardId != null)
            .Where(AgentTaskRoles.Stage)
            .Select(t => new AgentTaskPipelineStatusService.TaskRow(
                t.Id, t.Title, t.Role, t.Status, t.CardId, t.AgentName, t.AgentKind, t.ModelLevel,
                t.CreatedAt, t.DispatchedAt, t.CompletedAt, t.AgentSessionId, t.WorkingDirectory,
                t.RepoPath, t.Scope, t.Workspace, t.WorktreeBranch, t.DeliverablePath,
                t.DeliverableRef, t.Complexity, t.FailureReason, t.NextStage, t.NextHandoff, t.RoutingPinId))
            .ToListAsync(ct);

        var cardIds = landings.Select(l => l.VerificationCardId).Distinct().ToList();
        var cards = await _db.Cards.AsNoTracking()
            .Where(c => cardIds.Contains(c.Id))
            .Select(c => new MutationDebtProjection.CardRow(c.Id, c.Identifier, c.Title, c.Status, c.ArchivedAt))
            .ToDictionaryAsync(c => c.Id, ct);

        return MutationDebtProjection.Build(landings, sourced, boundStages, cards)
            .FirstOrDefault(d => !d.AnyAttempt);
    }

    /// <summary>UTC-day sum of Mutation-role <c>CostUsd</c>, on the INJECTED clock.</summary>
    internal async Task<decimal> DailySpendUsdAsync(CancellationToken ct)
    {
        var start = DateTime.SpecifyKind(_clock.GetUtcNow().UtcDateTime.Date, DateTimeKind.Utc);
        var end = start.AddDays(1);
        return await _db.AgentTasks
            .Where(t => t.Role == AgentTaskRole.Mutation && t.CreatedAt >= start && t.CreatedAt < end)
            .SumAsync(t => (decimal?)t.CostUsd, ct) ?? 0m;
    }

    private async Task<(AgentKind Kind, AgentModelLevel Level, Guid? PinId)> ResolveRouteAsync(
        Guid companionCardId, CancellationToken ct)
    {
        var policy = _settings.RolePolicy.TryGetValue(nameof(AgentTaskRole.Mutation), out var entry)
            ? entry : null;
        var kind = policy?.Kind ?? AgentKind.ClaudeCode;
        var level = policy?.Level ?? AgentModelLevel.Frontier;
        if (_routingPins is null) return (kind, level, null);

        var pin = await _routingPins.FindActiveAsync(companionCardId, AgentTaskRole.Mutation, ct)
            ?? await _routingPins.FindActiveAsync(null, AgentTaskRole.Mutation, ct);
        if (pin is null) return (kind, level, null);
        return (pin.AgentKind ?? kind, pin.ModelLevel ?? level, pin.Id);
    }

    /// <summary>
    /// CARD-0552 D-10. The brief carries IDENTITY, not method: the <c>stage-mutation</c> bundle and
    /// <c>docs/orchestration-loop.md</c> carry the method, and duplicating it here is how the two
    /// drift apart.
    /// </summary>
    public static string ComposeGoal(
        Card? original, Card companion, AgentTask owner, Guid? reviewTaskId, string? planPath,
        AgentTaskLanding op)
    {
        var goal = new StringBuilder();
        goal.Append("Run the post-land Mutation battery for ")
            .Append(original?.Identifier ?? companion.Identifier).Append(".\n\n");
        goal.Append("Original card: ").Append(original?.Identifier ?? "none recorded")
            .Append(original is null ? "" : $" ({original.Id:D})").Append('\n');
        goal.Append("Companion card (this task is bound to it): ").Append(companion.Identifier)
            .Append(" (").Append(companion.Id.ToString("D")).Append(")\n");
        goal.Append("Landing owner (Code task): ").Append(owner.Id.ToString("D")).Append('\n');
        goal.Append("Review task: ").Append(reviewTaskId is Guid r ? r.ToString("D") : "none recorded").Append('\n');
        goal.Append("Plan: ").Append(Field(planPath) ?? "not recorded").Append('\n');
        goal.Append("C=").Append(op.OriginalSourceSha).Append('\n');
        goal.Append("O=").Append(op.Id.ToString("D")).Append('\n');
        goal.Append("L=").Append(op.VerifiedSourceSha).Append('\n');
        goal.Append("R=").Append(op.ObservedRemoteTargetSha).Append("\n\n");
        goal.Append("HEAD must equal L=").Append(op.VerifiedSourceSha)
            .Append(" in the snapshot you are given; stop and report if it does not.\n");
        goal.Append("Work from the plan's Verification design: enumerate every PC-n and named variant,\n");
        goal.Append("execute each as a method-scoped red/restore/green cycle, and discover missing controls.\n");
        goal.Append("Report per stage-mutation.md, closing with the executed counts and the evidence root\n");
        goal.Append("the completion note names.\n\n");
        goal.Append("Recording the executed counts, evidence root and restoration verdict on ")
            .Append(companion.Identifier).Append(" at close is the caller's; you only report.\n");
        // No trailing newline: the create door trims the goal, and a composer whose output does
        // not survive storage byte for byte cannot be compared to what the task actually carries.
        goal.Append("Code handoff: ").Append(Field(owner.NextHandoff) ?? "none recorded");
        var text = PostLandVerificationCompanions.Ascii(goal.ToString());
        return text.Length < GoalCap ? text : text[..(GoalCap - 1)];
    }

    private static string? Field(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var single = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return single.Length <= 400 ? single : single[..400];
    }

    private static string Clip(string value) =>
        value.Length <= CardService.MaxDescriptionLength ? value : value[..CardService.MaxDescriptionLength];
}
